using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Notation
{
    internal enum DestKind
    {
        /// <summary>Anything that is not a project path: <c>https://...</c>, <c>mailto:...</c>, a relative web address.</summary>
        External,
        /// <summary>A path from the project root (form A), or the alias <c>/objects/&lt;guid&gt;</c> (form E).</summary>
        RootPath,
        /// <summary>A path relative to the file (form B).</summary>
        Relative,
        /// <summary>A <c>silfw://</c> back-link (form C).</summary>
        Silfw,
        /// <summary>A <c>motif://projects/...</c> back-link (form D).</summary>
        Motif,
    }

    /// <summary>A link destination, read: which of the five spellings of spec 4.4 it is, and what it names.</summary>
    internal sealed class Destination
    {
        public DestKind Kind;
        public string Text = string.Empty;
        public ProjectPath? Path;
        public Guid? Guid;
        public string? Project;
        public SilfwUrlParts? Silfw;
        /// <summary>For a relative destination: true when it looks like a project link (starts with <c>./</c> or <c>../</c>, or ends in <c>.fw.md</c>).</summary>
        public bool LooksRelativeToProject;
        /// <summary>For a relative destination with no <see cref="MarkdownOptions.LinkBase"/>.</summary>
        public bool NoBase;
    }

    internal static class LinkSyntax
    {
        const string MotifPrefix = "motif://projects/";

        public static Destination Classify(string dest, MarkdownOptions options)
        {
            var d = new Destination { Text = dest };
            if (dest.StartsWith("silfw://", StringComparison.OrdinalIgnoreCase) && SilfwUrl.TryParse(dest, out var parts))
            {
                d.Kind = DestKind.Silfw;
                d.Silfw = parts;
                d.Guid = parts.Guid;
                if (parts.Database != null && parts.Database != SilfwUrl.CurrentProject && parts.Database != options.SilfwDatabase) d.Project = parts.Database;
                return d;
            }
            if (dest.StartsWith(MotifPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string rest = dest.Substring(MotifPrefix.Length);
                string? query = null;
                int q = rest.IndexOf('?');
                if (q >= 0) { query = rest.Substring(q + 1); rest = rest.Substring(0, q); }
                int slash = rest.IndexOf('/');
                string project = ProjectPath.PercentDecode(slash < 0 ? rest : rest.Substring(0, slash));
                string pathText = slash < 0 ? "/" : rest.Substring(slash);
                var path = ProjectPath.Parse(pathText);
                if (path != null)
                {
                    d.Kind = DestKind.Motif;
                    d.Path = path;
                    d.Guid = path.ObjectGuid;
                    if (query != null)
                    {
                        foreach (string pair in query.Split('&'))
                            if (pair.StartsWith("guid=", StringComparison.Ordinal) && Guid.TryParse(pair.Substring(5), out var g)) d.Guid = g;
                    }
                    if (options.ProjectName == null || !string.Equals(project, options.ProjectName, StringComparison.Ordinal)) d.Project = project;
                    return d;
                }
            }
            if (dest.Length > 0 && dest[0] == '/')
            {
                var path = ProjectPath.Parse(dest);
                if (path != null)
                {
                    d.Kind = DestKind.RootPath;
                    d.Path = path;
                    d.Guid = path.ObjectGuid;
                    return d;
                }
            }
            bool hasScheme = false;
            int colon = dest.IndexOf(':');
            if (colon > 0)
            {
                hasScheme = true;
                for (int i = 0; i < colon; i++) { char c = dest[i]; if (!(char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.')) { hasScheme = false; break; } }
            }
            if (!hasScheme && dest.Length > 0 && dest[0] != '#' && dest[0] != '?' && dest[0] != '/')
            {
                string trimmed = dest;
                int hash = trimmed.IndexOf('#');
                string withoutFragment = hash < 0 ? trimmed : trimmed.Substring(0, hash);
                bool looks = dest.StartsWith("./", StringComparison.Ordinal) || dest.StartsWith("../", StringComparison.Ordinal) || dest == ".." || dest == "."
                    || withoutFragment.EndsWith(".fw.md", StringComparison.Ordinal);
                if (looks)
                {
                    d.LooksRelativeToProject = true;
                    d.Kind = DestKind.Relative;
                    if (options.LinkBase == null) { d.NoBase = true; return d; }
                    var path = ResolveRelative(options.LinkBase, dest);
                    if (path != null) { d.Path = path; d.Guid = path.ObjectGuid; return d; }
                }
            }
            d.Kind = DestKind.External;
            return d;
        }

        /// <summary>A relative destination against the path of the file that holds it, as RFC 3986 resolves it; the extension <c>.fw.md</c> is dropped.</summary>
        static ProjectPath? ResolveRelative(string baseFile, string relative)
        {
            string fragment = string.Empty;
            int hash = relative.IndexOf('#');
            if (hash >= 0) { fragment = relative.Substring(hash); relative = relative.Substring(0, hash); }
            var parts = new List<string>(baseFile.Split('/'));
            if (parts.Count > 0) parts.RemoveAt(parts.Count - 1);
            if (parts.Count > 0 && parts[0].Length == 0) parts.RemoveAt(0);
            foreach (string piece in relative.Split('/'))
            {
                if (piece == "." || piece.Length == 0) continue;
                if (piece == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(piece);
            }
            if (parts.Count > 0 && parts[parts.Count - 1].EndsWith(".fw.md", StringComparison.Ordinal))
                parts[parts.Count - 1] = parts[parts.Count - 1].Substring(0, parts[parts.Count - 1].Length - 6);
            return ProjectPath.Parse("/" + string.Join("/", parts) + fragment);
        }

        // ---------------------------------------------------------------- writing

        /// <summary>The path to a file-relative destination: from the directory of <paramref name="baseFile"/> to <paramref name="target"/>, with <c>.fw.md</c> on the last segment.</summary>
        public static string RelativeTo(string baseFile, ProjectPath target)
        {
            var from = new List<string>(baseFile.Split('/'));
            if (from.Count > 0 && from[0].Length == 0) from.RemoveAt(0);
            if (from.Count > 0) from.RemoveAt(from.Count - 1);
            var to = new List<string>();
            string text = target.ToString();
            string fragment = string.Empty;
            int hash = text.IndexOf('#');
            if (hash >= 0) { fragment = text.Substring(hash); text = text.Substring(0, hash); }
            foreach (string piece in text.Split('/')) if (piece.Length > 0) to.Add(piece);
            int common = 0;
            while (common < from.Count && common < to.Count - 1 && from[common] == to[common]) common++;
            var sb = new StringBuilder();
            for (int i = common; i < from.Count; i++) sb.Append("../");
            if (sb.Length == 0) sb.Append("./");
            for (int i = common; i < to.Count; i++) { if (i > common) sb.Append('/'); sb.Append(to[i]); }
            sb.Append(".fw.md").Append(fragment);
            return sb.ToString();
        }

        /// <summary>The destination as it stands in <c>(...)</c>: in angle brackets when it holds a space or is empty (a path with a space is written
        /// <c>&lt;/grammar/phonological-rules/Nasal assimilation&gt;</c>), and with <c>|</c> escaped in a table row.</summary>
        public static string Wrap(string dest, bool inTable)
        {
            bool angle = dest.Length == 0;
            foreach (char c in dest) if (c == ' ' || c == '\t' || c == '\n' || c == '\r') angle = true;
            if (dest.Length > 0 && dest[0] == '<') angle = true;
            if (inTable) dest = dest.Replace("|", "\\|");
            return angle ? "<" + dest + ">" : dest;
        }

        /// <summary>True when a destination is plain enough to be written as it is, for an external link: visible ASCII with no bracket, parenthesis, angle bracket,
        /// backslash, backtick or entity-looking ampersand. Anything else is percent-encoded where that is lossless, and otherwise carried in <c>url=</c>.</summary>
        public static bool IsPlainUrl(string url)
        {
            if (url.Length == 0) return false;
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (c <= ' ' || c > '~') return false;
                if ("()<>[]\\`{}".IndexOf(c) >= 0) return false;
                if (c == '&' && AttributeText.ReadsAsReference(url, i)) return false;
            }
            return true;
        }
    }
}
