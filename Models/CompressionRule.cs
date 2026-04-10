namespace S3Lite.Models;

public class CompressionRule
{
    /// <summary>Bucket name or wildcard mask. "*" matches all buckets.</summary>
    public string BucketMask { get; set; } = "*";

    /// <summary>File key or wildcard mask. "*" matches all files. e.g. "*.html", "assets/*"</summary>
    public string FileMask { get; set; } = "*";

    /// <summary>GZip compression level 1–9. 1=fastest, 9=best compression. Default 6.</summary>
    public int Level { get; set; } = 6;

    public bool Enabled { get; set; } = true;
}
