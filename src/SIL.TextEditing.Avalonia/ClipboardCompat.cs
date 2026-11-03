using System;
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

        static readonly DataFormat<string> Format = DataFormat.CreateStringPlatformFormat(Interchange.ClipboardFormat);
        static readonly DataFormat<byte[]> FormatBytes = DataFormat.CreateBytesPlatformFormat(Interchange.ClipboardFormat);

        public static async Task SetAsync(TopLevel? topLevel, string plain, string xml)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.SetText(plain);
            item.Set(Format, xml);
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        }

        public static async Task<string?> GetXmlAsync(TopLevel? topLevel)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return null;
            // Our own writer round-trips as a string; a native writer on Windows hands the payload over as bytes.
            try
            {
                var text = await clipboard.TryGetValueAsync(Format).ConfigureAwait(true);
                if (text != null) return text;
            }
            catch (Exception)
            {
                // fall through to the byte reader
            }
            try
            {
                var bytes = await clipboard.TryGetValueAsync(FormatBytes).ConfigureAwait(true);
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
