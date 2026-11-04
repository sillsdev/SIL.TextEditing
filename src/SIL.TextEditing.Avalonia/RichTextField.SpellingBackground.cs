using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Avalonia
{
    // Checking off the UI thread (issue 103): a checker that is an IBackgroundSpellChecker is asked the words of one idle step on a worker. The field finds
    // and judges the words on its own thread, sends the dictionary questions out as one batch, and applies the answers on its own thread, only for
    // a paragraph that is as it was when they were sent. The rules (what is a word, what is skipped) are those of the synchronous path.
    public partial class RichTextField
    {
        /// <summary>For a test: how the dictionary questions of a batch are run off the field's thread. It is given the work to run; null (the default) queues it
        /// on the thread pool. The answers come back through <see cref="SpellIdlePost"/> (else the field's dispatcher).</summary>
        internal Action<Action>? SpellBackgroundRun { get; set; }

        /// <summary>True while a batch is out. One batch is out at a time, so a checker is never asked on two threads at once, and the idle work does not start
        /// another step: the answer asks for the next one.</summary>
        bool _spellInFlight;

        /// <summary>The words of one step whose dictionary questions went to the worker, with what is needed to apply the answers: only if the paragraph's
        /// state is the one that was being checked, at the stamp it had, in the same epoch.</summary>
        sealed class SpellBatch
        {
            public ParaInfo Info = null!;
            public SpellState State = null!;
            public Paragraph Paragraph = null!;
            public long Stamp;
            public int Epoch;
            public int ZoneStart, Pos, RangeEnd;
            public bool Done;
            /// <summary>The marks of the step that the field's thread decided itself.</summary>
            public List<SpellingMark> Fresh = null!;
            public List<SpellScanner.Judged> Asked = null!;
            /// <summary>For each asked word, whether the dictionary has it; null until the worker has run.</summary>
            public bool[]? Answers;
            public readonly List<(string Ws, Exception Error)> Failures = new List<(string, Exception)>();
        }

        /// <summary>The marks of a step, applied to the state of the paragraph: the words looked at replace whatever marks lay over them, and the stretch is
        /// done or goes on from where the step stopped.</summary>
        static void ApplyStep(SpellState state, int zoneStart, int pos, bool done, int rangeEnd, List<SpellingMark> fresh)
        {
            state.Marks.RemoveAll(m => m.End > zoneStart && m.Start < pos);
            if (fresh.Count > 0)
            {
                state.Marks.AddRange(fresh);
                state.Marks.Sort((x, y) => x.Start.CompareTo(y.Start));
            }
            if (done) { state.Pending.RemoveAt(0); state.ResumeAt = -1; }
            else { state.Pending[0] = (pos, rangeEnd); state.ResumeAt = pos; }
        }

        static SpellingMark MarkOf(SpellScanner.Judged judged) => new SpellingMark(judged.Start, judged.End - judged.Start, judged.Problem);

        void SendToWorker(SpellBatch batch)
        {
            _spellInFlight = true;
            var dispatcher = OwnDispatcher;
            void Back(Action action)
            {
                var post = SpellIdlePost;
                if (post != null) post(action);
                else dispatcher.Post(action, global::Avalonia.Threading.DispatcherPriority.Background);
            }
            void Work()
            {
                try { AskTheWorker(batch); }
                catch (Exception e)
                {
                    batch.Answers = null;
                    batch.Failures.Add((string.Empty, e));
                }
                Back(() => FinishBatch(batch));
            }
            var run = SpellBackgroundRun;
            if (run != null) run(Work);
            else System.Threading.ThreadPool.QueueUserWorkItem(_ => Work());
        }

        /// <summary>On the worker: each word is asked of its checker, in order. A checker that throws is left alone for the rest of the batch and its words are
        /// taken as right; the field's thread hears of it afterwards. Nothing of the field is touched here but the batch.</summary>
        static void AskTheWorker(SpellBatch batch)
        {
            var answers = new bool[batch.Asked.Count];
            HashSet<string>? failed = null;
            for (int i = 0; i < answers.Length; i++)
            {
                var checker = (GuardedChecker)batch.Asked[i].Checker!;
                if (failed != null && failed.Contains(checker.Ws)) { answers[i] = true; continue; }
                try { answers[i] = checker.CheckOnWorker(batch.Asked[i].Word); }
                catch (Exception e)
                {
                    (failed ??= new HashSet<string>()).Add(checker.Ws);
                    batch.Failures.Add((checker.Ws, e));
                    answers[i] = true;
                }
            }
            batch.Answers = answers;
        }

        /// <summary>On the field's thread: a batch has come back. Failures are reported (once per writing system, as on the synchronous path) when checking has not
        /// started over since; the answers are applied only when the paragraph is the one that was asked about, in the state and at the stamp it had, and
        /// the checking is of the same epoch. Otherwise they are dropped and the text is asked again.</summary>
        void FinishBatch(SpellBatch batch)
        {
            _spellInFlight = false;
            if (batch.Epoch == _spellEpoch)
                foreach (var (ws, error) in batch.Failures) SpellFailure(ws, error);
            var paragraphs = Editor.Document.Paragraphs;
            int index = _spellHost == null ? -1 : _info.IndexOf(batch.Info);
            bool current = batch.Answers != null && index >= 0 && index < paragraphs.Count && ReferenceEquals(paragraphs[index], batch.Paragraph)
                && batch.Epoch == _spellEpoch && ReferenceEquals(batch.Info.Spell, batch.State) && batch.Paragraph.ChangeStamp == batch.Stamp;
            if (current)
            {
                for (int i = 0; i < batch.Asked.Count; i++)
                    if (!batch.Answers![i]) batch.Fresh.Add(MarkOf(batch.Asked[i].Misspelled()));
                ApplyStep(batch.State, batch.ZoneStart, batch.Pos, batch.Done, batch.RangeEnd, batch.Fresh);
                _layouts[index]?.SetSpellingMarks(batch.State.Marks);
                InvalidateVisual();
            }
            RequestSpellingIdle();
        }
    }
}
