using System.IO;
using System.IO.Compression;
using System.Text;

namespace EpubLiteReader.Tests;

/// <summary>Builds throwaway EPUB (zip) fixtures in-memory for tests that need book
/// shapes the checked-in tools/fixtures/sample.epub doesn't cover: many spine items, or
/// a manifest entry that attempts a path traversal.</summary>
internal static class EpubFixtureBuilder
{
    /// <summary>Appears exactly once in every generated chapter's body text.</summary>
    public const string SearchWord = "gizmoword";

    /// <summary>
    /// A book with <paramref name="spineCount"/> chapters (ch000..chNNN). The nav
    /// document lists only every 10th chapter; chapter index 5 (when present) is given a
    /// title containing characters that must survive HTML-attribute re-escaping when the
    /// continuous document is generated. Every chapter body contains
    /// <see cref="SearchWord"/> exactly once, and every un-listed chapter has no nav
    /// entry at all (so it must fall back to a numbered section title).
    /// </summary>
    public static void BuildLargeSpineEpub(string destPath, int spineCount)
    {
        using var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        for (int i = 0; i < spineCount; i++)
        {
            var id = $"ch{i:000}";
            manifest.AppendLine($"<item id=\"{id}\" href=\"{id}.xhtml\" media-type=\"application/xhtml+xml\"/>");
            spine.AppendLine($"<itemref idref=\"{id}\"/>");
            WriteEntry(zip, $"OEBPS/{id}.xhtml", $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                <head><title>{id}</title></head>
                <body><h1>{id}</h1><p>Filler paragraph {i} with the {SearchWord} term inside it.</p></body>
                </html>
                """);
        }

        manifest.AppendLine("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");

        // Every 10th chapter is listed, plus chapter 5 specifically (it does not fall on
        // a multiple of 10) so its escaping-exercise title is actually included.
        var listedIndices = new SortedSet<int>();
        for (int i = 0; i < spineCount; i += 10) listedIndices.Add(i);
        if (spineCount > 5) listedIndices.Add(5);

        var navItems = new StringBuilder();
        foreach (var i in listedIndices)
        {
            var id = $"ch{i:000}";
            var title = i == 5
                ? "Chapter Five &lt;5&gt; &amp; &quot;quotes&quot;"
                : $"Nav Title {i}";
            navItems.AppendLine($"<li><a href=\"{id}.xhtml\">{title}</a></li>");
        }

        WriteEntry(zip, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Nav</title></head>
            <body>
              <nav epub:type="toc"><ol>
            {navItems}
              </ol></nav>
            </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0" xml:lang="en">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">urn:uuid:elr-large-fixture</dc:identifier>
                <dc:title>Large Fixture</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
            {manifest}
              </manifest>
              <spine>
            {spine}
              </spine>
            </package>
            """);
    }

    /// <summary>
    /// A minimal book whose manifest declares an item at href "../escape.txt" - one
    /// directory level above the OPF's own OEBPS/ directory - to exercise how extraction
    /// handles a hostile or malformed manifest path. The corresponding zip entry lives at
    /// the zip root ("escape.txt"), i.e. where "../escape.txt" resolves to relative to
    /// OEBPS/content.opf.
    /// </summary>
    public static void BuildMaliciousEpub(string destPath)
    {
        using var stream = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/chapter1.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head><title>Chapter One</title></head>
            <body><p>Only chapter.</p></body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/nav.xhtml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Nav</title></head>
            <body><nav epub:type="toc"><ol><li><a href="chapter1.xhtml">Chapter One</a></li></ol></nav></body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/content.opf", """
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0" xml:lang="en">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">urn:uuid:elr-malicious-fixture</dc:identifier>
                <dc:title>Malicious Fixture</dc:title>
                <dc:language>en</dc:language>
              </metadata>
              <manifest>
                <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="c1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
                <item id="escape" href="../escape.txt" media-type="text/plain"/>
              </manifest>
              <spine>
                <itemref idref="c1"/>
              </spine>
            </package>
            """);

        // What the hostile manifest item resolves to, one level "above" OEBPS.
        WriteEntry(zip, "escape.txt", "if you can read this outside the extract root, extraction is unsafe");
    }

    private static void WriteEntry(ZipArchive zip, string name, string content,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
