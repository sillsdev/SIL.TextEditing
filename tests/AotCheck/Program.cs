using System;
using SIL.TextEditing;

// Touches the model so the publish has something to link; the check is the publish itself.
var clusters = GraphemeClusters.Starts("áb");
Console.WriteLine(clusters.Count == 2 ? "ok" : "unexpected");
