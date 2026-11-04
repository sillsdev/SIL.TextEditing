using System;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

// Touches the model so the publish has something to link; the check is the publish itself.
var clusters = GraphemeClusters.Starts("áb");
Console.WriteLine(clusters.Count == 2 ? "ok" : "unexpected");

// Touches the Markdown converter so Markdig has something to link.
Console.WriteLine(SIL.TextEditing.Markdown.MarkdownConverter.Version == "0" ? "md ok" : "md unexpected");

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
