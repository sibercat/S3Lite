namespace S3Lite.Models;

public class BucketProperties
{
    public string    Name                        { get; set; } = "";
    public string    OwnerId                     { get; set; } = "";
    public DateTime? CreationDate                { get; set; }
    public string    Region                      { get; set; } = "";
    public long      TotalObjects                { get; set; }
    public long      TotalFiles                  { get; set; }
    public long      TotalFolders                { get; set; }
    public long      TotalSize                   { get; set; }
    public int       UncompletedMultipartUploads { get; set; }
    public string    Versioning                  { get; set; } = "";
    public string    Logging                     { get; set; } = "";
    public string    ObjectLock                  { get; set; } = "";
    public string    Replication                 { get; set; } = "";
    public string    TransferAcceleration        { get; set; } = "";
    public string    Encryption                  { get; set; } = "";
    public string    RequesterPays               { get; set; } = "";
    public List<string> FileTypes               { get; set; } = new();
    public List<string> StorageClasses          { get; set; } = new();
    public DateTime? ModifiedFrom               { get; set; }
    public DateTime? ModifiedTo                 { get; set; }
}
