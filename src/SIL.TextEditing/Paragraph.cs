using System;
using System.Collections.Generic;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>A paragraph: a host key, paragraph properties and a list of runs. Offsets are UTF-16 indices into
    /// the concatenated run text, equal to ITsString offsets.</summary>
    public sealed class Paragraph
    {
        /// <summary>The host's opaque key for this paragraph (in FieldWorks the StTxtPara GUID); null until the host assigns one.
        /// The field never interprets it: it only moves it by the split and join rules of <see cref="DocumentEditor"/>.</summary>
        public object? Key { get; set; }
        public string Style { get; set; } = string.Empty;
        /// <summary>Paragraph direction (ktptRightToLeft).</summary>
        public bool Rtl { get; set; }
        /// <summary>The writing system used for typing in an empty paragraph.</summary>
        public string DefaultWs { get; set; } = "en";
        /// <summary>The paragraph's own text properties (an ITsString paragraph's StyleRules), opaque like a run's bag: copied through
        /// every split, join and slice, never interpreted. <see cref="Style"/> and <see cref="Rtl"/> are the two the field reads.</summary>
        public PropertyBag Props { get; set; } = new PropertyBag();

        RunList _runs;

        public Paragraph()
        {
            _runs = new RunList(this);
        }

        /// <summary>The paragraph's runs. Add, remove or change them directly if you like: the list and each run tell the paragraph,
        /// so <see cref="Length"/> and the run lookups stay right. Assigning a list that no paragraph holds adopts it; any other
        /// list is copied.</summary>
        public RunList Runs
        {
            get => _runs;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(value, _runs)) return;
                var adopted = value.Owner == null ? value : new RunList(value);
                _runs.Owner = null;
                adopted.Owner = this;
                _runs = adopted;
                RunsChanged();
            }
        }

        // Where each run starts, with the total length after the last: n + 1 entries for n runs. The runs and each run's text tell this
        // paragraph when they change (RunsChanged), which drops the starts; the next question builds them again in one pass. So a
        // run the paragraph was told about can never leave the starts stale, and any number of questions between changes cost a
        // binary search each instead of a walk over the runs (issue 76).
        int[]? _starts;

        internal void RunsChanged() => _starts = null;

        int[] Starts()
        {
            var starts = _starts;
            if (starts != null) return starts;
            var runs = _runs;
            starts = new int[runs.Count + 1];
            int n = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                starts[i] = n;
                n += runs[i].Text.Length;
            }
            starts[runs.Count] = n;
            return _starts = starts;
        }

        /// <summary>The index of the last run that starts at or before <paramref name="offset"/> (strictly before when
        /// <paramref name="strict"/>), or -1.</summary>
        int LastStartingBefore(int[] starts, int offset, bool strict)
        {
            int lo = 0, hi = starts.Length - 2;                // run indexes; starts[Count] is only the total
            int found = -1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (strict ? starts[mid] < offset : starts[mid] <= offset) { found = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return found;
        }

        /// <summary>The index of the run that holds the character at <paramref name="offset"/> (the one that starts there at a
        /// boundary), or <see cref="List{T}.Count"/> when the offset is at or past the end.</summary>
        internal int RunIndexAt(int offset, out int runStart)
        {
            var starts = Starts();
            int i = offset < 0 ? -1 : LastStartingBefore(starts, offset, false);
            if (i >= 0 && offset < starts[i + 1]) { runStart = starts[i]; return i; }
            runStart = starts[starts.Length - 1];
            return _runs.Count;
        }

        public int Length
        {
            get
            {
                var starts = Starts();
                return starts[starts.Length - 1];
            }
        }

        public string Text
        {
            get
            {
                if (Runs.Count == 1) return Runs[0].Text;
                var sb = new StringBuilder();
                foreach (var r in Runs) sb.Append(r.Text);
                return sb.ToString();
            }
        }

        public Paragraph Clone(bool keepKey = true)
        {
            var p = new Paragraph { Style = Style, Rtl = Rtl, DefaultWs = DefaultWs, Props = Props.Clone() };
            if (keepKey) p.Key = Key;
            foreach (var r in Runs) p.Runs.Add(r.Clone());
            return p;
        }

        /// <summary>The run that ends at or contains the offset (the preceding run at a boundary), or null.</summary>
        public Run? RunBefore(int offset, out int runStart)
        {
            runStart = 0;
            var starts = Starts();
            if (offset <= 0) return null;
            int i = LastStartingBefore(starts, offset, true);
            if (i < 0 || offset > starts[i + 1]) return null;           // an empty run is never the one that ends at the offset
            if (starts[i + 1] == starts[i]) return null;
            runStart = starts[i];
            return _runs[i];
        }

        /// <summary>The run that starts at or contains the offset (the following run at a boundary), or null.</summary>
        public Run? RunAfter(int offset, out int runStart)
        {
            runStart = 0;
            if (offset < 0) return null;
            int i = RunIndexAt(offset, out runStart);
            if (i >= _runs.Count) { runStart = 0; return null; }
            return _runs[i];
        }

        /// <summary>Makes a run boundary at the offset, copying all properties to both halves. Returns the index of
        /// the run that now starts there.</summary>
        public int SplitRunAt(int offset)
        {
            int pos = 0;
            for (int i = 0; i < Runs.Count; i++)
            {
                var r = Runs[i];
                if (offset == pos) return i;
                if (offset < pos + r.Text.Length)
                {
                    int k = offset - pos;
                    var right = r.CloneWithText(r.Text.Substring(k));
                    r.Text = r.Text.Substring(0, k);
                    Runs.Insert(i + 1, right);
                    return i + 1;
                }
                pos += r.Text.Length;
            }
            return Runs.Count;
        }

        /// <summary>Drops empty runs and merges neighbours of the same format.</summary>
        public void Normalize()
        {
            // One forward pass: drop empty runs and merge neighbours of the same format. A chain of merges is built in a string
            // builder, so a paragraph of thousands of runs that merge into one stays linear.
            var result = new List<Run>(Runs.Count);
            Run? current = null;
            System.Text.StringBuilder? joined = null;
            void Finish()
            {
                if (current == null) return;
                if (joined != null) { current.Text = joined.ToString(); joined = null; }
                result.Add(current);
            }
            foreach (var run in Runs)
            {
                if (run.Text.Length == 0) continue;
                if (current != null && current.SameFormat(run))
                {
                    joined ??= new System.Text.StringBuilder(current.Text);
                    joined.Append(run.Text);
                }
                else
                {
                    Finish();
                    current = run;
                }
            }
            Finish();
            Runs.Clear();
            Runs.AddRange(result);
        }

        public void Insert(int offset, string text, string ws, PropertyBag? props)
        {
            int i = SplitRunAt(offset);
            var run = new Run { Text = text, Ws = ws };
            if (props != null) run.Props = props.Clone();
            Runs.Insert(i, run);
            Normalize();
        }

        public void Delete(int start, int length)
        {
            if (length <= 0) return;
            int a = SplitRunAt(start);
            int b = SplitRunAt(start + length);
            Runs.RemoveRange(a, b - a);
            Normalize();
        }

        /// <summary>Clones of the runs in [start, end), properties kept.</summary>
        public List<Run> Slice(int start, int end)
        {
            var c = Clone();
            var result = new List<Run>();
            int a = c.SplitRunAt(start);
            int b = c.SplitRunAt(end);
            for (int i = a; i < b; i++) result.Add(c.Runs[i].Clone());
            return result;
        }

        /// <summary>The runs with their start and length, as a TsString would report them.</summary>
        public List<RunSpan> Spans()
        {
            var result = new List<RunSpan>();
            int pos = 0;
            foreach (var r in Runs)
            {
                result.Add(new RunSpan(pos, r.Text.Length, r));
                pos += r.Text.Length;
            }
            return result;
        }
    }
}
