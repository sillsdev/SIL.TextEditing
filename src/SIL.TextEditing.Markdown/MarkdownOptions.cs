using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    /// <summary>What the converter needs from its host: the writing systems that unmarked text and <c>v</c> stand for, the
    /// style names the block notation uses, which styles exist, and what an object run's data means. Everything is
    /// optional; the defaults need no project (ADR 0011).</summary>
    public sealed class MarkdownOptions
    {
        IWritingSystemMapper _writingSystems = new FixedWritingSystems("en", "en");
        MarkdownStyleNames _styleNames = new MarkdownStyleNames();
        IObjectMapper _objects = DefaultObjectMapper.Instance;

        /// <summary>The writing system of unmarked text and of <c>v</c>. Default: <see cref="FixedWritingSystems"/> with "en" for both. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IWritingSystemMapper WritingSystems
        {
            get => _writingSystems;
            set => _writingSystems = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The paragraph and hyperlink style names the notation uses. Default: the spec's names. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public MarkdownStyleNames StyleNames
        {
            get => _styleNames;
            set => _styleNames = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Whether a style name exists in the project; null accepts every name.</summary>
        public IStyleCatalog? Styles { get; set; }

        /// <summary>What an object run's data means. Default: <see cref="DefaultObjectMapper"/>. Never null.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IObjectMapper Objects
        {
            get => _objects;
            set => _objects = value ?? throw new ArgumentNullException(nameof(value));
        }

    }

    /// <summary>The writing systems the notation leaves implicit (spec 3.2). A run's <see cref="Run.Ws"/> is a language tag, so
    /// the host supplies the two defaults and says which tags exist; it needs no libpalaso type here.</summary>
    public interface IWritingSystemMapper
    {
        /// <summary>The writing system of unmarked text: the project's default analysis (or the base's, at import).</summary>
        string DefaultAnalysis { get; }

        /// <summary>The writing system <c>v</c> stands for: the project's default vernacular.</summary>
        string DefaultVernacular { get; }

        /// <summary>True when the tag is a writing system of the project; false makes the parser report <c>gdr.ws.unknown</c>.</summary>
        bool IsKnown(string tag);
    }

    /// <summary>Optional: a writing-system mapper that also lists the tags of the project. When <see cref="MarkdownOptions.WritingSystems"/>
    /// implements it, <c>gdr.ws.unknown</c> carries them as its choices and the closest ones as its nearest.</summary>
    public interface IKnownWritingSystems
    {
        /// <summary>The tags of the project's writing systems (the spec's choices for <c>gdr.ws.unknown</c>: its analysis and vernacular systems).</summary>
        IReadOnlyList<string> KnownTags { get; }
    }

    /// <summary>Optional: a writing-system mapper that can say more than yes or no about a tag. When <see cref="MarkdownOptions.WritingSystems"/>
    /// implements it, the parser calls <see cref="CheckWritingSystem"/> instead of <see cref="IWritingSystemMapper.IsKnown"/>.</summary>
    public interface IWritingSystemChecker
    {
        /// <summary>Null when the tag is fine. Otherwise the problem to report on the tag: an error with the default id <c>gdr.ws.unknown</c>, or a
        /// warning or suggestion for a tag that is accepted but worth a word (a legacy system); the text keeps the tag either way. The host's
        /// <see cref="MarkdownProblem.Choices"/> and <see cref="MarkdownProblem.Nearest"/> are reported; when it leaves them empty and also
        /// implements <see cref="IKnownWritingSystems"/>, its list and the closest tags fill them.</summary>
        MarkdownProblem? CheckWritingSystem(string tag);
    }

    /// <summary>A mapper with two fixed defaults that knows every tag.</summary>
    public sealed class FixedWritingSystems : IWritingSystemMapper
    {
        public FixedWritingSystems(string defaultAnalysis, string defaultVernacular)
        {
            DefaultAnalysis = defaultAnalysis ?? throw new ArgumentNullException(nameof(defaultAnalysis));
            DefaultVernacular = defaultVernacular ?? throw new ArgumentNullException(nameof(defaultVernacular));
        }

        public string DefaultAnalysis { get; }
        public string DefaultVernacular { get; }
        public bool IsKnown(string tag) => true;
    }

    /// <summary>The paragraph styles the block notation maps to (spec 3.1) and the character style a link implies (spec 3.2).
    /// The defaults are the names a new FieldWorks project already has, plus the two GDR table styles.</summary>
    public sealed class MarkdownStyleNames
    {
        string _normal = "Normal", _heading1 = "Heading 1", _heading2 = "Heading 2", _heading3 = "Heading 3", _bulletedList = "Bulleted List",
            _numberedList = "Numbered List", _blockQuote = "Block Quote", _tableHeader = "GDR Table Header", _tableRow = "GDR Table Row", _hyperlink = "Hyperlink";

        // No name is null: a null would only show as a NullReferenceException deep in the reader.
        static string Name(string? value) => value ?? throw new ArgumentNullException(nameof(value));

        /// <exception cref="ArgumentNullException">A style name is set to null.</exception>
        public string Normal { get => _normal; set => _normal = Name(value); }
        public string Heading1 { get => _heading1; set => _heading1 = Name(value); }
        public string Heading2 { get => _heading2; set => _heading2 = Name(value); }
        public string Heading3 { get => _heading3; set => _heading3 = Name(value); }
        public string BulletedList { get => _bulletedList; set => _bulletedList = Name(value); }
        public string NumberedList { get => _numberedList; set => _numberedList = Name(value); }
        public string BlockQuote { get => _blockQuote; set => _blockQuote = Name(value); }
        public string TableHeader { get => _tableHeader; set => _tableHeader = Name(value); }
        public string TableRow { get => _tableRow; set => _tableRow = Name(value); }
        /// <summary>The character style a link run has unless the notation says otherwise.</summary>
        public string Hyperlink { get => _hyperlink; set => _hyperlink = Name(value); }

        internal string? Heading(int level) => level == 1 ? Heading1 : level == 2 ? Heading2 : level == 3 ? Heading3 : null;
    }

    /// <summary>Which style names exist in the project, so the parser can report <c>gdr.style.unknown</c>.</summary>
    public interface IStyleCatalog
    {
        bool IsParagraphStyle(string name);
        bool IsCharacterStyle(string name);
    }

    /// <summary>Optional: a style catalog that also lists the style names of the project. When <see cref="MarkdownOptions.Styles"/> implements
    /// it, <c>gdr.style.unknown</c> carries the closest names as its nearest (the spec: <c>Nearest: Example</c>).</summary>
    public interface IKnownStyles
    {
        IReadOnlyList<string> ParagraphStyleNames { get; }
        IReadOnlyList<string> CharacterStyleNames { get; }
    }

    /// <summary>Optional: a style catalog that can say more than yes or no about a style name. When <see cref="MarkdownOptions.Styles"/> implements it,
    /// the parser calls <see cref="CheckStyle"/> instead of <see cref="IStyleCatalog.IsParagraphStyle"/> and <see cref="IStyleCatalog.IsCharacterStyle"/>.</summary>
    public interface IStyleChecker
    {
        /// <summary>Null when the style is fine. Otherwise the problem to report on the name: an error with the default id <c>gdr.style.unknown</c>, or a
        /// warning or suggestion for a style that is accepted but worth a word. The host's <see cref="MarkdownProblem.Nearest"/> is reported; when it
        /// leaves it empty and also implements <see cref="IKnownStyles"/>, the closest names fill it.</summary>
        MarkdownProblem? CheckStyle(string name, bool paragraph);
    }

    /// <summary>Where a piece of notation is in the text that was read: UTF-16 offsets (the end is exclusive) and the 1-based line and
    /// column of each end, the same numbers a finding carries.</summary>
    public sealed class MarkdownSourceRange
    {
        public MarkdownSourceRange(int start, int end, int line, int column, int endLine, int endColumn)
        {
            Start = start;
            End = end;
            Line = line;
            Column = column;
            EndLine = endLine;
            EndColumn = endColumn;
        }

        public int Start { get; }
        public int End { get; }
        public int Line { get; }
        public int Column { get; }
        public int EndLine { get; }
        public int EndColumn { get; }
    }

    /// <summary>What a host found wrong, or worth saying, about an object the notation spells: the same parts as a finding. The parser places it
    /// on the object's range and reports it. A problem that comes with a failure is an error; one that comes with success is a warning
    /// (<c>gdr.link.broken</c>) or a suggestion.</summary>
    public sealed class MarkdownProblem
    {
        /// <summary>A problem with the default id: <c>gdr.link.not-found</c> for a link, <c>gdr.picture.unknown</c> for a picture.</summary>
        public MarkdownProblem(string message) : this(null, MarkdownSeverity.Error, message) { }

        public MarkdownProblem(string? id, MarkdownSeverity severity, string message)
        {
            Id = id;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        /// <summary>The finding's id, for example <see cref="MarkdownDiagnosticIds.LinkAmbiguous"/>; null for the default of the object's kind.</summary>
        public string? Id { get; }
        public MarkdownSeverity Severity { get; }
        public string Message { get; }

        /// <summary>The ways to fix it when intent is ambiguous, for example one <c>[[w:palit]]{guid=...}</c> for each entry that matches.</summary>
        public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();

        /// <summary>The names the host suggests (<c>palit</c>, <c>palito</c> for <c>[[w:palt]]</c>).</summary>
        public IReadOnlyList<string> Nearest { get; set; } = Array.Empty<string>();

        /// <summary>One exact replacement of the observed text that fixes it, or null.</summary>
        public string? Replacement { get; set; }
    }

    /// <summary>What kind of target an object run's data holds, as far as the notation distinguishes them.</summary>
    public enum MarkdownObjectKind
    {
        /// <summary>A link to a path or URL: <c>[text](url)</c>.</summary>
        ExternalLink,
        /// <summary>A link to an object of the project: <c>[[kind:Name]]{guid=...}</c> or <c>[text]{link=kind:Name guid=...}</c>.</summary>
        InternalLink,
        /// <summary>A picture: <c>![caption](fw:picture/guid)</c>.</summary>
        Picture,
    }

    /// <summary>The notation-level description of an object run's data. The host fills it when exporting and receives it when
    /// importing; the converter never looks inside the data string.</summary>
    public sealed class MarkdownObject
    {
        public MarkdownObjectKind Kind { get; set; }

        /// <summary>External link: the path or URL. Internal link: the stored URL, set only when it differs from what the host
        /// builds from kind, guid and tool (written <c>url="..."</c>).</summary>
        public string? Url { get; set; }

        /// <summary>Internal link: the kind (<c>w</c>, <c>rule</c>, ...).</summary>
        public string? LinkKind { get; set; }

        /// <summary>Internal link: the target's current name, which the name form shows.</summary>
        public string? Name { get; set; }

        /// <summary>Internal link or picture: the target's GUID.</summary>
        public Guid? Guid { get; set; }

        /// <summary>Internal link: the FLEx tool, set only when the host's table does not know it (written <c>tool=...</c>).</summary>
        public string? Tool { get; set; }

        /// <summary>Internal link: the target was deleted (<c>broken</c>).</summary>
        public bool Broken { get; set; }

        /// <summary>Picture: the caption shown as alt text on export; ignored on import.</summary>
        public string? Caption { get; set; }

        /// <summary>Import: where the object is in the text (the whole <c>[[kind:Name]]{...}</c>, <c>[text]{...}</c>, <c>[text](url){...}</c> or
        /// <c>![caption](url)</c>). The host can place its own findings with it. Null on export.</summary>
        public MarkdownSourceRange? Source { get; set; }

        /// <summary>Import: the object data the host made for it, once it has; null when it could not.</summary>
        public string? ObjData { get; set; }
    }

    /// <summary>Maps an object run's data string (property 6) to the notation and back. Data it does not describe is written as
    /// <c>[...]{s6="..."}</c>, so nothing is lost.</summary>
    public interface IObjectMapper
    {
        /// <summary>Export: describes the data, or returns null for "write it generically".</summary>
        MarkdownObject? Describe(string objData);

        /// <summary>Import: builds the data for an object the notation spelled. On failure <paramref name="problem"/> says what
        /// was not found, and the parser reports <c>gdr.link.not-found</c> or <c>gdr.picture.unknown</c>.</summary>
        bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem);

        /// <summary>True when <paramref name="kind"/> is a link kind the host resolves; the name form <c>[[kind:Name]]</c> only
        /// triggers for these.</summary>
        bool IsKnownLinkKind(string kind);
    }

    /// <summary>Optional: an object mapper that can say more than yes or no. When <see cref="MarkdownOptions.Objects"/> implements it, the parser
    /// calls <see cref="TryResolve"/> instead of <see cref="IObjectMapper.TryCreateObjData"/>, so the host's own findings (the spec's
    /// <c>gdr.link.not-found</c> with its nearest names, <c>gdr.link.ambiguous</c> with its choices, <c>gdr.link.broken</c> as a warning) reach
    /// the result with the line and column of the object.</summary>
    public interface IObjectResolver
    {
        /// <summary>Import: builds the data for an object the notation spelled. Returns true with the data when it can, and then
        /// <paramref name="problem"/> may still hold a warning or suggestion to report (a deleted target: <c>gdr.link.broken</c>). Returns false when it
        /// cannot, and <paramref name="problem"/> says why (null for a plain "cannot be resolved"); that is always an error.
        /// <paramref name="target"/> carries the object's place in the text (<see cref="MarkdownObject.Source"/>).</summary>
        bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem);
    }

    /// <summary>The mapper with no project: an object whose data starts with U+0004 (FieldWorks' external path name type) is an
    /// external link to the rest of the string; every other object is generic. Pictures and project links need a host.</summary>
    public sealed class DefaultObjectMapper : IObjectMapper
    {
        public static readonly DefaultObjectMapper Instance = new DefaultObjectMapper();

        /// <summary>FieldWorks' kodtExternalPathName (4): an ObjData string is this character, then the path or URL.</summary>
        public const char ExternalPathNameType = '\u0004';

        public MarkdownObject? Describe(string objData)
        {
            if (objData.Length > 0 && objData[0] == ExternalPathNameType)
                return new MarkdownObject { Kind = MarkdownObjectKind.ExternalLink, Url = objData.Substring(1) };
            return null;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            if (target.Kind == MarkdownObjectKind.ExternalLink && target.Url != null)
            {
                objData = ExternalPathNameType + target.Url;
                problem = null;
                return true;
            }
            objData = string.Empty;
            problem = target.Kind == MarkdownObjectKind.Picture ? "pictures need a host" : "links to project objects need a host";
            return false;
        }

        public bool IsKnownLinkKind(string kind) => false;
    }
}
