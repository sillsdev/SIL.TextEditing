using System.Collections.Generic;
using SIL.WritingSystems;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How one writing system is drawn: font, size, direction and OpenType features, read from a libpalaso
    /// <see cref="WritingSystemDefinition"/> (ADR 0002).</summary>
    public sealed class WritingSystemInfo
    {
        WritingSystemInfo(string id)
        {
            Tag = id;
            Font = "Segoe UI";
            Size = 16;
        }

        /// <summary>Plain 16 point fallback. It touches no libpalaso type, so a field needs no SLDR to show text. Provenance: our own readable default, not a
        /// FieldWorks value (FieldWorks' view engine starts from 10 pt, and a libpalaso writing system gives at least 10 pt); a host registers its writing systems.</summary>
        internal static WritingSystemInfo Fallback(string id) => new WritingSystemInfo(id);

        internal WritingSystemInfo(WritingSystemDefinition ws)
        {
            Tag = ws.Id;
            Font = ws.DefaultFont?.Name ?? "Segoe UI";
            Size = ws.GetDefaultFontSizeOrMinimum();
            Rtl = ws.RightToLeftScript;
            Features = ws.DefaultFont?.Features;
        }

        public string Tag { get; }
        public string Font { get; }
        public double Size { get; }
        public bool Rtl { get; }
        /// <summary>OpenType features, comma separated ("lnum=1, ss01"), or null.</summary>
        public string? Features { get; }
    }

    /// <summary>The host's writing systems, keyed by the id a run carries in <see cref="Run.Ws"/>.</summary>
    public sealed class WritingSystemRegistry
    {
        readonly Dictionary<string, WritingSystemInfo> _byId = new Dictionary<string, WritingSystemInfo>();

        /// <summary>Registers a definition under its <see cref="WritingSystemDefinition.Id"/>, which the host
        /// sets (a store sets it; a new definition has none). The registry reads the
        /// definition now; add it again after changing it.</summary>
        public WritingSystemRegistry Add(WritingSystemDefinition ws)
        {
            _byId[ws.Id] = new WritingSystemInfo(ws);
            return this;
        }

        /// <summary>How many writing systems are registered.</summary>
        public int Count => _byId.Count;

        /// <summary>True when the id is registered (a fallback for an unknown id does not count).</summary>
        public bool Contains(string? id) => id != null && _byId.ContainsKey(id);

        /// <summary>The registered writing system, or a plain 16 point fallback for an unknown id.</summary>
        public WritingSystemInfo Get(string? id)
        {
            if (id != null && _byId.TryGetValue(id, out var info)) return info;
            return WritingSystemInfo.Fallback(id ?? "en");
        }

        /// <summary>A definition with a default font, for samples and tests that have no writing-system store. Every
        /// <see cref="WritingSystemDefinition"/> constructor reads the SLDR language tables, so this starts the SLDR in
        /// offline mode if the host has not; a real host initialises it itself.</summary>
        public static WritingSystemDefinition Define(string tag, string font, float size, bool rtl = false, string? features = null)
        {
            if (!Sldr.IsInitialized) Sldr.Initialize(offlineTestMode: true);
            var ws = new WritingSystemDefinition(tag) { Id = tag, DefaultFontSize = size, RightToLeftScript = rtl };
            ws.DefaultFont = new FontDefinition(font) { Features = features };
            return ws;
        }

        /// <summary>A small registry of common writing systems with Windows fonts, for samples and tests (see <see cref="Define"/>).</summary>
        public static WritingSystemRegistry CreateDefault()
        {
            return new WritingSystemRegistry()
                .Add(Define("en", "Segoe UI", 16))
                .Add(Define("fr", "Segoe UI", 16))
                .Add(Define("ar", "Segoe UI", 18, true))
                .Add(Define("he", "Segoe UI", 18, true))
                .Add(Define("ta", "Nirmala UI", 18))
                .Add(Define("hi", "Nirmala UI", 18))
                .Add(Define("th", "Leelawadee UI", 18))
                .Add(Define("x-pua", "Segoe UI", 16));
        }
    }
}
