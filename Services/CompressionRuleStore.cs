using System.Text.Json;
using System.Text.RegularExpressions;
using S3Lite.Models;

namespace S3Lite.Services;

public static class CompressionRuleStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "S3Lite", "compression_rules.json");

    public static List<CompressionRule> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<CompressionRule>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(List<CompressionRule> rules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(rules,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Returns the first enabled rule that matches the given bucket and file key, or null.</summary>
    public static CompressionRule? FindMatch(List<CompressionRule> rules, string bucket, string key)
    {
        foreach (var rule in rules)
        {
            if (!rule.Enabled) continue;
            if (!WildcardMatch(rule.BucketMask, bucket)) continue;
            // Match against full key and also just the filename
            if (WildcardMatch(rule.FileMask, key) ||
                WildcardMatch(rule.FileMask, Path.GetFileName(key)))
                return rule;
        }
        return null;
    }

    private static bool WildcardMatch(string pattern, string input)
    {
        if (pattern == "*" || string.IsNullOrEmpty(pattern)) return true;
        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
