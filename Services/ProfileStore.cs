using System.Text.Json;
using S3Lite.Models;

namespace S3Lite.Services;

public static class ProfileStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "S3Lite", "profiles.json");

    public static List<S3Connection> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<S3Connection>>(json) ?? [];
        }
        catch { return []; }
    }

    public static void Save(List<S3Connection> profiles)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static void Upsert(S3Connection conn)
    {
        var profiles = Load();
        var existing = profiles.FindIndex(p => p.ProfileName == conn.ProfileName);
        if (existing >= 0) profiles[existing] = conn;
        else profiles.Add(conn);
        Save(profiles);
    }

    public static void Delete(string profileName)
    {
        var profiles = Load();
        profiles.RemoveAll(p => p.ProfileName == profileName);
        Save(profiles);
    }
}
