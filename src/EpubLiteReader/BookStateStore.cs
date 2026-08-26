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

    /// <summary>Test hook: redirects all persistence to a throwaway directory.</summary>
    internal static string? RootOverride;

    public static string RootDir =>
        RootOverride ??
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
            WriteAtomic(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
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
            state.UpdatedUtc = DateTime.UtcNow;
            WriteAtomic(BookPath(state.BookId), JsonSerializer.Serialize(state, JsonOptions));
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    private static string BookPath(string bookId) =>
        Path.Combine(BooksDir, bookId + ".json");

    /// <summary>
    /// Writes via a same-directory temp file followed by an atomic replace, so an
    /// interrupted write can never truncate the only copy of the target file.
    /// </summary>
    internal static void WriteAtomic(string path, string content)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        CleanStaleTempFiles(dir);

        var tmp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best effort */ }
            throw;
        }
    }

    /// <summary>Removes orphaned temp files left behind by writes that died mid-flight.</summary>
    private static void CleanStaleTempFiles(string dir)
    {
        try
        {
            foreach (var tmp in Directory.EnumerateFiles(dir, "*.json.*.tmp"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(tmp) > TimeSpan.FromMinutes(10))
                        File.Delete(tmp);
                }
                catch { /* another writer may hold it; skip */ }
            }
        }
        catch { /* directory enumeration is best effort */ }
    }
}
