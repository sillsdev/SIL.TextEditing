using System;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

// Touches the model so the publish has something to link; the check is the publish itself.
var clusters = GraphemeClusters.Starts("áb");
Console.WriteLine(clusters.Count == 2 ? "ok" : "unexpected");

// Runs the Markdown converter both ways, so Markdig's parser, tables and our extension are linked and executed under AOT.
{
    const string md = "# Title\n\nHello **bold** [mang]{ws=ceb} [x](https://example.org)\n\n| A | B |\n| --- | --- |\n| 1 | 2 |\n\n::: {style=Example}\nline\n:::\n";
    var parsed = SIL.TextEditing.Markdown.MarkdownConverter.Parse(md);
    string written = SIL.TextEditing.Markdown.MarkdownConverter.ToMarkdown(parsed.Document);
    bool same = !parsed.HasErrors && parsed.Document.Paragraphs.Count == 5 && written == md;
    Console.WriteLine(same ? "md ok" : "md unexpected: " + written);
    if (!same) return 1;
}

// Runs the libpalaso path under AOT: SLDR offline init (reads langtags JSON with Newtonsoft) and a definition.
try
{
    var registry = new WritingSystemRegistry().Add(WritingSystemRegistry.Define("ar", "Segoe UI", 18, true, "lnum=1, ss01"));
    var info = registry.Get("ar");
    bool good = info.Rtl && info.Font == "Segoe UI" && info.Size == 18 && info.Features == "lnum=1, ss01";
    Console.WriteLine(good ? "ws ok" : "ws unexpected");
    return good ? 0 : 1;
}
catch (Exception e)
{
    Console.WriteLine("ws FAILED: " + e.GetType().Name + ": " + e.Message);
    return 1;
}
