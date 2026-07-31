using System.IO;
using EpubLiteReader;

var path = args[0];
var (doc, chapters) = await EpubDoc.OpenWithChaptersAsync(path, "Untitled");
Console.WriteLine($"Title={doc.Title}; Spine={doc.SpineCount}; Chapters={chapters.Count}");
var disk = Path.Combine(doc.ExtractRoot, doc.SpinePaths[0].Replace('/', Path.DirectorySeparatorChar));
var ch1 = File.ReadAllText(disk);
if (ch1.Contains("<script", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("FAIL: script not stripped");
    return 1;
}
if (!ch1.Contains("Hello from EPUB"))
{
    Console.WriteLine("FAIL: content missing");
    return 1;
}
var hits = doc.Search("rivers").ToList();
Console.WriteLine($"SearchHits={hits.Count}");
doc.Dispose();
Console.WriteLine("OK");
return 0;
