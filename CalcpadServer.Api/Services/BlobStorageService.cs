using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using CalcpadServer.Api.Models;

namespace CalcpadServer.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null);
    Task<Stream> DownloadFileAsync(string fileName, UserContext userContext);
    Task<Stream> DownloadFileVersionAsync(string fileName, string versionId, UserContext userContext);
    Task<bool> DeleteFileAsync(string fileName, UserContext userContext);
    Task<IEnumerable<string>> ListFilesAsync(UserContext userContext);
    Task<IEnumerable<BlobMetadata>> ListFilesWithMetadataAsync(UserContext userContext);
    Task<IEnumerable<FileVersion>> ListFileVersionsAsync(string fileName, UserContext userContext);
    Task<bool> FileExistsAsync(string fileName, UserContext userContext);
    Task<BlobMetadata> GetFileMetadataAsync(string fileName, UserContext userContext);
    Task<bool> SetFileTagsAsync(string fileName, Dictionary<string, string> tags, UserContext userContext);
    Task<Dictionary<string, string>> GetFileTagsAsync(string fileName, UserContext userContext);
    Task<bool> DeleteFileTagsAsync(string fileName, UserContext userContext);
}

public class BlobStorageService(IMinioClient minioClient, IConfiguration configuration, ILogger<BlobStorageService> logger, IUserService userService) : IBlobStorageService
{
    private readonly IMinioClient _minioClient = minioClient;
    private readonly string _bucketName = configuration["MinIO:BucketName"] ?? "calcpad-storage";
    private readonly string _workingBucketName = GetWorkingBucketName(configuration["MinIO:BucketName"] ?? "calcpad-storage");
    private readonly string _stableBucketName = GetStableBucketName(configuration["MinIO:BucketName"] ?? "calcpad-storage");
    private readonly ILogger<BlobStorageService> _logger = logger;
    private readonly IUserService _userService = userService;

    private static string GetWorkingBucketName(string baseBucketName)
    {
        var bucketPrefix = baseBucketName.EndsWith("-storage") ? baseBucketName.Substring(0, baseBucketName.Length - 8) : baseBucketName;
        return $"{bucketPrefix}-storage-working";
    }

    private static string GetStableBucketName(string baseBucketName)
    {
        var bucketPrefix = baseBucketName.EndsWith("-storage") ? baseBucketName.Substring(0, baseBucketName.Length - 8) : baseBucketName;
        return $"{bucketPrefix}-storage-stable";
    }

    public async Task<string> UploadFileAsync(string FileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null)
    {
        try
        {
            // Determine target bucket based on file category
            var targetBucket = _stableBucketName; // Default to stable bucket
            if (metadata?.FileCategory?.ToLower() == "working")
            {
                targetBucket = _workingBucketName;
            }
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(targetBucket)
                .WithObject(FileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            // Add structured metadata
            var headers = new Dictionary<string, string>();
            
            // Get user's email for CreatedBy field
            var user = await _userService.GetUserByIdAsync(userContext.UserId);
            var userEmail = user?.Email ?? userContext.Username; // Fallback to username if email not found
            
            // Auto-assign current date and user for created/updated fields
            var currentDate = DateTime.UtcNow;
            headers["x-amz-meta-date-created"] = currentDate.ToString("O");
            headers["x-amz-meta-date-updated"] = currentDate.ToString("O");
            headers["x-amz-meta-created-by"] = userEmail;
            headers["x-amz-meta-updated-by"] = userEmail;
            
            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.FileCategory))
                    headers["x-amz-meta-file-category"] = metadata.FileCategory;
                if (metadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = metadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = metadata.ReviewedBy;
                if (!string.IsNullOrEmpty(metadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = metadata.TestedBy;
                if (metadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = metadata.DateTested.Value.ToString("O");
            }
            
            headers["x-amz-meta-created-by-user-id"] = userContext.UserId;
            headers["x-amz-meta-created-by-username"] = userContext.Username;

            if (headers.Count != 0)
            {
                _logger.LogInformation("Setting headers for upload: {Headers}", string.Join(", ", headers.Select(h => $"{h.Key}={h.Value}")));
                putObjectArgs.WithHeaders(headers);
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            // Set tags if provided
            if (tags != null && tags.Count != 0)
            {
                await SetFileTagsAsync(FileName, tags, userContext);
            }
            
            _logger.LogInformation("File {FileName} uploaded successfully to bucket {BucketName}", FileName, targetBucket);
            return FileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {FileName}", FileName);
            throw;
        }
    }

    public async Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType = "application/octet-stream")
    {
        try
        {
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            await _minioClient.PutObjectAsync(putObjectArgs);
            
            _logger.LogInformation("Simple file {FileName} uploaded successfully", fileName);
            return fileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading simple file {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileAsync(string fileName, UserContext userContext)
    {
        try
        {
            // Determine bucket based on file category from metadata
            var targetBucket = await DetermineBucketForFile(fileName);
            
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(targetBucket)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;
            
            _logger.LogInformation("File {FileName} downloaded successfully from bucket {Bucket}", fileName, targetBucket);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName}", fileName);
            throw;
        }
    }

    public async Task<Stream> DownloadFileVersionAsync(string fileName, string versionId, UserContext userContext)
    {
        try
        {
            // Determine bucket based on file category from metadata
            var targetBucket = await DetermineBucketForFile(fileName);
            
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(targetBucket)
                .WithObject(fileName)
                .WithVersionId(versionId)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;
            
            _logger.LogInformation("File {FileName} version {VersionId} downloaded successfully from bucket {Bucket}", fileName, versionId, targetBucket);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName} version {VersionId}", fileName, versionId);
            throw;
        }
    }

    public async Task<IEnumerable<FileVersion>> ListFileVersionsAsync(string fileName, UserContext userContext)
    {
        try
        {
            // Determine bucket based on file category from metadata
            var targetBucket = await DetermineBucketForFile(fileName);
            
            var versions = new List<FileVersion>();
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(targetBucket)
                .WithPrefix(fileName)
                .WithVersions(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                if (item.Key == fileName) // Exact match only
                {
                    versions.Add(new FileVersion
                    {
                        VersionId = item.VersionId ?? "null",
                        LastModified = item.LastModifiedDateTime ?? DateTime.MinValue,
                        Size = (long)item.Size,
                        IsLatest = item.IsLatest,
                        ETag = item.ETag ?? string.Empty
                    });
                }
            }
            
            // Sort by LastModified descending (newest first)
            versions = versions.OrderByDescending(v => v.LastModified).ToList();
            
            _logger.LogInformation("Found {Count} versions for file {FileName} in bucket {Bucket}", versions.Count, fileName, targetBucket);
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing versions for file {FileName}", fileName);
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string fileName, UserContext userContext)
    {
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            await _minioClient.RemoveObjectAsync(removeObjectArgs);
            
            _logger.LogInformation("File {FileName} deleted successfully", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {FileName}", fileName);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListFilesAsync(UserContext userContext)
    {
        try
        {
            var files = new List<string>();
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                files.Add(item.Key);
            }
            
            _logger.LogInformation("Listed {Count} files from bucket", files.Count);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files");
            throw;
        }
    }

    public async Task<bool> FileExistsAsync(string fileName, UserContext userContext)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            await _minioClient.StatObjectAsync(statObjectArgs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IEnumerable<BlobMetadata>> ListFilesWithMetadataAsync(UserContext userContext)
    {
        try
        {
            var files = new List<BlobMetadata>();
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                var metadata = await GetFileMetadataAsync(item.Key, userContext);
                files.Add(metadata);
            }
            
            _logger.LogInformation("Listed {Count} files with metadata from bucket", files.Count);
            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing files with metadata");
            throw;
        }
    }

    public async Task<BlobMetadata> GetFileMetadataAsync(string fileName, UserContext userContext)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            var objectStat = await _minioClient.StatObjectAsync(statObjectArgs);
            
            _logger.LogInformation("Retrieved metadata for file {FileName}: {Metadata}", fileName, 
                objectStat.MetaData != null ? string.Join(", ", objectStat.MetaData.Select(m => $"{m.Key}={m.Value}")) : "No metadata");
            
            var metadata = new Metadata();
            
            if (objectStat.MetaData != null)
            {
                foreach (var kvp in objectStat.MetaData)
                {
                    
                    if (kvp.Key != null)
                    {
                        _logger.LogInformation("Processing metadata key: {OriginalKey} = {Value}", kvp.Key, kvp.Value);
                        
                        // Parse structured metadata fields
                        switch (kvp.Key.ToLower())
                        {
                            case "date-created":
                                if (DateTime.TryParse(kvp.Value, out var dateCreated))
                                    metadata.DateCreated = dateCreated;
                                break;
                            case "created-by":
                                metadata.CreatedBy = kvp.Value;
                                break;
                            case "updated-by":
                                metadata.UpdatedBy = kvp.Value;
                                break;
                            case "date-reviewed":
                                if (DateTime.TryParse(kvp.Value, out var dateReviewed))
                                    metadata.DateReviewed = dateReviewed;
                                break;
                            case "reviewed-by":
                                metadata.ReviewedBy = kvp.Value;
                                break;
                            case "tested-by":
                                metadata.TestedBy = kvp.Value;
                                break;
                            case "date-tested":
                                if (DateTime.TryParse(kvp.Value, out var dateTested))
                                    metadata.DateTested = dateTested;
                                break;
                            default:
                                // Ignore non-structured fields
                                break;
                        }
                    }
                }
            }

            var tags = await GetFileTagsAsync(fileName, userContext);

            _logger.LogInformation("Final metadata for {FileName}", 
                fileName);

            return new BlobMetadata
            {
                FileName = fileName,
                Size = objectStat.Size,
                LastModified = objectStat.LastModified,
                ContentType = objectStat.ContentType ?? "application/octet-stream",
                ETag = objectStat.ETag ?? string.Empty,
                Tags = tags,
                Metadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metadata for file {FileName}", fileName);
            throw;
        }
    }


    public async Task<bool> SetFileTagsAsync(string fileName, Dictionary<string, string> tags, UserContext userContext)
    {
        try
        {
            var tagging = new Tagging();
            foreach (var kvp in tags)
            {
                tagging.Tags.Add(kvp.Key, kvp.Value);
            }

            var setObjectTagsArgs = new SetObjectTagsArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithTagging(tagging);

            await _minioClient.SetObjectTagsAsync(setObjectTagsArgs);
            
            _logger.LogInformation("Tags set for file {FileName}", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting tags for file {FileName}", fileName);
            return false;
        }
    }

    public async Task<Dictionary<string, string>> GetFileTagsAsync(string fileName, UserContext userContext)
    {
        try
        {
            var getObjectTagsArgs = new GetObjectTagsArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            var tagging = await _minioClient.GetObjectTagsAsync(getObjectTagsArgs);
            
            return tagging.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Error getting tags for file {FileName}: {ErrorMessage}", fileName, ex.Message);
            return new Dictionary<string, string>();
        }
    }

    public async Task<bool> DeleteFileTagsAsync(string fileName, UserContext userContext)
    {
        try
        {
            var removeObjectTagsArgs = new RemoveObjectTagsArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            await _minioClient.RemoveObjectTagsAsync(removeObjectTagsArgs);
            
            _logger.LogInformation("Tags removed for file {FileName}", fileName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tags for file {FileName}", fileName);
            return false;
        }
    }

    private async Task<string> DetermineBucketForFile(string fileName)
    {
        // Check working bucket first
        try
        {
            var workingBucketExistsArgs = new StatObjectArgs()
                .WithBucket(_workingBucketName)
                .WithObject(fileName);
            
            await _minioClient.StatObjectAsync(workingBucketExistsArgs);
            return _workingBucketName;
        }
        catch
        {
            // File not in working bucket, try stable bucket
            try
            {
                var stableBucketExistsArgs = new StatObjectArgs()
                    .WithBucket(_stableBucketName)
                    .WithObject(fileName);
                
                await _minioClient.StatObjectAsync(stableBucketExistsArgs);
                return _stableBucketName;
            }
            catch
            {
                // File not found in either bucket, default to stable
                return _stableBucketName;
            }
        }
    }

}