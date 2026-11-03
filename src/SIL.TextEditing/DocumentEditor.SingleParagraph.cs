using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    // Single-paragraph mode (ADR 0012): the field never makes a second paragraph.
    public sealed partial class DocumentEditor
    {
        /// <summary>When true the editor never splits a paragraph: <see cref="Enter"/> does nothing (the field hands the key to
        /// the host, as FieldWorks does when a view does not accept Return), a line break in typed text becomes a space, and
        /// pasted paragraphs are joined into one with a space between them. A document that already holds several
        /// paragraphs keeps them; only new splits are refused.</summary>
        public bool SingleParagraph { get; set; }

        /// <summary>The fragment as one paragraph: the first paragraph's style and properties, the runs of every paragraph in
        /// order, and one plain space (in the writing system of the text before it) between paragraphs.</summary>
        static List<Paragraph> JoinIntoOne(List<Paragraph> fragment)
        {
            var joined = fragment[0].Clone(false);
            joined.Runs = new List<Run>();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0)
                {
                    var before = joined.Runs.Count > 0 ? joined.Runs[joined.Runs.Count - 1].Ws : fragment[i - 1].DefaultWs;
                    joined.Runs.Add(new Run { Text = " ", Ws = before });
                }
                joined.Runs.AddRange(fragment[i].Runs.Select(r => r.Clone()));
            }
            joined.Normalize();
            return new List<Paragraph> { joined };
        }
    }
}
