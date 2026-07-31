using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EpubLiteReader;

public sealed class BookmarkEntry
{
    public int SpineIndex { get; set; }
    public double ScrollFraction { get; set; }
    public string? Anchor { get; set; }
    public string Label { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public override string ToString() => Label;
}

public sealed class BookState
{
    public string BookId { get; set; } = "";
    public string? FilePath { get; set; }
    public int SpineIndex { get; set; }
    public double ScrollFraction { get; set; }
    public DisplaySettings Display { get; set; } = new();
    public List<BookmarkEntry> Bookmarks { get; set; } = new();
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public DisplaySettings Defaults { get; set; } = new();
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
}

/// <summary>JSON persistence under %LocalAppData%\GreenYogaInc\EpubLiteReader\</summary>
public static class BookStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GreenYogaInc", "EpubLiteReader");

    private static string BooksDir => Path.Combine(RootDir, "books");
    private static string SettingsPath => Path.Combine(RootDir, "settings.json");

    public static AppSettings LoadAppSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        return new AppSettings();
    }

    public static void SaveAppSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    public static BookState? LoadBook(string bookId)
    {
        try
        {
            var path = BookPath(bookId);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<BookState>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return null;
        }
    }

    public static void SaveBook(BookState state)
    {
        try
        {
            Directory.CreateDirectory(BooksDir);
            state.UpdatedUtc = DateTime.UtcNow;
            File.WriteAllText(BookPath(state.BookId), JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    private static string BookPath(string bookId) =>
        Path.Combine(BooksDir, bookId + ".json");
}
