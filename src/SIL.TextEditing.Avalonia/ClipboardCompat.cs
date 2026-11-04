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
        public static string Decode(byte[] bytes) => ClipboardPayload.Decode(bytes);

        // One pair of format objects per clipboard format name: a string reader (how our own writer round-trips) and a byte reader (how
        // a native writer on Windows hands a custom payload over).
        static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string> Text, DataFormat<byte[]> Bytes)> Formats =
            new System.Collections.Concurrent.ConcurrentDictionary<string, (DataFormat<string>, DataFormat<byte[]>)>();

        static (DataFormat<string> Text, DataFormat<byte[]> Bytes) FormatsFor(string name) =>
            Formats.GetOrAdd(name, n => (DataFormat.CreateStringPlatformFormat(n), DataFormat.CreateBytesPlatformFormat(n)));

        /// <summary>Puts plain text and any number of named formats on the clipboard together: a string goes in as text, a <c>byte[]</c> as raw bytes. A host
        /// object cannot go through Avalonia's clipboard (a host with such objects plugs in its own <see cref="IClipboardBackend"/>); it is left out.</summary>
        public static async Task SetAsync(TopLevel? topLevel, string plain, IEnumerable<KeyValuePair<string, object>>? forms = null)
        {
            var clipboard = topLevel?.Clipboard;
            if (clipboard == null) return;
            var transfer = new DataTransfer();
            var item = new DataTransferItem();
            item.SetText(plain);
            if (forms != null)
            {
                foreach (var form in forms)
                {
                    if (form.Value is string text) item.Set(FormatsFor(form.Key).Text, text);
                    else if (form.Value is byte[] bytes) item.Set(FormatsFor(form.Key).Bytes, bytes);
                    else System.Diagnostics.Trace.TraceWarning("Clipboard format " + form.Key + " holds a host object, which Avalonia's clipboard cannot store; left out");
                }
            }
            transfer.Add(item);
            await clipboard.SetDataAsync(transfer).ConfigureAwait(true);
        }

        /// <summary>The payload under a format name as it was stored: the string if it was text, else the raw bytes, else null. Never throws.</summary>
        public static async Task<object?> GetAsync(TopLevel? topLevel, string formatName)
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
            try { return await clipboard.TryGetValueAsync(formatBytes).ConfigureAwait(true); }
            catch (Exception) { return null; }
        }

        /// <summary>The payload under a format name as text (bytes decoded), or null.</summary>
        public static async Task<string?> GetStringAsync(TopLevel? topLevel, string formatName) =>
            ClipboardPayload.AsText(await GetAsync(topLevel, formatName).ConfigureAwait(true));

        public static async Task<string?> GetTextAsync(TopLevel? topLevel)
        {
            var clipboard = topLevel?.Clipboard;
            return clipboard == null ? null : await clipboard.TryGetTextAsync().ConfigureAwait(true);
        }
    }
}
