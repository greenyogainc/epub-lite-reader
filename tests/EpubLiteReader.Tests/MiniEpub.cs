using System.IO;
using System.IO.Compression;
using System.Text;

namespace EpubLiteReader.Tests;

/// <summary>A configurable throwaway EPUB for tests that need a specific manifest shape
/// (colliding names, nested navigation, metadata variants) rather than one of the fixed
/// books in <see cref="EpubFixtureBuilder"/>.</summary>
internal sealed class MiniEpub
{
    /// <summary>dc:title content; null omits the element entirely.</summary>
    public string? Title { get; set; } = "Mini Book";

    /// <summary>dc:creator content; null omits the element.</summary>
    public string? Author { get; set; }

    /// <summary>dc:description content; null omits the element.</summary>
    public string? Description { get; set; }

    /// <summary>Chapter file names under OEBPS/, in spine order.</summary>
    public List<string> Chapters { get; } = new() { "c1.xhtml", "c2.xhtml" };

    /// <summary>The inside of the nav document's &lt;ol&gt;. Defaults to listing the first chapter only.</summary>
    public string NavItems { get; set; } = "<li><a href=\"c1.xhtml\">One</a></li>";

    /// <summary>Extra manifest items (and their zip entries), after the chapters, in order.</summary>
    public List<(string Href, string MediaType, byte[] Data)> Extras { get; } = new();

    public string Save(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Write(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        Write(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        var manifest = new StringBuilder("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        var spine = new StringBuilder();
        for (var i = 0; i < Chapters.Count; i++)
        {
            manifest.AppendLine($"<item id=\"c{i}\" href=\"{Chapters[i]}\" media-type=\"application/xhtml+xml\"/>");
            spine.AppendLine($"<itemref idref=\"c{i}\"/>");
            Write(zip, "OEBPS/" + Chapters[i], $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <html xmlns="http://www.w3.org/1999/xhtml"><head><title>Chapter {i}</title></head>
                <body><p>Chapter {i} text.</p></body></html>
                """);
        }

        for (var i = 0; i < Extras.Count; i++)
        {
            var (href, mediaType, data) = Extras[i];
            manifest.AppendLine($"<item id=\"x{i}\" href=\"{href}\" media-type=\"{mediaType}\"/>");
            var entry = zip.CreateEntry("OEBPS/" + href, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(data, 0, data.Length);
        }

        Write(zip, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Nav</title></head>
            <body><nav epub:type="toc"><ol>{NavItems}</ol></nav></body>
            </html>
            """);

        var metadata = new StringBuilder("<dc:identifier id=\"BookId\">urn:uuid:elr-mini-fixture</dc:identifier>\n<dc:language>en</dc:language>\n");
        if (Title is not null) metadata.AppendLine($"<dc:title>{Title}</dc:title>");
        if (Author is not null) metadata.AppendLine($"<dc:creator>{Author}</dc:creator>");
        if (Description is not null) metadata.AppendLine($"<dc:description>{Description}</dc:description>");

        Write(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0" xml:lang="en">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">{metadata}</metadata>
              <manifest>{manifest}</manifest>
              <spine>{spine}</spine>
            </package>
            """);
        return path;
    }

    private static void Write(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
