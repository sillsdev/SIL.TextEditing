using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Issue 101: a property bag changed in place moves the <see cref="Paragraph.ChangeStamp"/> of the paragraphs that hold it, however it is
    /// changed, so a host that edits a bag, or a paragraph's direction, behind the editor's back is noticed.</summary>
    public class BagChangeStampTests
    {
        static Paragraph Two() => Para("en", "en", "ab", "fr", "cd");

        static void MovesStamp(Paragraph p, Action change, string what)
        {
            long before = p.ChangeStamp;
            change();
            Assert.True(p.ChangeStamp != before, what + " did not move the change stamp");
        }

        [Fact]
        public void Every_way_of_changing_a_runs_bag_in_place_moves_the_stamp()
        {
            var p = Two();
            var bag = p.Runs[1].Props;
            MovesStamp(p, () => bag.SetInt(TextPropertyNumbers.Bold, 1), "SetInt");
            MovesStamp(p, () => bag.SetInt(TextPropertyNumbers.Bold, 0), "SetInt (another value)");
            MovesStamp(p, () => bag.SetStr(TextPropertyNumbers.FontFamily, "Arial"), "SetStr");
            MovesStamp(p, () => bag.IntProps[TextPropertyNumbers.Italic] = new IntPropValue(0, 1), "an IntProps write");
            MovesStamp(p, () => bag.StrProps[TextPropertyNumbers.NamedStyle] = "Emphasis", "a StrProps write");
            MovesStamp(p, () => bag.IntProps.Add(TextPropertyNumbers.Underline, new IntPropValue(0, 3)), "IntProps.Add");
            MovesStamp(p, () => bag.IntProps.Remove(TextPropertyNumbers.Underline), "IntProps.Remove");
            MovesStamp(p, () => bag.StrProps.Remove(TextPropertyNumbers.NamedStyle), "StrProps.Remove");
            MovesStamp(p, () => ((ICollection<KeyValuePair<int, string>>)bag.StrProps).Add(new KeyValuePair<int, string>(TextPropertyNumbers.FontVariations, "x")), "an ICollection add");
            MovesStamp(p, () => bag.IntProps.Clear(), "IntProps.Clear");
        }

        [Fact]
        public void A_change_that_changes_nothing_leaves_the_stamp_alone()
        {
            var p = Two();
            var bag = p.Runs[0].Props;
            bag.SetInt(TextPropertyNumbers.Bold, 1);
            bag.SetStr(TextPropertyNumbers.FontFamily, "Arial");
            long stamp = p.ChangeStamp;
            bag.SetInt(TextPropertyNumbers.Bold, 1);
            bag.SetStr(TextPropertyNumbers.FontFamily, "Arial");
            Assert.False(bag.IntProps.Remove(TextPropertyNumbers.Italic));
            bag.IntProps.Remove(TextPropertyNumbers.Italic);
            Assert.Equal(stamp, p.ChangeStamp);
            bag.IntProps.Clear();
            long cleared = p.ChangeStamp;
            Assert.NotEqual(stamp, cleared);
            bag.IntProps.Clear();                       // nothing left to clear
            Assert.Equal(cleared, p.ChangeStamp);
        }

        [Fact]
        public void The_paragraphs_own_bag_moves_the_stamp()
        {
            var p = Two();
            MovesStamp(p, () => p.Props.SetInt(TextPropertyNumbers.LineHeight, 12000), "the paragraph's bag SetInt");
            MovesStamp(p, () => p.Props.IntProps[TextPropertyNumbers.Align] = new IntPropValue(0, 2), "the paragraph's bag indexer");
            MovesStamp(p, () => p.Props.IntProps.Remove(TextPropertyNumbers.Align), "the paragraph's bag Remove");
        }

        [Fact]
        public void Direction_and_default_writing_system_move_the_stamp_only_when_they_change()
        {
            var p = Two();
            MovesStamp(p, () => p.Rtl = true, "Rtl");
            long stamp = p.ChangeStamp;
            p.Rtl = true;
            p.DefaultWs = "en";
            Assert.Equal(stamp, p.ChangeStamp);
            MovesStamp(p, () => p.DefaultWs = "ar", "DefaultWs");
        }

        [Fact]
        public void A_bag_a_run_let_go_of_no_longer_moves_the_paragraph()
        {
            var p = Two();
            var old = p.Runs[0].Props;
            p.Runs[0].Props = new PropertyBag();
            long stamp = p.ChangeStamp;
            old.SetInt(TextPropertyNumbers.Bold, 1);
            Assert.Equal(stamp, p.ChangeStamp);
            MovesStamp(p, () => p.Runs[0].Props.SetInt(TextPropertyNumbers.Bold, 1), "the new bag");
        }

        [Fact]
        public void A_run_taken_out_of_the_paragraph_no_longer_moves_it()
        {
            var p = Two();
            var run = p.Runs[1];
            p.Runs.RemoveAt(1);
            long stamp = p.ChangeStamp;
            run.Props.SetInt(TextPropertyNumbers.Bold, 1);
            Assert.Equal(stamp, p.ChangeStamp);
        }

        [Fact]
        public void A_copy_of_a_bag_is_not_held_by_whoever_holds_the_original()
        {
            var p = Two();
            var copy = p.Runs[0].Props.Clone();
            long stamp = p.ChangeStamp;
            copy.SetInt(TextPropertyNumbers.Bold, 1);
            Assert.Equal(stamp, p.ChangeStamp);
            p.Runs[0].Props = copy;                       // until a run takes it
            MovesStamp(p, () => copy.SetInt(TextPropertyNumbers.Bold, 0), "the adopted copy");
        }

        [Fact]
        public void A_bag_shared_by_runs_of_two_paragraphs_moves_both_until_one_lets_go()
        {
            var a = Two();
            var b = Two();
            var shared = new PropertyBag();
            a.Runs[0].Props = shared;
            b.Runs[1].Props = shared;
            long sa = a.ChangeStamp, sb = b.ChangeStamp;
            shared.SetInt(TextPropertyNumbers.Bold, 1);
            Assert.NotEqual(sa, a.ChangeStamp);
            Assert.NotEqual(sb, b.ChangeStamp);
            b.Runs[1].Props = new PropertyBag();          // b lets go of it
            sa = a.ChangeStamp; sb = b.ChangeStamp;
            shared.SetInt(TextPropertyNumbers.Bold, 0);
            Assert.NotEqual(sa, a.ChangeStamp);
            Assert.Equal(sb, b.ChangeStamp);
        }

        [Fact]
        public void A_run_with_no_paragraph_and_a_bag_with_no_holder_take_changes_quietly()
        {
            var run = new Run { Text = "x" };
            run.Props.SetInt(TextPropertyNumbers.Bold, 1);
            new PropertyBag().SetInt(TextPropertyNumbers.Bold, 1).SetStr(TextPropertyNumbers.FontFamily, "A");
            Assert.Equal(1, run.Props.IntProps[TextPropertyNumbers.Bold].Value);
        }

        [Fact]
        public void The_bags_dictionaries_still_behave_like_sorted_dictionaries()
        {
            var bag = new PropertyBag();
            bag.SetInt(9, 1).SetInt(3, 2).SetInt(6, 3).SetStr(133, "S").SetStr(1, "F");
            Assert.Equal(new[] { 3, 6, 9 }, bag.IntProps.Keys.ToArray());
            Assert.Equal(new[] { 2, 3, 1 }, bag.IntProps.Values.Select(v => v.Value).ToArray());
            Assert.Equal(new[] { 1, 133 }, bag.StrProps.Select(kv => kv.Key).ToArray());
            Assert.Equal(5, bag.Count);
            Assert.True(bag.IntProps.ContainsKey(6));
            Assert.True(bag.IntProps.TryGetValue(6, out var v) && v.Value == 3);
            Assert.False(bag.IntProps.TryGetValue(7, out _));
            Assert.Throws<KeyNotFoundException>(() => bag.IntProps[7]);
            Assert.True(bag.ContentEquals(bag.Clone()));
            IDictionary<int, string> asDictionary = bag.StrProps;
            Assert.Equal(2, asDictionary.Count);
            IReadOnlyDictionary<int, string> readOnly = bag.StrProps;
            Assert.Equal("S", readOnly[133]);
        }
    }
}
