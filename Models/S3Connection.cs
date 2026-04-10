namespace S3Lite.Models;

public class S3Connection
{
    public string ProfileName    { get; set; } = "Default";
    public string AccessKey      { get; set; } = "";
    public string SecretKey      { get; set; } = "";
    public string Region         { get; set; } = "us-east-1";
    public string? EndpointUrl   { get; set; }
    public bool ForcePathStyle   { get; set; } = false;

    /// <summary>"AccessKey" | "EnvVars" | "AwsProfile"</summary>
    public string CredentialType  { get; set; } = "AccessKey";
    /// <summary>AWS shared credentials file profile name (used when CredentialType = "AwsProfile").</summary>
    public string AwsProfileName  { get; set; } = "default";
    public bool UseDualStack      { get; set; } = true;
    public bool UseAcceleration   { get; set; } = false;
}
