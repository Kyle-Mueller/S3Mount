using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using S3Mount.Models;
using System.IO;

namespace S3Mount.Services;

public class S3Service : IDisposable
{
    private AmazonS3Client? _client;
    private S3MountConfiguration? _config;
    
    public void Initialize(S3MountConfiguration config)
    {
        _config = config;
        
        var s3Config = new AmazonS3Config
        {
            ServiceURL = config.ServiceUrl,
            ForcePathStyle = config.ForcePathStyle,
            AuthenticationRegion = config.Region
        };
        
        var credentials = new BasicAWSCredentials(config.AccessKey, config.SecretKey);
        _client = new AmazonS3Client(credentials, s3Config);
    }
    
    public async Task<bool> TestConnectionAsync()
    {
        if (_client == null || _config == null)
            return false;
            
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _config.BucketName,
                MaxKeys = 1
            };
            
            await _client.ListObjectsV2Async(request);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<List<S3Object>> ListObjectsAsync(string prefix = "", string? continuationToken = null)
    {
        if (_client == null || _config == null)
            return new List<S3Object>();
            
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _config.BucketName,
                Prefix = prefix,
                ContinuationToken = continuationToken
            };
            
            var response = await _client.ListObjectsV2Async(request);
            return response.S3Objects;
        }
        catch
        {
            return new List<S3Object>();
        }
    }
    
    public async Task<Stream?> GetObjectStreamAsync(string key)
    {
        if (_client == null || _config == null)
            return null;
            
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key
            };
            
            var response = await _client.GetObjectAsync(request);
            return response.ResponseStream;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<Stream?> GetObjectRangeAsync(string key, long start, long end)
    {
        if (_client == null || _config == null)
            return null;
            
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key,
                ByteRange = new ByteRange(start, end)
            };
            
            var response = await _client.GetObjectAsync(request);
            return response.ResponseStream;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<GetObjectMetadataResponse?> GetObjectMetadataAsync(string key)
    {
        if (_client == null || _config == null)
            return null;
            
        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _config.BucketName,
                Key = key
            };
            
            return await _client.GetObjectMetadataAsync(request);
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<bool> PutObjectAsync(string key, Stream dataStream)
    {
        if (_client == null || _config == null)
            return false;
            
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key,
                InputStream = dataStream
            };
            
            await _client.PutObjectAsync(request);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<bool> DeleteObjectAsync(string key)
    {
        if (_client == null || _config == null)
            return false;
            
        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _config.BucketName,
                Key = key
            };
            
            await _client.DeleteObjectAsync(request);
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public void Dispose()
    {
        _client?.Dispose();
    }
}
