namespace S3Mount.Models;

public class S3ProviderTemplate
{
    public string Name { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool ForcePathStyle { get; set; }
    public string Description { get; set; } = string.Empty;
    
    public static List<S3ProviderTemplate> GetDefaultTemplates()
    {
        return
        [
            new S3ProviderTemplate
            {
                Name = "AWS S3",
                ServiceUrl = "https://s3.amazonaws.com",
                Region = "us-east-1",
                ForcePathStyle = false,
                Description = "Amazon Web Services S3"
            },
            new S3ProviderTemplate
            {
                Name = "Backblaze B2",
                ServiceUrl = "https://s3.us-west-004.backblazeb2.com",
                Region = "us-west-004",
                ForcePathStyle = true,
                Description = "Backblaze B2 Cloud Storage"
            },
            new S3ProviderTemplate
            {
                Name = "Google Cloud Storage",
                ServiceUrl = "https://storage.googleapis.com",
                Region = "auto",
                ForcePathStyle = false,
                Description = "Google Cloud Storage"
            },
            new S3ProviderTemplate
            {
                Name = "Wasabi",
                ServiceUrl = "https://s3.wasabisys.com",
                Region = "us-east-1",
                ForcePathStyle = false,
                Description = "Wasabi Hot Cloud Storage"
            },
            new S3ProviderTemplate
            {
                Name = "DigitalOcean Spaces",
                ServiceUrl = "https://nyc3.digitaloceanspaces.com",
                Region = "nyc3",
                ForcePathStyle = false,
                Description = "DigitalOcean Spaces"
            },
            new S3ProviderTemplate
            {
                Name = "MinIO",
                ServiceUrl = "http://localhost:9000",
                Region = "us-east-1",
                ForcePathStyle = true,
                Description = "MinIO Self-hosted S3"
            },
            new S3ProviderTemplate
            {
                Name = "Custom",
                ServiceUrl = "",
                Region = "us-east-1",
                ForcePathStyle = false,
                Description = "Custom S3 Compatible Provider"
            }
        ];
    }
}
