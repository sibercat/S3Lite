using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using S3Lite.Models;

namespace S3Lite.Services;

public static class ProfileStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "S3Lite", "profiles.json");

    // Secret keys are stored DPAPI-encrypted (per Windows user). Legacy plaintext
    // entries load as-is and are encrypted on the next save.
    private const string EncPrefix = "dpapi:";

    private static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain) || plain.StartsWith(EncPrefix)) return plain;
        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return EncPrefix + Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(EncPrefix)) return stored;
        try
        {
            var bytes = Convert.FromBase64String(stored[EncPrefix.Length..]);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch { return ""; } // different Windows user/machine — key unrecoverable
    }

    public static List<S3Connection> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var json = File.ReadAllText(FilePath);
            var profiles = JsonSerializer.Deserialize<List<S3Connection>>(json) ?? [];
            foreach (var p in profiles)
                p.SecretKey = Unprotect(p.SecretKey);
            return profiles;
        }
        catch { return []; }
    }

    public static void Save(List<S3Connection> profiles)
    {
        try
        {
            // Serialize a copy so in-memory profiles keep their usable plaintext keys
            var toWrite = profiles.Select(p => new S3Connection
            {
                ProfileName     = p.ProfileName,
                AccessKey       = p.AccessKey,
                SecretKey       = Protect(p.SecretKey),
                Region          = p.Region,
                EndpointUrl     = p.EndpointUrl,
                ForcePathStyle  = p.ForcePathStyle,
                CredentialType  = p.CredentialType,
                AwsProfileName  = p.AwsProfileName,
                UseDualStack    = p.UseDualStack,
                UseAcceleration = p.UseAcceleration,
            }).ToList();

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(toWrite, new JsonSerializerOptions { WriteIndented = true }));
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
