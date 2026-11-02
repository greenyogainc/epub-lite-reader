using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EpubLiteReader.UiTests;

/// <summary>
/// A throwaway EPUB 3 for the window tests, with chapter bodies, head markup and the navigation list
/// chosen by the test. Spine order is the order of <see cref="Chapters"/>.
/// </summary>
internal sealed class TestBook
{
    public string Title { get; set; } = "Test Book";

    public List<(string File, string Head, string Body)> Chapters { get; } = new();

    /// <summary>The inside of the navigation document's &lt;ol&gt;.</summary>
    public string NavItems { get; set; } = "";

    public List<(string Href, string MediaType, byte[] Data)> Extras { get; } = new();

    public TestBook Chapter(string file, string body, string head = "")
    {
        Chapters.Add((file, head, body));
        return this;
    }

    // ---------- Fixtures ----------

    /// <summary>Words the tests search for. <see cref="SharedWord"/> is in part1, part2 and part3 (spines 1, 3
    /// and 4), so Next and Previous lead to different chapters from the middle one.</summary>
    public const string IntroWord = "zanzibarintro";
    public const string SharedWord = "quokka";
    public const string RightPaneWord = "wombat";
    public const string HiddenWord = "hiddenword";

    /// <summary>
    /// Five chapters: intro (spine 0, in no navigation entry), part1 (long, spine 1), part1b (2), part2 (3)
    /// and part3 (4). Facing groups are [0], [1,2], [3,4]. The navigation list has a header with no target
    /// holding part1, which holds part1b#sec; then part2; then part1#again (the same spine as part1, to
    /// exercise the ordering tie-breaks); then an entry with a blank title.
    /// </summary>
    public static TestBook Rich(string title)
    {
        var book = new TestBook
        {
            Title = title,
            NavItems =
                "<li><span>Header</span><ol>" +
                "<li><a href=\"part1.xhtml\">Part One</a><ol><li><a href=\"part1b.xhtml#sec\">Section</a></li></ol></li>" +
                "</ol></li>" +
                "<li><a href=\"part2.xhtml\">Part Two</a></li>" +
                "<li><a href=\"part1.xhtml#again\">Again</a></li>" +
                "<li><a href=\"part3.xhtml\"> </a></li>"
        };
        book.Chapter("intro.xhtml",
            $"<h1>Introduction</h1><p>{IntroWord} opens the book.</p><p>A short first page.</p>");

        var long1 = new StringBuilder("<h1>Part One</h1>");
        for (var i = 1; i <= 160; i++)
        {
            long1.Append($"<p>Paragraph {i} of part one carries enough ordinary words to fill a line or two of the reading pane.");
            if (i == 3)
                long1.Append($" The {SharedWord} lives here.");
            if (i == 80)
                long1.Append("</p><h2 id=\"again\">Again</h2><p>The anchored middle of part one.");
            long1.Append("</p>");
        }

        book.Chapter("part1.xhtml", long1.ToString());
        book.Chapter("part1b.xhtml", $"<h2 id=\"sec\">Section</h2><p>The {RightPaneWord} appears only here.</p>");
        var long2 = new StringBuilder($"<h1>Part Two</h1><p>The {SharedWord} returns in part two.</p>");
        for (var i = 1; i <= 40; i++)
            long2.Append($"<p>Second part paragraph {i} is plain filler for the page.</p>");
        book.Chapter("part2.xhtml", long2.ToString());
        var long3 = new StringBuilder(
            $"<h1>Part Three</h1><p>The last chapter. Visible text <span style=\"display:none\">{HiddenWord}</span> ends it. A {SharedWord} waves goodbye.</p>");
        for (var i = 1; i <= 60; i++)
            long3.Append($"<p>Closing paragraph {i} keeps the last chapter longer than one page.</p>");
        book.Chapter("part3.xhtml", long3.ToString());
        return book;
    }

    /// <summary>Two chapters and a navigation list with no entries.</summary>
    public static TestBook EmptyToc(string title) => new TestBook { Title = title, NavItems = "" }
        .Chapter("a.xhtml", "<p>First.</p>")
        .Chapter("b.xhtml", "<p>Second.</p>");

    /// <summary>Two manifest items whose names differ only in case, so the second cannot be extracted.</summary>
    public static TestBook Colliding(string title)
    {
        var book = new TestBook { Title = title, NavItems = "<li><a href=\"a.xhtml\">One</a></li>" }
            .Chapter("a.xhtml", "<p>First.</p>");
        book.Extras.Add(("Dup.css", "text/css", Encoding.ASCII.GetBytes("p { color: red; }")));
        book.Extras.Add(("dup.css", "text/css", Encoding.ASCII.GetBytes("p { color: blue; }")));
        return book;
    }

    /// <summary>A book with an empty navigation list and nothing in its spine.</summary>
    public static TestBook EmptySpine(string title) => new() { Title = title };

    /// <summary>
    /// One chapter using the features a normal book relies on: a stylesheet and a font-face, an inline
    /// style, a bundled image, SVG image and use, an object, a remote image and audio.
    /// </summary>
    public static TestBook Media(string title)
    {
        var book = new TestBook { Title = title, NavItems = "<li><a href=\"media.xhtml\">Media</a></li>" };
        book.Chapter("media.xhtml",
            "<p id=\"styled\" class=\"styled\">Styled by the stylesheet.</p>" +
            "<p id=\"fonted\" class=\"fonted\">Set in the bundled font.</p>" +
            "<p id=\"inline\" style=\"text-indent: 13px\">Styled inline.</p>" +
            "<img id=\"img\" src=\"pic.png\" alt=\"bundled\"/>" +
            "<img id=\"remote\" src=\"https://probe.invalid/x.png\" alt=\"remote\"/>" +
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" id=\"svg\" width=\"60\" height=\"30\">" +
            "<defs><rect id=\"box\" width=\"10\" height=\"10\" fill=\"red\"/></defs>" +
            "<use id=\"use\" xlink:href=\"#box\"/>" +
            "<image id=\"svgimg\" x=\"20\" width=\"10\" height=\"10\" xlink:href=\"svgpic.png\"/></svg>" +
            "<object id=\"obj\" data=\"objpic.png\" type=\"image/png\"></object>" +
            "<audio id=\"aud\" src=\"tone.wav\" preload=\"auto\"></audio>",
            "<link rel=\"stylesheet\" type=\"text/css\" href=\"book.css\"/>");
        book.Extras.Add(("book.css", "text/css", Encoding.ASCII.GetBytes(
            "@font-face { font-family: 'ElrTest'; src: url('font.ttf'); }\n" +
            "p.styled { border-left: 7px solid rgb(1, 2, 3); }\n" +
            "p.fonted { font-family: 'ElrTest', serif; }\n")));
        book.Extras.Add(("font.ttf", "font/ttf", File.ReadAllBytes(SystemFont())));
        var png = Png();
        book.Extras.Add(("pic.png", "image/png", png));
        book.Extras.Add(("svgpic.png", "image/png", png));
        book.Extras.Add(("objpic.png", "image/png", png));
        book.Extras.Add(("tone.wav", "audio/wav", Wav(seconds: 2)));
        return book;
    }

    private static string SystemFont()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var name in new[] { "arial.ttf", "segoeui.ttf", "calibri.ttf", "tahoma.ttf" })
        {
            var path = Path.Combine(fonts, name);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("No TrueType font found in " + fonts);
    }

    private static byte[] Png()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 200;
            pixels[i + 3] = 255;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>A silent 8-bit mono PCM WAV, long enough to seek in.</summary>
    private static byte[] Wav(int seconds)
    {
        const int rate = 8000;
        var data = new byte[rate * seconds];
        Array.Fill(data, (byte)128);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + data.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(data.Length);
        writer.Write(data);
        writer.Flush();
        return stream.ToArray();
    }

    // ---------- Writing ----------

    /// <summary>Opens a window on this book. The source file is written to a temp folder that is gone
    /// again once the session has copied it into its own data folder.</summary>
    public StaReadingSession Open(Action<MainWindow>? beforeShow = null, Action<string>? beforeOpen = null,
        string? reuseDataDir = null, params string[] extraArgs)
    {
        var path = SaveTemp();
        try
        {
            return StaReadingSession.Open(path, beforeShow, beforeOpen, reuseDataDir, extraArgs);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    /// <summary>Writes the book to <paramref name="path"/> and returns the path.</summary>
    public string Save(string path)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteText(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
        WriteText(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
            </container>
            """);

        var manifest = new StringBuilder("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        var spine = new StringBuilder();
        for (var i = 0; i < Chapters.Count; i++)
        {
            var (file, head, body) = Chapters[i];
            manifest.AppendLine($"<item id=\"c{i}\" href=\"{file}\" media-type=\"application/xhtml+xml\"/>");
            spine.AppendLine($"<itemref idref=\"c{i}\"/>");
            WriteText(zip, "OEBPS/" + file,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Chapter " + i + "</title>" + head + "</head>" +
                "<body>" + body + "</body></html>");
        }

        for (var i = 0; i < Extras.Count; i++)
        {
            var (href, mediaType, data) = Extras[i];
            manifest.AppendLine($"<item id=\"x{i}\" href=\"{href}\" media-type=\"{mediaType}\"/>");
            using var entry = zip.CreateEntry("OEBPS/" + href, CompressionLevel.Optimal).Open();
            entry.Write(data, 0, data.Length);
        }

        WriteText(zip, "OEBPS/nav.xhtml", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Nav</title></head>
            <body><nav epub:type="toc"><ol>{NavItems}</ol></nav></body>
            </html>
            """);

        WriteText(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0" xml:lang="en">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="BookId">urn:uuid:elr-ui-fixture</dc:identifier>
                <dc:language>en</dc:language>
                <dc:title>{Title}</dc:title>
                <dc:creator>Test Author</dc:creator>
              </metadata>
              <manifest>{manifest}</manifest>
              <spine>{spine}</spine>
            </package>
            """);
        return path;
    }

    /// <summary>Writes the book under a new unique name in a fresh temp folder; the caller deletes the folder.</summary>
    public string SaveTemp(string name = "book.epub")
    {
        var dir = Path.Combine(Path.GetTempPath(), "elr-ui-book-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Save(Path.Combine(dir, name));
    }

    private static void WriteText(ZipArchive zip, string name, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
