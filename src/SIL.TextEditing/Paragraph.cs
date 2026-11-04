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
        List<Run> _runs = new List<Run>();
        int[]? _runStarts;
        List<Run>? _runStartsFor;

        public List<Run> Runs
        {
            get => _runs;
            set { _runs = value; _runStarts = null; }
        }

        /// <summary>The run that holds the character at <paramref name="offset"/> (the first run that ends after it), found by binary
        /// search over cached run starts instead of adding up lengths from the start of the paragraph; <paramref name="index"/> is
        /// the run count and <paramref name="start"/> the paragraph length when there is none. The cache is dropped by every
        /// change this class makes to the runs and checked against the runs it finds, so a host that edits <see cref="Runs"/> or a
        /// run's text directly is still answered correctly (the cache is rebuilt when it no longer fits). <paramref name="probe"/> is
        /// called once per run the work touches, so a test can count it.</summary>
        internal void RunIndexAt(int offset, out int index, out int start, Action? probe)
        {
            var runs = _runs;
            var starts = _runStarts;
            if (starts == null || !ReferenceEquals(_runStartsFor, runs) || starts.Length != runs.Count + 1) starts = BuildRunStarts(probe);
            for (int attempt = 0; ; attempt++)
            {
                int lo = 0, hi = runs.Count;
                while (lo < hi)
                {
                    probe?.Invoke();
                    int mid = (lo + hi) >> 1;
                    if (starts[mid + 1] > offset) hi = mid; else lo = mid + 1;
                }
                bool fits = lo == runs.Count
                    ? starts[lo] == (lo == 0 ? 0 : starts[lo - 1] + runs[lo - 1].Text.Length)
                    : starts[lo + 1] - starts[lo] == runs[lo].Text.Length && (lo == 0 ? starts[0] == 0 : starts[lo] - starts[lo - 1] == runs[lo - 1].Text.Length);
                if (fits || attempt > 0) { index = lo; start = starts[lo]; return; }
                starts = BuildRunStarts(probe);
            }
        }

        int[] BuildRunStarts(Action? probe)
        {
            var starts = new int[_runs.Count + 1];
            int pos = 0;
            for (int i = 0; i < _runs.Count; i++)
            {
                probe?.Invoke();
                starts[i] = pos;
                pos += _runs[i].Text.Length;
            }
            starts[_runs.Count] = pos;
            _runStarts = starts;
            _runStartsFor = _runs;
            return starts;
        }

        public int Length
        {
            get
            {
                int n = 0;
                foreach (var r in Runs) n += r.Text.Length;
                return n;
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
            int pos = 0;
            runStart = 0;
            foreach (var r in Runs)
            {
                if (r.Text.Length > 0 && offset > pos && offset <= pos + r.Text.Length)
                {
                    runStart = pos;
                    return r;
                }
                pos += r.Text.Length;
            }
            return null;
        }

        /// <summary>The run that starts at or contains the offset (the following run at a boundary), or null.</summary>
        public Run? RunAfter(int offset, out int runStart)
        {
            int pos = 0;
            runStart = 0;
            foreach (var r in Runs)
            {
                if (offset >= pos && offset < pos + r.Text.Length)
                {
                    runStart = pos;
                    return r;
                }
                pos += r.Text.Length;
            }
            return null;
        }

        /// <summary>Makes a run boundary at the offset, copying all properties to both halves. Returns the index of
        /// the run that now starts there.</summary>
        public int SplitRunAt(int offset)
        {
            _runStarts = null;
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
            _runStarts = null;
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
