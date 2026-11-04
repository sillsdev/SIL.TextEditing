using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>A small project: link kinds and tools like the spec's table, pictures by GUID, a few writing systems and styles.</summary>
    internal sealed class TestObjects : IObjectMapper
    {
        public const char ExternalPath = (char)4;
        public const char PictureType = (char)8;
        public const char OwnName = (char)5;

        /// <summary>Target names by GUID: the host's current names.</summary>
        public Dictionary<Guid, string> Names = new Dictionary<Guid, string>();
        public HashSet<Guid> Pictures = new HashSet<Guid>();
        public static readonly string[] Kinds = { "w", "rule", "exc", "tmpl", "cat", "GDR-1", "obj" };
        static readonly Dictionary<string, string> ToolOfKind = new Dictionary<string, string>
        {
            ["w"] = "lexiconEdit", ["rule"] = "PhonologicalRuleEdit", ["exc"] = "ProdRestrictEdit", ["tmpl"] = "AffixTemplatesEdit", ["cat"] = "posEdit",
        };

        public static string LinkData(string kind, Guid guid)
            => ExternalPath + "silfw://localhost/link?tool=" + ToolOfKind[kind] + "&guid=" + guid.ToString("D");

        public static string PictureData(Guid guid) => PictureType + EncodeGuid(guid);

        public static string EncodeGuid(Guid guid)
        {
            var bytes = guid.ToByteArray();
            var chars = new char[8];
            for (int i = 0; i < 8; i++) chars[i] = (char)(bytes[2 * i] | (bytes[2 * i + 1] << 8));
            return new string(chars);
        }

        static Guid DecodeGuid(string text)
        {
            var bytes = new byte[16];
            for (int i = 0; i < 8; i++) { bytes[2 * i] = (byte)text[i]; bytes[2 * i + 1] = (byte)(text[i] >> 8); }
            return new Guid(bytes);
        }

        public MarkdownObject? Describe(string objData)
        {
            if (objData.Length == 9 && objData[0] == PictureType)
            {
                var guid = DecodeGuid(objData.Substring(1));
                if (Pictures.Contains(guid)) return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = guid, Caption = "a picture" };
                return null;
            }
            if (objData.Length > 0 && objData[0] == ExternalPath)
            {
                string url = objData.Substring(1);
                const string prefix = "silfw://localhost/link?tool=";
                if (url.StartsWith(prefix, StringComparison.Ordinal))
                {
                    int amp = url.IndexOf("&guid=", StringComparison.Ordinal);
                    if (amp > 0 && Guid.TryParse(url.Substring(amp + 6), out var guid))
                    {
                        string tool = url.Substring(prefix.Length, amp - prefix.Length);
                        foreach (var kv in ToolOfKind)
                        {
                            if (kv.Value != tool) continue;
                            Names.TryGetValue(guid, out var name);
                            return new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, LinkKind = kv.Key, Guid = guid, Name = name };
                        }
                    }
                }
                return new MarkdownObject { Kind = MarkdownObjectKind.ExternalLink, Url = url };
            }
            return null;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            problem = null;
            switch (target.Kind)
            {
                case MarkdownObjectKind.ExternalLink:
                    objData = ExternalPath + (target.Url ?? string.Empty);
                    return true;
                case MarkdownObjectKind.Picture:
                    if (target.Guid is Guid pg && Pictures.Contains(pg)) { objData = PictureData(pg); return true; }
                    objData = string.Empty;
                    problem = "no picture " + target.Guid;
                    return false;
                default:
                    if (target.Guid is Guid g && ToolOfKind.ContainsKey(target.LinkKind ?? string.Empty)) { objData = LinkData(target.LinkKind!, g); return true; }
                    objData = string.Empty;
                    problem = "no " + target.LinkKind + " named " + target.Name;
                    return false;
            }
        }

        public bool IsKnownLinkKind(string kind) => Array.IndexOf(Kinds, kind) >= 0;
    }

    internal sealed class TestWritingSystems : IWritingSystemMapper
    {
        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => tag != "zzz-unknown";
    }

    internal static class Hosts
    {
        public static MarkdownOptions Project(IObjectMapper? objects = null) => new MarkdownOptions
        {
            WritingSystems = new TestWritingSystems(),
            Objects = objects ?? new TestObjects(),
        };
    }
}
