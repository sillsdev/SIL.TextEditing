using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SIL.TextEditing
{
    /// <summary>The runs of a <see cref="Paragraph"/>. It tells its paragraph whenever a run is added, removed, replaced or has its
    /// text changed, so the paragraph keeps the run starts and the length up to date without a cache that a direct edit of the
    /// runs could leave stale (issue 76). A run may sit in more than one list; each one is told.</summary>
    public sealed class RunList : Collection<Run>, IEnumerable<Run>
    {
        Paragraph? _owner;

        internal RunList(Paragraph? owner)
        {
            _owner = owner;
        }

        /// <summary>A list of these runs that no paragraph holds yet; assign it to <see cref="Paragraph.Runs"/> to adopt it.</summary>
        public RunList(IEnumerable<Run> runs) : this((Paragraph?)null)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            foreach (var run in runs) Add(run);
        }

        public RunList() : this((Paragraph?)null) { }

        /// <summary>The paragraph that holds this list. Its runs tell this list (and so the paragraph) of a change only while a paragraph
        /// holds it, so a list that is built and thrown away, or that a paragraph has let go of, leaves no link behind on the runs.</summary>
        internal Paragraph? Owner
        {
            get => _owner;
            set
            {
                if (ReferenceEquals(_owner, value)) return;
                if (_owner != null) foreach (var run in Items) run.Detach(this);
                _owner = value;
                if (_owner != null) foreach (var run in Items) run.Attach(this);
            }
        }

        /// <summary>How many runs have been read through the indexer or an enumerator, so a test can count the work a lookup does
        /// instead of timing it.</summary>
        internal long ElementReads { get; private set; }

        public new Run this[int index]
        {
            get { ElementReads++; return Items[index]; }
            set { base[index] = value; }
        }

        public new Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<Run> IEnumerable<Run>.GetEnumerator() => new Enumerator(this);

        public void AddRange(IEnumerable<Run> runs)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            // A run list given itself, or a list built from this one's items, is copied first.
            foreach (var run in new List<Run>(runs)) Add(run);
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > Count) throw new ArgumentOutOfRangeException(nameof(index));
            if (count == 0) return;
            // One pass: detach the runs, then take them out of the backing list in one move (RemoveAt for each would shift the tail each time).
            if (_owner != null) for (int i = index; i < index + count; i++) Items[i].Detach(this);
            ((List<Run>)Items).RemoveRange(index, count);
            Changed();
        }

        protected override void InsertItem(int index, Run item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            base.InsertItem(index, item);
            if (_owner != null) item.Attach(this);
            Changed();
        }

        protected override void SetItem(int index, Run item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_owner != null) Items[index].Detach(this);
            base.SetItem(index, item);
            if (_owner != null) item.Attach(this);
            Changed();
        }

        protected override void RemoveItem(int index)
        {
            if (_owner != null) Items[index].Detach(this);
            base.RemoveItem(index);
            Changed();
        }

        protected override void ClearItems()
        {
            if (_owner != null) foreach (var run in Items) run.Detach(this);
            base.ClearItems();
            Changed();
        }

        internal void Changed() => _owner?.RunsChanged();

        /// <summary>Walks the runs, counting each one read.</summary>
        public struct Enumerator : IEnumerator<Run>
        {
            readonly RunList _list;
            int _index;

            internal Enumerator(RunList list)
            {
                _list = list;
                _index = -1;
            }

            public Run Current => _list.Items[_index];
            object System.Collections.IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_index + 1 >= _list.Count) return false;
                _index++;
                _list.ElementReads++;
                return true;
            }

            public void Reset() => _index = -1;
            public void Dispose() { }
        }
    }
}
