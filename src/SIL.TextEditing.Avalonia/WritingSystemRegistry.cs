using System.Collections.Generic;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How one writing system is drawn: font, size, direction and OpenType features. The host fills a
    /// registry from its own writing-system store.</summary>
    public sealed class WritingSystemInfo
    {
        public WritingSystemInfo(string tag, string font, double size, bool rtl = false, string? features = null)
        {
            Tag = tag;
            Font = font;
            Size = size;
            Rtl = rtl;
            Features = features;
        }

        public string Tag { get; }
        public string Font { get; }
        public double Size { get; }
        public bool Rtl { get; }
        /// <summary>OpenType features, comma separated ("lnum=1, ss01"), or null.</summary>
        public string? Features { get; }
    }

    public sealed class WritingSystemRegistry
    {
        readonly Dictionary<string, WritingSystemInfo> _byTag = new Dictionary<string, WritingSystemInfo>();

        public WritingSystemRegistry Add(WritingSystemInfo info)
        {
            _byTag[info.Tag] = info;
            return this;
        }

        /// <summary>The registered writing system, or a plain 16 point fallback for an unknown tag.</summary>
        public WritingSystemInfo Get(string? tag)
        {
            if (tag != null && _byTag.TryGetValue(tag, out var info)) return info;
            return new WritingSystemInfo(tag ?? "en", "Segoe UI", 16);
        }

        /// <summary>A small registry of common writing systems with Windows fonts.</summary>
        public static WritingSystemRegistry CreateDefault()
        {
            return new WritingSystemRegistry()
                .Add(new WritingSystemInfo("en", "Segoe UI", 16))
                .Add(new WritingSystemInfo("fr", "Segoe UI", 16))
                .Add(new WritingSystemInfo("ar", "Segoe UI", 18, true))
                .Add(new WritingSystemInfo("he", "Segoe UI", 18, true))
                .Add(new WritingSystemInfo("ta", "Nirmala UI", 18))
                .Add(new WritingSystemInfo("hi", "Nirmala UI", 18))
                .Add(new WritingSystemInfo("th", "Leelawadee UI", 18))
                .Add(new WritingSystemInfo("x-pua", "Segoe UI", 16));
        }
    }
}
