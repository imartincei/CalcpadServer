using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using CalcpadServer.Api.Models;

namespace CalcpadServer.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? metadata = null, Dictionary<string, string>? tags = null, StructuredMetadataRequest? structuredMetadata = null);
    Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType = "application/octet-stream");
    Task<string> CreateNewVersionAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? metadata = null, Dictionary<string, string>? tags = null, StructuredMetadataRequest? structuredMetadata = null);
    Task<Stream> DownloadFileAsync(string fileName, UserContext userContext);
    Task<Stream> DownloadLatestVersionAsync(string baseFileName, UserContext userContext);
    Task<bool> DeleteFileAsync(string fileName, UserContext userContext);
    Task<bool> DeleteAllVersionsAsync(string baseFileName, UserContext userContext);
    Task<IEnumerable<string>> ListFilesAsync(UserContext userContext);
    Task<IEnumerable<BlobMetadata>> ListFilesWithMetadataAsync(UserContext userContext);
    Task<IEnumerable<BlobMetadata>> ListFileVersionsAsync(string baseFileName, UserContext userContext);
    Task<bool> FileExistsAsync(string fileName, UserContext userContext);
    Task<BlobMetadata> GetFileMetadataAsync(string fileName, UserContext userContext);
    Task<BlobMetadata> GetLatestVersionMetadataAsync(string baseFileName, UserContext userContext);
    Task<bool> SetFileTagsAsync(string fileName, Dictionary<string, string> tags, UserContext userContext);
    Task<Dictionary<string, string>> GetFileTagsAsync(string fileName, UserContext userContext);
    Task<bool> DeleteFileTagsAsync(string fileName, UserContext userContext);
    Task<int> GetNextVersionNumberAsync(string baseFileName, UserContext userContext);
    Task<string> GetVersionedFileNameAsync(string baseFileName, int version, UserContext userContext);
}

public class BlobStorageService : IBlobStorageService
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<BlobStorageService> _logger;

    public BlobStorageService(IMinioClient minioClient, IConfiguration configuration, ILogger<BlobStorageService> logger)
    {
        _minioClient = minioClient;
        _bucketName = configuration["MinIO:BucketName"] ?? "calcpad-storage";
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? metadata = null, Dictionary<string, string>? tags = null, StructuredMetadataRequest? structuredMetadata = null)
    {
        try
        {
            await EnsureBucketExistsAsync();
            
            // Create version 1 for new file
            var versionedFileName = await GetVersionedFileNameAsync(baseFileName, 1, userContext);
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(versionedFileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            // Add custom metadata with x-amz-meta- prefix
            var headers = new Dictionary<string, string>();
            
            if (metadata != null && metadata.Any())
            {
                foreach (var kvp in metadata)
                {
                    headers[$"x-amz-meta-{kvp.Key.ToLower()}"] = kvp.Value;
                }
            }

            // Add structured metadata
            if (structuredMetadata != null)
            {
                if (!string.IsNullOrEmpty(structuredMetadata.OriginalFileName))
                    headers["x-amz-meta-original-filename"] = structuredMetadata.OriginalFileName;
                if (structuredMetadata.DateCreated.HasValue)
                    headers["x-amz-meta-date-created"] = structuredMetadata.DateCreated.Value.ToString("O");
                if (structuredMetadata.DateUpdated.HasValue)
                    headers["x-amz-meta-date-updated"] = structuredMetadata.DateUpdated.Value.ToString("O");
                if (!string.IsNullOrEmpty(structuredMetadata.CreatedBy))
                    headers["x-amz-meta-created-by"] = structuredMetadata.CreatedBy;
                if (!string.IsNullOrEmpty(structuredMetadata.UpdatedBy))
                    headers["x-amz-meta-updated-by"] = structuredMetadata.UpdatedBy;
                if (structuredMetadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = structuredMetadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(structuredMetadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = structuredMetadata.ReviewedBy;
                if (!string.IsNullOrEmpty(structuredMetadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = structuredMetadata.TestedBy;
                if (structuredMetadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = structuredMetadata.DateTested.Value.ToString("O");
            }

            // Always set version to 1 for new files
            headers["x-amz-meta-version"] = "1";
            headers["x-amz-meta-base-filename"] = baseFileName;
            headers["x-amz-meta-created-by-user-id"] = userContext.UserId;
            headers["x-amz-meta-created-by-username"] = userContext.Username;

            if (headers.Any())
            {
                putObjectArgs.WithHeaders(headers);
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            // Set tags if provided
            if (tags != null && tags.Any())
            {
                await SetFileTagsAsync(versionedFileName, tags, userContext);
            }
            
            _logger.LogInformation("File {VersionedFileName} uploaded successfully as version 1 of {BaseFileName}", versionedFileName, baseFileName);
            return versionedFileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file {BaseFileName}", baseFileName);
            throw;
        }
    }

    public async Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType = "application/octet-stream")
    {
        try
        {
            await EnsureBucketExistsAsync();
            
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
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream));

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;
            
            _logger.LogInformation("File {FileName} downloaded successfully", fileName);
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file {FileName}", fileName);
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
            
            var customMetadata = new Dictionary<string, string>();
            var structuredMetadata = new StructuredMetadata();
            
            if (objectStat.MetaData != null)
            {
                foreach (var kvp in objectStat.MetaData)
                {
                    if (kvp.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = kvp.Key.Substring("x-amz-meta-".Length);
                        
                        // Parse structured metadata fields
                        switch (key.ToLower())
                        {
                            case "original-filename":
                                structuredMetadata.OriginalFileName = kvp.Value;
                                break;
                            case "date-created":
                                if (DateTime.TryParse(kvp.Value, out var dateCreated))
                                    structuredMetadata.DateCreated = dateCreated;
                                break;
                            case "date-updated":
                                if (DateTime.TryParse(kvp.Value, out var dateUpdated))
                                    structuredMetadata.DateUpdated = dateUpdated;
                                break;
                            case "version":
                                structuredMetadata.Version = kvp.Value;
                                break;
                            case "created-by":
                                structuredMetadata.CreatedBy = kvp.Value;
                                break;
                            case "updated-by":
                                structuredMetadata.UpdatedBy = kvp.Value;
                                break;
                            case "date-reviewed":
                                if (DateTime.TryParse(kvp.Value, out var dateReviewed))
                                    structuredMetadata.DateReviewed = dateReviewed;
                                break;
                            case "reviewed-by":
                                structuredMetadata.ReviewedBy = kvp.Value;
                                break;
                            case "tested-by":
                                structuredMetadata.TestedBy = kvp.Value;
                                break;
                            case "date-tested":
                                if (DateTime.TryParse(kvp.Value, out var dateTested))
                                    structuredMetadata.DateTested = dateTested;
                                break;
                            default:
                                // Add to custom metadata if not a structured field
                                customMetadata[key] = kvp.Value;
                                break;
                        }
                    }
                }
            }

            var tags = await GetFileTagsAsync(fileName, userContext);

            return new BlobMetadata
            {
                FileName = fileName,
                Size = objectStat.Size,
                LastModified = objectStat.LastModified,
                ContentType = objectStat.ContentType ?? "application/octet-stream",
                ETag = objectStat.ETag ?? string.Empty,
                CustomMetadata = customMetadata,
                Tags = tags,
                Structured = structuredMetadata
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
            _logger.LogDebug(ex, "No tags found for file {FileName}", fileName);
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

    public async Task<string> CreateNewVersionAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? metadata = null, Dictionary<string, string>? tags = null, StructuredMetadataRequest? structuredMetadata = null)
    {
        try
        {
            await EnsureBucketExistsAsync();
            
            var nextVersion = await GetNextVersionNumberAsync(baseFileName, userContext);
            var versionedFileName = await GetVersionedFileNameAsync(baseFileName, nextVersion, userContext);
            
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(versionedFileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(contentType);

            var headers = new Dictionary<string, string>();
            
            if (metadata != null && metadata.Any())
            {
                foreach (var kvp in metadata)
                {
                    headers[$"x-amz-meta-{kvp.Key.ToLower()}"] = kvp.Value;
                }
            }

            if (structuredMetadata != null)
            {
                if (!string.IsNullOrEmpty(structuredMetadata.OriginalFileName))
                    headers["x-amz-meta-original-filename"] = structuredMetadata.OriginalFileName;
                if (structuredMetadata.DateCreated.HasValue)
                    headers["x-amz-meta-date-created"] = structuredMetadata.DateCreated.Value.ToString("O");
                if (structuredMetadata.DateUpdated.HasValue)
                    headers["x-amz-meta-date-updated"] = structuredMetadata.DateUpdated.Value.ToString("O");
                if (!string.IsNullOrEmpty(structuredMetadata.CreatedBy))
                    headers["x-amz-meta-created-by"] = structuredMetadata.CreatedBy;
                if (!string.IsNullOrEmpty(structuredMetadata.UpdatedBy))
                    headers["x-amz-meta-updated-by"] = structuredMetadata.UpdatedBy;
                if (structuredMetadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = structuredMetadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(structuredMetadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = structuredMetadata.ReviewedBy;
                if (!string.IsNullOrEmpty(structuredMetadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = structuredMetadata.TestedBy;
                if (structuredMetadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = structuredMetadata.DateTested.Value.ToString("O");
            }

            headers["x-amz-meta-version"] = nextVersion.ToString();
            headers["x-amz-meta-base-filename"] = baseFileName;
            headers["x-amz-meta-version-created-by-user-id"] = userContext.UserId;
            headers["x-amz-meta-version-created-by-username"] = userContext.Username;

            if (headers.Any())
            {
                putObjectArgs.WithHeaders(headers);
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            if (tags != null && tags.Any())
            {
                await SetFileTagsAsync(versionedFileName, tags, userContext);
            }
            
            _logger.LogInformation("File {VersionedFileName} created as version {Version} of {BaseFileName}", versionedFileName, nextVersion, baseFileName);
            return versionedFileName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new version of file {BaseFileName}", baseFileName);
            throw;
        }
    }

    public async Task<Stream> DownloadLatestVersionAsync(string baseFileName, UserContext userContext)
    {
        try
        {
            var latestVersion = await GetLatestVersionMetadataAsync(baseFileName, userContext);
            return await DownloadFileAsync(latestVersion.FileName, userContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading latest version of {BaseFileName}", baseFileName);
            throw;
        }
    }

    public async Task<bool> DeleteAllVersionsAsync(string baseFileName, UserContext userContext)
    {
        try
        {
            var versions = await ListFileVersionsAsync(baseFileName, userContext);
            var success = true;
            
            foreach (var version in versions)
            {
                var deleted = await DeleteFileAsync(version.FileName, userContext);
                if (!deleted) success = false;
            }
            
            _logger.LogInformation("All versions of {BaseFileName} deletion completed. Success: {Success}", baseFileName, success);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting all versions of {BaseFileName}", baseFileName);
            return false;
        }
    }

    public async Task<IEnumerable<BlobMetadata>> ListFileVersionsAsync(string baseFileName, UserContext userContext)
    {
        try
        {
            var allFiles = await ListFilesWithMetadataAsync(userContext);
            var versions = allFiles.Where(f => f.Structured.OriginalFileName == baseFileName || 
                                              f.CustomMetadata.ContainsKey("base-filename") && f.CustomMetadata["base-filename"] == baseFileName)
                                  .OrderBy(f => int.TryParse(f.Structured.Version, out var v) ? v : 0);
            
            _logger.LogInformation("Found {Count} versions of {BaseFileName}", versions.Count(), baseFileName);
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing versions of {BaseFileName}", baseFileName);
            throw;
        }
    }

    public async Task<BlobMetadata> GetLatestVersionMetadataAsync(string baseFileName, UserContext userContext)
    {
        try
        {
            var versions = await ListFileVersionsAsync(baseFileName, userContext);
            var latest = versions.OrderByDescending(f => int.TryParse(f.Structured.Version, out var v) ? v : 0).FirstOrDefault();
            
            if (latest == null)
                throw new FileNotFoundException($"No versions found for base file {baseFileName}");
                
            return latest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest version of {BaseFileName}", baseFileName);
            throw;
        }
    }

    public async Task<int> GetNextVersionNumberAsync(string baseFileName, UserContext userContext)
    {
        try
        {
            var versions = await ListFileVersionsAsync(baseFileName, userContext);
            if (!versions.Any()) return 1;
            
            var highestVersion = versions.Max(f => int.TryParse(f.Structured.Version, out var v) ? v : 0);
            return highestVersion + 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next version number for {BaseFileName}", baseFileName);
            return 1;
        }
    }

    public async Task<string> GetVersionedFileNameAsync(string baseFileName, int version, UserContext userContext)
    {
        // Create versioned filename: "document.pdf" -> "document_v1.pdf"
        var extension = Path.GetExtension(baseFileName);
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(baseFileName);
        return await Task.FromResult($"{nameWithoutExtension}_v{version}{extension}");
    }

    private async Task EnsureBucketExistsAsync()
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(_bucketName);

        if (!await _minioClient.BucketExistsAsync(bucketExistsArgs))
        {
            var makeBucketArgs = new MakeBucketArgs()
                .WithBucket(_bucketName);
                
            await _minioClient.MakeBucketAsync(makeBucketArgs);
            _logger.LogInformation("Bucket {BucketName} created", _bucketName);
        }
    }
}