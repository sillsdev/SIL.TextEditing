using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The field's thread check and its pause-timer fallback use the field's own dispatcher (issue #68).</summary>
    public class DispatcherThreadTests
    {
#if NET10_0_OR_GREATER
        /// <summary>Runs <paramref name="body"/> on a new thread that owns an Avalonia dispatcher of its own (Avalonia 12 lets a
        /// control belong to a dispatcher other than the main UI thread's), and returns once it has finished. The thread has none
        /// of the platform services the headless session gives the main UI thread, so a field there cannot be laid out.</summary>
        static void OnSecondDispatcher(Action<global::Avalonia.Threading.Dispatcher> body)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try { body(global::Avalonia.Threading.Dispatcher.CurrentDispatcher); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            thread.Join();
            if (failure != null) throw new Exception("on the second dispatcher: " + failure.Message, failure);
        }

        static global::Avalonia.Threading.Dispatcher MainDispatcher()
        {
            global::Avalonia.Threading.Dispatcher? main = null;
            Headless.Run(() => main = global::Avalonia.Threading.Dispatcher.UIThread);
            return main!;
        }

        [Fact]
        public void A_field_on_a_second_dispatcher_takes_edits_from_its_own_thread_and_refuses_the_main_UI_thread()
        {
            var main = MainDispatcher();
            RichTextField? field = null;
            OnSecondDispatcher(d =>
            {
                field = new RichTextField(OneParagraph("abcd"));
                Assert.Same(d, field.Dispatcher);
                Assert.NotSame(main, field.Dispatcher);
                field.Editor.AccessCheck!();                      // its own thread: allowed
            });
            Exception? fromMain = null;
            Headless.Run(() => fromMain = Record.Exception(() => field!.Editor.TypeText("y")));
            Assert.IsType<InvalidOperationException>(fromMain);
            Assert.Contains("dispatcher the field belongs to", fromMain!.Message);
            Assert.Equal("abcd", field!.Editor.Document.Paragraphs[0].Text);   // refused before it touched the document
        }

        [Fact]
        public void The_pause_timer_fallback_for_a_field_on_a_second_dispatcher_is_handed_to_that_dispatcher_and_settles()
        {
            var main = MainDispatcher();
            var restart = typeof(RichTextField).GetMethod("RestartPauseTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var onScreen = typeof(RichTextField).GetField("_onScreen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var scheduledOn = new List<int>();
            RichTextField? field = null;
            using var ready = new ManualResetEventSlim();
            using var asked = new ManualResetEventSlim();
            int secondThread = 0;
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var d = global::Avalonia.Threading.Dispatcher.CurrentDispatcher;
                    secondThread = Environment.CurrentManagedThreadId;
                    field = new RichTextField(OneParagraph("ab"));
                    field.PauseScheduler = (after, run) => { scheduledOn.Add(Environment.CurrentManagedThreadId); return new NoPause(); };
                    onScreen.SetValue(field, true);
                    // The commit starts the pause; the layout that follows it needs services this thread lacks, so it throws after.
                    Record.Exception(() => field.Editor.TypeText("x"));
                    Assert.True(field.Editor.HasOpenUndoGroup);
                    Assert.Equal(new[] { secondThread }, scheduledOn);
                    scheduledOn.Clear();
                    ready.Set();
                    asked.Wait();
                    d.RunJobs();                    // the hand-off, if it was made to this dispatcher, runs here
                    d.RunJobs();                    // and is not posted again
                }
                catch (Exception e) { failure = e; ready.Set(); }
            });
            thread.Start();
            ready.Wait();
            if (failure == null)
                Headless.Run(() => restart.Invoke(field, null));   // the main UI thread asks for the restart; the field is not its own
            asked.Set();
            thread.Join();
            if (failure != null) throw new Exception("on the second dispatcher: " + failure.Message, failure);
            Assert.Equal(new[] { secondThread }, scheduledOn);     // scheduled once, on the field's thread
        }

        sealed class NoPause : IDisposable { public void Dispose() { } }
#endif
    }
}
