using System;
using System.Collections.Generic;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>A paragraph: an identity, paragraph properties and a list of runs. Offsets are UTF-16 indices into
    /// the concatenated run text, equal to ITsString offsets.</summary>
    public sealed class Paragraph
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Style { get; set; } = string.Empty;
        /// <summary>Paragraph direction (ktptRightToLeft).</summary>
        public bool Rtl { get; set; }
        /// <summary>The writing system used for typing in an empty paragraph.</summary>
        public string DefaultWs { get; set; } = "en";
        public List<Run> Runs { get; set; } = new List<Run>();

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

        public Paragraph Clone(bool keepId = true)
        {
            var p = new Paragraph { Style = Style, Rtl = Rtl, DefaultWs = DefaultWs };
            if (keepId) p.Id = Id;
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
            for (int i = Runs.Count - 1; i >= 0; i--)
                if (Runs[i].Text.Length == 0) Runs.RemoveAt(i);
            for (int i = Runs.Count - 1; i > 0; i--)
            {
                if (Runs[i - 1].SameFormat(Runs[i]))
                {
                    Runs[i - 1].Text += Runs[i].Text;
                    Runs.RemoveAt(i);
                }
            }
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
