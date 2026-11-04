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

        string _text = string.Empty;
        RunList? _owner;
        List<WeakReference<RunList>>? _otherOwners;

        /// <summary>The run's text. Changing it tells every paragraph that holds the run, so its run starts and length stay right.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (ReferenceEquals(_text, value)) return;
                _text = value ?? throw new ArgumentNullException(nameof(value));
                NotifyOwners();
            }
        }

        // A run knows the lists of the paragraphs that hold it: the first strongly, any others (the same run in two paragraphs is rare)
        // weakly, so a paragraph that is dropped while its runs live on is not kept alive by them. A list is attached only while a
        // paragraph holds it.
        void NotifyOwners()
        {
            _owner?.Changed();
            if (_otherOwners == null) return;
            for (int i = _otherOwners.Count - 1; i >= 0; i--)
            {
                if (_otherOwners[i].TryGetTarget(out var list)) list.Changed();
                else _otherOwners.RemoveAt(i);
            }
            if (_otherOwners.Count == 0) _otherOwners = null;
        }

        internal void Attach(RunList list)
        {
            if (_owner == null) _owner = list;
            else (_otherOwners ??= new List<WeakReference<RunList>>()).Add(new WeakReference<RunList>(list));
        }

        internal void Detach(RunList list)
        {
            if (_otherOwners != null)
            {
                for (int i = 0; i < _otherOwners.Count; i++)
                {
                    if (_otherOwners[i].TryGetTarget(out var other) && ReferenceEquals(other, list))
                    {
                        _otherOwners.RemoveAt(i);
                        if (_otherOwners.Count == 0) _otherOwners = null;
                        return;
                    }
                }
            }
            if (!ReferenceEquals(_owner, list)) return;
            _owner = null;
            while (_otherOwners != null && _owner == null)
            {
                var first = _otherOwners[0];
                _otherOwners.RemoveAt(0);
                if (_otherOwners.Count == 0) _otherOwners = null;
                if (first.TryGetTarget(out var promoted)) _owner = promoted;
            }
        }

        /// <summary>For a test: how many paragraphs' lists this run is attached to.</summary>
        internal int OwnerCount => (_owner == null ? 0 : 1) + (_otherOwners == null ? 0 : _otherOwners.Count);

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

    /// <summary>The three values of FieldWorks' editable text property (ADR 0019). Semi-editable text ("type-ahead") takes typing
    /// and deletion like editable text but cannot be formatted.</summary>
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
