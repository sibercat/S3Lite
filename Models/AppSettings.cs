namespace S3Lite.Models;

public class AppSettings
{
    public int    MaxConcurrentUploads    { get; set; } = 3;
    public int    MaxConcurrentDownloads  { get; set; } = 3;
    public int    ParallelPartsPerUpload  { get; set; } = 4;
    public int    MultipartThresholdMB    { get; set; } = 16;  // files >= this size use multipart
    public string Theme                   { get; set; } = "Light"; // "Light" or "Dark"
    public bool   ShowTrayIcon            { get; set; } = true;
    public bool   MinimizeToTray          { get; set; } = false;
    public bool   EnablePagination        { get; set; } = false;
    public int    PageSize                { get; set; } = 1000;
    public bool   LimitPreviewSize        { get; set; } = true;
    public int    PreviewMaxSizeMB        { get; set; } = 10;

    /// <summary>Externally-added buckets: bucket name → AWS region.</summary>
    public Dictionary<string, string> ExternalBuckets { get; set; } = new();

    /// <summary>Profile name to auto-connect on startup. Null = don't auto-connect.</summary>
    public string? LastProfile { get; set; }
}
