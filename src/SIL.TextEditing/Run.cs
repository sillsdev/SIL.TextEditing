using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>One run of a string, as an ITsString reports it: text, a writing-system id, an opaque property bag
    /// and, for an object run, the object data. An object run's text is exactly one U+FFFC.</summary>
    public sealed class Run
    {
        public const char ObjectReplacement = '￼';

        public string Text { get; set; } = string.Empty;
        public string Ws { get; set; } = "en";
        public PropertyBag Props { get; set; } = new PropertyBag();
        /// <summary>Non-null makes this an atomic object run (an ORC); the text is then exactly U+FFFC.</summary>
        public string? ObjData { get; set; }

        public bool IsObject => ObjData != null;

        /// <summary>The run's editable property (ktptEditable); a run without it is <see cref="RunEditable.Editable"/>.</summary>
        public RunEditable Editable
        {
            get
            {
                if (!Props.TryGetInt(TextPropertyNumbers.Editable, out var v)) return RunEditable.Editable;
                return v == TextPropertyNumbers.EditableNo ? RunEditable.NotEditable
                    : v == TextPropertyNumbers.EditableSemi ? RunEditable.SemiEditable
                    : RunEditable.Editable;
            }
        }

        public Run Clone()
        {
            return new Run { Text = Text, Ws = Ws, ObjData = ObjData, Props = Props.Clone() };
        }

        public Run CloneWithText(string text)
        {
            var copy = Clone();
            copy.Text = text;
            return copy;
        }

        /// <summary>True when two runs can merge: same writing system and same properties, neither an object.</summary>
        public bool SameFormat(Run other)
        {
            return !IsObject && !other.IsObject && Ws == other.Ws && Props.ContentEquals(other.Props);
        }

        public static Run Object(string ws, string data)
        {
            return new Run { Text = ObjectReplacement.ToString(), Ws = ws, ObjData = data };
        }
    }

    /// <summary>The three values of FieldWorks' editable text property (ADR 0019).</summary>
    public enum RunEditable { NotEditable, Editable, SemiEditable }

    /// <summary>A run's place in a paragraph, as ITsString GetRunInfo reports it.</summary>
    public readonly struct RunSpan
    {
        public RunSpan(int start, int length, Run run)
        {
            Start = start;
            Length = length;
            Run = run;
        }

        public int Start { get; }
        public int Length { get; }
        public Run Run { get; }
        public string Ws => Run.Ws;
    }
}
