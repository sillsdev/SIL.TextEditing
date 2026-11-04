using System;
using System.Collections.Generic;
using System.Globalization;
using SIL.TextEditing;

namespace SIL.TextEditing.Playground
{
    /// <summary>A ready-made set of documents for the playground. A scenario with <see cref="Many"/> set builds
    /// one small document per field, N of them; every other scenario builds exactly one document.</summary>
    public sealed class Scenario
    {
        public Scenario(string name, string description, Func<int, List<Document>> build, bool many = false)
        {
            Name = name;
            Description = description;
            Build = build;
            Many = many;
        }

        public string Name { get; }
        public string Description { get; }
        public bool Many { get; }
        /// <summary>Builds the documents; the argument is the field count for a "many fields" scenario.</summary>
        public Func<int, List<Document>> Build { get; }
        public override string ToString() => Name;
    }

    public static class Scenarios
    {
        public const string MixedBidi = "Latin and Arabic";
        public const string IndicThai = "Devanagari, Tamil, Thai";
        public const string Unicode = "NFD, marks, surrogates, PUA";
        public const string Picture = "Inline picture (ORC)";
        public const string Drawing = "Drawing properties";
        public const string Paragraphs = "Paragraph formatting";
        public const string Opaque = "Unknown properties";
        public const string Long = "Long document (200 paragraphs)";
        public const string ReadOnlyRuns = "Read-only runs";
        public const string ManyFields = "Many fields";
        public const string Empty = "Empty";

        /// <summary>The object data of the sample picture runs; the playground draws a coloured box for each.</summary>
        public const string RedPicture = "\u0008pic-red";
        public const string BluePicture = "\u0008pic-blue";

        public static IReadOnlyList<Scenario> All { get; } = new List<Scenario>
        {
            new Scenario(MixedBidi, "An LTR paragraph with Arabic inside, an RTL paragraph with Latin and digits inside, and a plain Arabic one. Try arrows, mouse selection and typing at the boundaries.", _ => One(MixedDoc())),
            new Scenario(IndicThai, "Hindi conjuncts and vowel signs, Tamil, and Thai with stacked marks. Carets must stop on clusters, never inside one.", _ => One(IndicDoc())),
            new Scenario(Unicode, "Decomposed accents (NFD) next to composed ones, stacked combining marks, surrogate pairs, emoji sequences and private-use characters. The inspector shows the exact code units.", _ => One(Stored(UnicodeDoc(), System.Text.NormalizationForm.FormD))),
            new Scenario(Picture, "Inline picture objects (U+FFFC with object data), in LTR text, in RTL text and side by side. Backspace or Delete removes a picture whole. Use Picture to insert one.", _ => One(PictureDoc())),
            new Scenario(Drawing, "Every drawing property of a FieldWorks text string, from the run's own properties and from the host's named styles (the playground keeps a small stylesheet). Pick a style in the toolbar to apply it.", _ => One(DrawingDoc())),
            new Scenario(Paragraphs, "Every paragraph drawing property from the host's paragraph styles and from a paragraph's own properties: alignment, first-line and hanging indents, margins, spacing, line height, colour, borders, a line limit, bullets and numbers. Pick a paragraph style in the toolbar to try one on the paragraph at the caret.", _ => One(ParagraphsDoc())),
            new Scenario(Opaque, "Runs with property numbers the editor does not understand, and a paragraph style. They must survive typing, formatting, split, join, undo and copy and paste untouched.", _ => One(OpaqueDoc())),
            new Scenario(ReadOnlyRuns, "A label that may not be edited in front of editable text, a semi-editable run, and a paragraph that is all read-only. The caret moves to the nearest place where editing is allowed; typing, Delete, Backspace, cut and paste are refused across read-only text; selection and copy still work.", _ => One(ReadOnlyDoc())),
            new Scenario(Long, "200 paragraphs in mixed writing systems: scrolling, caret at the far end, and cost of edits in a long document.", _ => One(LongDoc(200))),
            new Scenario(ManyFields, "N small fields in one window: tab between them by clicking, undo is per field, the inspector follows the focused field.", ManyDocs, many: true),
            new Scenario(Empty, "One empty paragraph: type from scratch.", _ => One(EmptyDoc())),
        };

        public static Scenario Find(string name)
        {
            foreach (var s in All)
                if (s.Name == name) return s;
            throw new ArgumentException("No scenario named " + name, nameof(name));
        }

        static Document Stored(Document d, System.Text.NormalizationForm form) { d.Normalization = form; return d; }

        static List<Document> One(Document d) => new List<Document> { d };

        static Paragraph Para(string defaultWs, bool rtl, params string[] wsAndText)
        {
            var p = new Paragraph { DefaultWs = defaultWs, Rtl = rtl };
            for (int i = 0; i < wsAndText.Length; i += 2)
                p.Runs.Add(new Run { Ws = wsAndText[i], Text = wsAndText[i + 1] });
            return p;
        }

        static Document Doc(params Paragraph[] paragraphs)
        {
            var d = new Document();
            d.Paragraphs.AddRange(paragraphs);
            // The host assigns keys, as FieldWorks assigns StTxtPara GUIDs; the field only carries them.
            foreach (var p in paragraphs) p.Key = Guid.NewGuid();
            return d;
        }

        static Document MixedDoc() => Doc(
            Para("en", false, "en", "The word ", "ar", "كتاب", "en", " means book; ", "ar", "مرحبا بالعالم", "en", " means hello world."),
            Para("ar", true, "ar", "هذه جملة عربية فيها ", "en", "English words", "ar", " و ", "en", "1234", "ar", " أرقام."),
            Para("ar", true, "ar", "فقرة عربية قصيرة."),
            Para("en", false, "en", "Plain English to finish, with an ", "fr", "expression française", "en", " in the middle."));

        static Document IndicDoc() => Doc(
            Para("hi", false, "hi", "हिन्दी भाषा: क्षत्रिय, ज्ञान, श्रीमान, कृपया, पुस्तकें"),
            Para("ta", false, "ta", "தமிழ் மொழி: கொக்கு, ஸ்ரீ, க்ஷ, தொலைக்காட்சி"),
            Para("th", false, "th", "ภาษาไทย: ที่นี่ กำลัง น้ำ ผู้ชาย ปลาทู"),
            Para("en", false, "en", "Mixed: ", "hi", "नमस्ते", "en", " / ", "ta", "வணக்கம்", "en", " / ", "th", "สวัสดี", "en", "."));

        static Document UnicodeDoc() => Doc(
            Para("en", false, "en", "NFC: café ", "en", "NFD: café ", "en", "stacked: ạ̈ ố"),
            Para("en", false, "en", "Surrogates: \U0001F600 \U00020000 \U0001D11E, ZWJ family \U0001F468‍\U0001F469‍\U0001F467, flag \U0001F1EB\U0001F1F7"),
            Para("en", false, "en", "Private use: ", "x-pua", "", "en", " then Hangul jamo 한"),
            Para("en", false, "en", "Format characters: a‌b (ZWNJ) c‍d (ZWJ) e­f (soft hyphen)"));

        static Document PictureDoc()
        {
            var inline = Para("en", false, "en", "Before ");
            inline.Runs.Add(Run.Object("en", RedPicture));
            inline.Runs.Add(new Run { Ws = "en", Text = " after, then two together " });
            inline.Runs.Add(Run.Object("en", BluePicture));
            inline.Runs.Add(Run.Object("en", RedPicture));
            inline.Runs.Add(new Run { Ws = "en", Text = " done." });

            var rtl = Para("ar", true, "ar", "نص عربي ");
            rtl.Runs.Add(Run.Object("ar", BluePicture));
            rtl.Runs.Add(new Run { Ws = "ar", Text = " بعد الصورة" });

            return Doc(inline, rtl, Para("en", false, "en", "Put the caret here and press Picture."));
        }

        /// <summary>A run with its text, writing system and integer properties given as (number, value, variation) triples.</summary>
        static Run Styled(string ws, string text, params int[] props)
        {
            var r = new Run { Ws = ws, Text = text };
            for (int i = 0; i < props.Length; i += 3) r.Props.SetInt(props[i], props[i + 1], props[i + 2]);
            return r;
        }

        static Run NamedStyle(string ws, string text, string style)
        {
            var r = new Run { Ws = ws, Text = text };
            r.Props.SetStr(TextPropertyNumbers.NamedStyle, style);
            return r;
        }

        static Paragraph Runs(string defaultWs, string style, params Run[] runs)
        {
            var p = new Paragraph { DefaultWs = defaultWs, Style = style };
            p.Runs.AddRange(runs);
            return p;
        }

        static Document DrawingDoc()
        {
            const int mp = TextPropertyNumbers.VariationMilliPoint;
            var heading = Runs("en", "Heading 1", new Run { Ws = "en", Text = "Drawing properties" });
            var weights = Runs("en", "",
                new Run { Ws = "en", Text = "Weights: normal, " },
                Styled("en", "bold", TextPropertyNumbers.Bold, 1, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "weight 300", TextPropertyNumbers.Bold, 300, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "weight 900", TextPropertyNumbers.Bold, 900, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "italic", TextPropertyNumbers.Italic, 1, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "bold italic", TextPropertyNumbers.Bold, 1, 0, TextPropertyNumbers.Italic, 1, 0),
                new Run { Ws = "en", Text = ", and " },
                NamedStyle("en", "Strong (a style)", "Strong"),
                new Run { Ws = "en", Text = " and " },
                NamedStyle("ar", "قوي (Strong in Arabic is larger)", "Strong"),
                new Run { Ws = "en", Text = "." });
            var sizes = Runs("en", "",
                new Run { Ws = "en", Text = "Sizes: 16, " },
                Styled("en", "24 point", TextPropertyNumbers.FontSize, 24000, mp),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "half size", TextPropertyNumbers.FontSize, 5000, TextPropertyNumbers.VariationRelative),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "named large", TextPropertyNumbers.FontSize, 4, TextPropertyNumbers.VariationEnum),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "named smaller", TextPropertyNumbers.FontSize, 7, TextPropertyNumbers.VariationEnum),
                new Run { Ws = "en", Text = ", " },
                NamedStyle("en", "Large (a style)", "Large"),
                new Run { Ws = "en", Text = "; fonts " },
                Fonts("<default serif>"),
                new Run { Ws = "en", Text = ", " },
                Fonts("<default sans serif>"),
                new Run { Ws = "en", Text = ", " },
                Fonts("<default monospace>"),
                new Run { Ws = "en", Text = ", " },
                Fonts("Comic Sans MS"),
                new Run { Ws = "en", Text = "." });
            var colours = Runs("en", "",
                new Run { Ws = "en", Text = "Colours: " },
                Styled("en", "red text", TextPropertyNumbers.ForeColor, SampleStyles.Red, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "green on pale yellow", TextPropertyNumbers.ForeColor, SampleStyles.Green, 0, TextPropertyNumbers.BackColor, SampleStyles.PaleYellow, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "transparent colour is the default", TextPropertyNumbers.ForeColor, unchecked((int)0xC0000000), 0),
                new Run { Ws = "en", Text = ", " },
                NamedStyle("en", "Warning (a style)", "Warning"),
                new Run { Ws = "en", Text = "." });
            var underlines = Runs("en", "",
                new Run { Ws = "en", Text = "Lines: " },
                Styled("en", "dotted", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineDotted, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "dashed", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineDashed, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "single", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "double", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineDouble, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "strikethrough", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineStrikethrough, 0),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "blue single", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle, 0, TextPropertyNumbers.UnderColor, SampleStyles.Blue, 0),
                new Run { Ws = "en", Text = ", " },
                NamedStyle("en", "Misspelled (a style: red double)", "Misspelled"),
                new Run { Ws = "en", Text = ", " },
                Styled("en", "scribble", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSquiggle, 0, TextPropertyNumbers.UnderColor, SampleStyles.Red, 0),
                new Run { Ws = "en", Text = " (squiggle), and " },
                Styled("en", "a squiggled run that is long enough to wrap onto a second line", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSquiggle, 0, TextPropertyNumbers.UnderColor, SampleStyles.Red, 0),
                new Run { Ws = "en", Text = "." });
            var shifts = Runs("en", "",
                new Run { Ws = "en", Text = "Position: H" },
                Styled("en", "2", TextPropertyNumbers.Superscript, 2, 0),
                new Run { Ws = "en", Text = "O, E=mc" },
                Styled("en", "2", TextPropertyNumbers.Superscript, 1, 0),
                new Run { Ws = "en", Text = ", " },
                NamedStyle("en", "super", "Superscript"),
                new Run { Ws = "en", Text = " and " },
                NamedStyle("en", "sub", "Subscript"),
                new Run { Ws = "en", Text = ", raised 4 pt (not drawn yet): " },
                Styled("en", "up", TextPropertyNumbers.Offset, 4000, mp),
                new Run { Ws = "en", Text = ", lowered (not drawn yet): " },
                Styled("en", "down", TextPropertyNumbers.Offset, -4000, mp),
                new Run { Ws = "en", Text = "." });
            var features = Runs("en", "",
                new Run { Ws = "en", Text = "Features: " },
                FontFeatures("1234567890 (default)", null),
                new Run { Ws = "en", Text = " " },
                FontFeatures("1234567890 (tnum)", "tnum=1"),
                new Run { Ws = "en", Text = "." });
            return Doc(heading, weights, sizes, colours, underlines, shifts, features);
        }

        const string Sentence = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly, and every wrapped line shows where its margins fall. ";

        static Paragraph Styled(string style, string text = Sentence)
        {
            var p = Para("en", false, "en", text);
            p.Style = style;
            return p;
        }

        static Document ParagraphsDoc()
        {
            var heading = Styled("Heading 1", "Paragraph formatting");
            var plain = Styled("", "Unstyled: starts at the leading edge. " + Sentence);
            var own = Para("en", false, "en", "Own properties: this paragraph is centred by its own bag, with no style. " + Sentence);
            own.Props.SetInt(TextPropertyNumbers.Align, 2);
            var arabic = Para("ar", true, "ar", "هذه فقرة عربية بأسلوب معلّق: السطر الأول يبدأ من الحافة اليمنى والسطور التالية تبدأ بعد المسافة البادئة. هذه فقرة عربية بأسلوب معلّق: السطر الأول يبدأ من الحافة اليمنى والسطور التالية تبدأ بعد المسافة البادئة.");
            arabic.Style = "Hanging";
            var arabicCentred = Para("ar", true, "ar", "فقرة عربية موسطة");
            arabicCentred.Style = "Centered";

            const string ArabicSentence = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة، وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار. ";
            Paragraph ArabicStyled(string style, string text)
            {
                var a = Para("ar", true, "ar", text);
                a.Style = style;
                return a;
            }
            return Doc(heading, plain,
                Styled("Centered", "Centred. " + Sentence), Styled("Right", "Right. " + Sentence), Styled("Trailing", "Trailing (right in LTR). " + Sentence),
                Styled("Justified", "Justified, but not its last line. " + Sentence + Sentence),
                Styled("First Line Indent", "First line indent. " + Sentence), Styled("Hanging", "Hanging indent. " + Sentence),
                Styled("Block Quote", "Block quote: indented on both sides. " + Sentence), own,
                Styled("Double Spaced", "Double spaced. " + Sentence), Styled("Exact 30", "Exactly 30 point lines. " + Sentence),
                Styled("Boxed", "Boxed: a border, padding and a background colour. " + Sentence), Styled("Two Lines Only", "Two lines only. " + Sentence + Sentence),
                Styled("List Number", "First numbered paragraph. " + Sentence), Styled("List Number", "Second numbered paragraph."), Styled("List Number", "Third."),
                Styled("List Roman", "In roman numerals."), Styled("List Roman", "Roman two."), Styled("List Letter", "A letter."), Styled("List Letter", "Another letter."),
                Styled("List Bullet", "A bullet. " + Sentence), Styled("List Bullet", "Another bullet."),
                arabic, arabicCentred,
                ArabicStyled("List Number", "فقرة مرقمة: " + ArabicSentence), ArabicStyled("List Number", "فقرة مرقمة ثانية."),
                Styled("List Flush", "Numbered with no hanging indent: the text starts after the number. " + Sentence),
                ArabicStyled("Centered", ArabicSentence + ArabicSentence), ArabicStyled("List Bullet", "نقطة: " + ArabicSentence),
                Styled("List Wide Label", "Wonderful words follow a label too wide for a narrow field."), ArabicStyled("List Wide Label", "المعلومات المهمة تأتي بعد عنوان عريض."),
                Styled("Exact 10", "xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx xxxxxxxx"));
        }

        static Run Fonts(string family)
        {
            var r = new Run { Ws = "en", Text = family };
            r.Props.SetStr(TextPropertyNumbers.FontFamily, family);
            return r;
        }

        static Run FontFeatures(string text, string? features)
        {
            var r = new Run { Ws = "en", Text = text };
            if (features != null) r.Props.SetStr(TextPropertyNumbers.FontVariations, features);
            return r;
        }

        static Document OpaqueDoc()
        {
            var styled = Para("en", false, "en", "A paragraph with a style (see the inspector).");
            styled.Style = "Block Quote";

            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "Plain, then " });
            var unknown = new Run { Ws = "en", Text = "unknown properties 9001 and 9002" };
            unknown.Props.SetInt(9001, 42, 1).SetStr(9002, "a;b=c<&>é");
            p.Runs.Add(unknown);
            p.Runs.Add(new Run { Ws = "en", Text = ", then bold with one of them: " });
            var boldAndUnknown = new Run { Ws = "en", Text = "bold + 9003" };
            boldAndUnknown.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn).SetInt(9003, 7);
            p.Runs.Add(boldAndUnknown);
            p.Runs.Add(new Run { Ws = "fr", Text = " et du français." });

            var charStyle = new Paragraph { DefaultWs = "fr" };
            var emph = new Run { Ws = "fr", Text = "Style de caractère" };
            emph.Props.SetStr(TextPropertyNumbers.CharStyle, "Emphasis");
            charStyle.Runs.Add(emph);
            charStyle.Runs.Add(new Run { Ws = "fr", Text = ", puis du texte ordinaire." });

            return Doc(styled, p, charStyle);
        }

        static Run Marked(string ws, string text, int editable)
        {
            var r = new Run { Ws = ws, Text = text };
            r.Props.SetInt(TextPropertyNumbers.Editable, editable);
            return r;
        }

        static Document ReadOnlyDoc()
        {
            var labelled = new Paragraph { DefaultWs = "en" };
            labelled.Runs.Add(Marked("en", "Gloss: ", TextPropertyNumbers.EditableNo));
            labelled.Runs.Add(new Run { Ws = "en", Text = "edit this part" });
            labelled.Runs.Add(Marked("en", " [fixed]", TextPropertyNumbers.EditableNo));
            labelled.Runs.Add(new Run { Ws = "en", Text = " and this" });

            var semi = new Paragraph { DefaultWs = "en" };
            semi.Runs.Add(Marked("en", "type-ahead: ", TextPropertyNumbers.EditableSemi));
            semi.Runs.Add(new Run { Ws = "en", Text = "typed" });

            var locked = new Paragraph { DefaultWs = "en" };
            locked.Runs.Add(Marked("en", "Entirely read-only paragraph.", TextPropertyNumbers.EditableNo));

            return Doc(labelled, semi, locked, Para("en", false, "en", "An ordinary paragraph."));
        }

        static readonly string[] Words = { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet" };

        static Document LongDoc(int count)
        {
            var d = new Document();
            for (int i = 0; i < count; i++)
            {
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                switch (i % 5)
                {
                    case 3:
                        d.Paragraphs.Add(Para("ar", true, "ar", "الفقرة " + n + " ", "en", Words[i % Words.Length], "ar", " نص عربي."));
                        break;
                    case 4:
                        d.Paragraphs.Add(Para("hi", false, "en", "Paragraph " + n + " ", "hi", "हिन्दी पाठ", "en", " " + Words[i % Words.Length] + "."));
                        break;
                    default:
                        d.Paragraphs.Add(Para("en", false, "en", "Paragraph " + n + ": " + Words[i % Words.Length] + " " + Words[(i + 3) % Words.Length] + " ", "fr", "texte français " + n, "en", ", and some more words to wrap the line when the window is narrow."));
                        break;
                }
            }
            return d;
        }

        static List<Document> ManyDocs(int count)
        {
            var result = new List<Document>();
            for (int i = 0; i < count; i++)
            {
                string n = (i + 1).ToString(CultureInfo.InvariantCulture);
                switch (i % 4)
                {
                    case 1: result.Add(Doc(Para("fr", false, "fr", "Champ " + n))); break;
                    case 2: result.Add(Doc(Para("ar", true, "ar", "حقل " + n))); break;
                    case 3: result.Add(Doc(Para("swh", false, "swh", "Sehemu " + n))); break;
                    default: result.Add(Doc(Para("en", false, "en", "Field " + n))); break;
                }
            }
            return result;
        }

        static Document EmptyDoc() => Doc(new Paragraph { DefaultWs = "en" });
    }
}
