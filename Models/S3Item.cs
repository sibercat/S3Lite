namespace S3Lite.Models;

public enum S3ItemType { Folder, File }

public class S3Item
{
    public S3ItemType Type { get; set; }
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }

    public string StorageClass { get; set; } = "";

    public string DisplaySize => Type == S3ItemType.Folder
        ? ""
        : Size >= 1_048_576 ? $"{Size / 1_048_576.0:F1} MB"
        : Size >= 1024 ? $"{Size / 1024.0:F1} KB"
        : $"{Size} B";
}
