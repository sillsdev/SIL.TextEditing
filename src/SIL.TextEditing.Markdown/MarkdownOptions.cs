using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    /// <summary>What the converter needs from its host: the writing systems that unmarked text and <c>v</c> stand for, the
    /// style names the block notation uses, which styles exist, and what an object run's data means. Everything is
    /// optional; the defaults need no project (ADR 0011).</summary>
    public sealed class MarkdownOptions
    {
        /// <summary>The writing system of unmarked text and of <c>v</c>. Default: <see cref="FixedWritingSystems"/> with "en" for both.</summary>
        public IWritingSystemMapper WritingSystems { get; set; } = new FixedWritingSystems("en", "en");

        /// <summary>The paragraph and hyperlink style names the notation uses. Default: the spec's names.</summary>
        public MarkdownStyleNames StyleNames { get; set; } = new MarkdownStyleNames();

        /// <summary>Whether a style name exists in the project; null accepts every name.</summary>
        public IStyleCatalog? Styles { get; set; }

        /// <summary>What an object run's data means. Default: <see cref="DefaultObjectMapper"/>.</summary>
        public IObjectMapper Objects { get; set; } = DefaultObjectMapper.Instance;

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
        public string Normal { get; set; } = "Normal";
        public string Heading1 { get; set; } = "Heading 1";
        public string Heading2 { get; set; } = "Heading 2";
        public string Heading3 { get; set; } = "Heading 3";
        public string BulletedList { get; set; } = "Bulleted List";
        public string NumberedList { get; set; } = "Numbered List";
        public string BlockQuote { get; set; } = "Block Quote";
        public string TableHeader { get; set; } = "GDR Table Header";
        public string TableRow { get; set; } = "GDR Table Row";
        /// <summary>The character style a link run has unless the notation says otherwise.</summary>
        public string Hyperlink { get; set; } = "Hyperlink";

        internal string? Heading(int level) => level == 1 ? Heading1 : level == 2 ? Heading2 : level == 3 ? Heading3 : null;
    }

    /// <summary>Which style names exist in the project, so the parser can report <c>gdr.style.unknown</c>.</summary>
    public interface IStyleCatalog
    {
        bool IsParagraphStyle(string name);
        bool IsCharacterStyle(string name);
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
