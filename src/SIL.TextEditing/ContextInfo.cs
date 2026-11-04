using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>What a field knows about the place a context menu was asked for (ADR 0021): the word, the run, any link or
    /// embedded object, and the selection. The host builds the menu from it. Everything here is a copy: changing it changes
    /// nothing in the document.</summary>
    public sealed class ContextInfo
    {
        /// <summary>The place asked about: the character under the pointer, or the caret.</summary>
        public TextPosition Position { get; internal set; }

        /// <summary>The word at the place, or the empty string when it is on white space, punctuation or an embedded object.</summary>
        public string Word { get; internal set; } = string.Empty;

        /// <summary>The word's range; empty (at <see cref="Position"/>) when there is no word.</summary>
        public TextRange WordRange { get; internal set; }

        /// <summary>The writing system of the word, or of the text at the place when there is no word: the run's writing system, or
        /// the paragraph's default in an empty paragraph.</summary>
        public string WritingSystem { get; internal set; } = string.Empty;

        /// <summary>A copy of the run at the place (its text, writing system and properties), or null in an empty paragraph.</summary>
        public Run? Run { get; internal set; }

        /// <summary>Where that run lies; empty when <see cref="Run"/> is null.</summary>
        public TextRange RunRange { get; internal set; }

        /// <summary>The embedded object at the place, or null.</summary>
        public EmbeddedObjectInfo? EmbeddedObject { get; internal set; }

        /// <summary>The selection, in document order; empty at the caret when there is none.</summary>
        public TextRange Selection { get; internal set; }

        public bool HasSelection => !Selection.IsEmpty;

        /// <summary>The selected text, paragraphs joined by a line feed; empty when nothing is selected.</summary>
        public string SelectedText { get; internal set; } = string.Empty;

        /// <summary>Spelling suggestions for the word. Empty: nothing fills it yet. The host's spell checker (ADR 0020) will, and the
        /// host that builds the menu lists what is here, so the hook is in place before the checker is.</summary>
        public List<string> SpellingSuggestions { get; } = new List<string>();
    }

    /// <summary>An embedded object (an ORC) at a place: its one-character range and the host's data for it.</summary>
    public sealed class EmbeddedObjectInfo
    {
        internal EmbeddedObjectInfo(TextRange range, string data)
        {
            Range = range;
            Data = data;
        }

        public TextRange Range { get; }

        /// <summary>The object data as the host stored it. Its first character is the kind: 3, 4 and 5 (name and GUID hot link,
        /// external path name, own name and GUID hot link) are links; other kinds are pictures and embedded objects.</summary>
        public string Data { get; }

        /// <summary>True for the hot-link kinds, which activate when clicked.</summary>
        public bool IsLink => Data.Length > 0 && Data[0] >= (char)3 && Data[0] <= (char)5;
    }
}
