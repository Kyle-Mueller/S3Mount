namespace S3Mount.Models;

public class S3MountConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string MountName { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string BucketName { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; }
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string IconPath { get; set; } = string.Empty;
    public bool AutoMount { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public DateTime LastModified { get; set; } = DateTime.Now;
    public bool IsMounted { get; set; }
    public string ProviderName { get; set; } = string.Empty;
}
