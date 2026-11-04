using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>The one place where Avalonia 11.3 and 12 differ for this editor: the clipboard API.
    /// Everything else compiles unchanged for both.</summary>
    public static class ClipboardCompat
    {
        /// <summary>Decodes a custom-format clipboard payload: UTF-16LE (how Avalonia 11 writes strings) or UTF-8
        /// (a typical external writer).</summary>
        public static string Decode(byte[] bytes)
        {
            int n = bytes.Length;
            while (n > 0 && bytes[n - 1] == 0) n--;
            bool utf16 = bytes.Length >= 2 && bytes[1] == 0 && bytes[0] != 0;
            return utf16
                ? System.Text.Encoding.Unicode.GetString(bytes, 0, n + (n % 2))
                : System.Text.Encoding.UTF8.GetString(bytes, 0, n);
        }

        // One pair of format objects per clipboard format name: a string reader (how our own writer round-trips) and a byte reader (how
        // a native writer on Windows hands a custom payload over).
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string> Text, DataFormat<byte[]> Bytes)> Formats =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string>, DataFormat<byte[]>)>();

        static (DataFormat<string> Text, DataFormat<byte[]> Bytes) FormatsFor(string name) =>
            Formats.GetOrAdd(name, n => (DataFormat.CreateStringPlatformFormat(n), DataFormat.CreateBytesPlatformFormat(n)));

        /// <summary>Puts plain text and any number of named string formats on the clipboard together.</summary>
        public static async Task SetAsync(TopLevel? topLevel, string plain, IEnumerable<KeyValuePair<string, string>>? forms = null)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.SetText(plain);
            if (forms != null)
                foreach (var form in forms)
                    item.Set(FormatsFor(form.Key).Text, form.Value);
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        }

        /// <summary>The payload stored under a format name, or null when the clipboard has none.</summary>
        public static async Task<string?> GetStringAsync(TopLevel? topLevel, string formatName)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return null;
            var (format, formatBytes) = FormatsFor(formatName);
            try
            {
                var text = await clipboard.TryGetValueAsync(format).ConfigureAwait(true);
                if (text != null) return text;
            }
            catch (Exception)
            {
                // fall through to the byte reader
            }
            try
            {
                var bytes = await clipboard.TryGetValueAsync(formatBytes).ConfigureAwait(true);
                return bytes == null ? null : Decode(bytes);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static async Task<string?> GetTextAsync(TopLevel? topLevel)
        {
            var clipboard = topLevel?.Clipboard;
            return clipboard == null ? null : await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }
    }
}
