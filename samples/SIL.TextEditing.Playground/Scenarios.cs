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
        public const string Opaque = "Unknown properties";
        public const string Long = "Long document (200 paragraphs)";
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
            new Scenario(Opaque, "Runs with property numbers the editor does not understand, and a paragraph style. They must survive typing, formatting, split, join, undo and copy and paste untouched.", _ => One(OpaqueDoc())),
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
