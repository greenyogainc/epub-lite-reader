using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using Xunit;

namespace EpubLiteReader.Tests;

/// <summary>Guards against tools/make_localized_resx.py drifting out of sync with
/// the checked-in Strings*.resx files. Historically the generator carried 58 keys
/// while the resx files carried 77 (including live About/Support strings);
/// re-running it would have silently dropped ~20 keys per locale.</summary>
public sealed class LocalizationDriftTests
{
    [Fact]
    public void GeneratorOrderMatchesBaseResxKeys()
    {
        var repoRoot = TestPaths.RepoRoot;
        var scriptPath = Path.Combine(repoRoot, "tools", "make_localized_resx.py");
        var baseResxPath = Path.Combine(repoRoot, "src", "EpubLiteReader", "Strings.resx");

        var scriptText = File.ReadAllText(scriptPath);
        var generatorKeys = ExtractOrderKeys(scriptText);
        var resxKeys = ExtractResxKeys(baseResxPath);

        var missing = resxKeys.Except(generatorKeys).ToHashSet();
        var extra = generatorKeys.Except(resxKeys).ToHashSet();

        Assert.True(
            missing.Count == 0 && extra.Count == 0,
            $"Generator ORDER drift vs {Path.GetFileName(baseResxPath)}: "
            + (missing.Count > 0 ? $"missing-from-generator=[{string.Join(", ", missing.OrderBy(k => k))}] " : "")
            + (extra.Count > 0 ? $"extra-in-generator=[{string.Join(", ", extra.OrderBy(k => k))}]" : ""));
    }

    [Fact]
    public void AllSatelliteResxShareBaseKeySet()
    {
        var repoRoot = TestPaths.RepoRoot;
        var resxDir = Path.Combine(repoRoot, "src", "EpubLiteReader");
        var baseKeys = ExtractResxKeys(Path.Combine(resxDir, "Strings.resx"));

        var satellites = Directory
            .EnumerateFiles(resxDir, "Strings.*.resx")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(13, satellites.Length);

        foreach (var path in satellites)
        {
            var keys = ExtractResxKeys(path);
            var missing = baseKeys.Except(keys).ToHashSet();
            var extra = keys.Except(baseKeys).ToHashSet();
            Assert.True(
                missing.Count == 0 && extra.Count == 0,
                $"{Path.GetFileName(path)} key set differs from base Strings.resx: "
                + (missing.Count > 0 ? $"missing=[{string.Join(", ", missing.OrderBy(k => k))}] " : "")
                + (extra.Count > 0 ? $"extra=[{string.Join(", ", extra.OrderBy(k => k))}]" : ""));
        }
    }

    private static HashSet<string> ExtractResxKeys(string resxPath)
    {
        var doc = new XmlDocument();
        doc.Load(resxPath);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (XmlNode node in doc.DocumentElement!.SelectNodes("data")!)
        {
            var nameAttr = node.Attributes?["name"];
            if (nameAttr is not null)
            {
                names.Add(nameAttr.Value);
            }
        }
        return names;
    }

    private static HashSet<string> ExtractOrderKeys(string scriptText)
    {
        const string marker = "ORDER = [";
        var start = scriptText.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not locate ORDER = [ in make_localized_resx.py");
        start += marker.Length;

        var depth = 1;
        var end = start;
        while (end < scriptText.Length && depth > 0)
        {
            var c = scriptText[end];
            if (c == '[') depth++;
            else if (c == ']') depth--;
            end++;
        }
        Assert.True(depth == 0, "Could not locate matching ']' for ORDER = [");

        var body = scriptText[start..(end - 1)];
        var matches = Regex.Matches(body, "\"([^\"\n]+)\"");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in matches)
        {
            keys.Add(m.Groups[1].Value);
        }
        return keys;
    }
}