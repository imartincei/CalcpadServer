using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using CalcpadServer.Api.Models;

namespace CalcpadServer.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadFileAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null);
    Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType = "application/octet-stream");
    Task<string> CreateNewVersionAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null);
    Task<VersionCreationResult> CreateNewVersionWithResultAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null);
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
    private readonly IUserService _userService;

    public BlobStorageService(IMinioClient minioClient, IConfiguration configuration, ILogger<BlobStorageService> logger, IUserService userService)
    {
        _minioClient = minioClient;
        _bucketName = configuration["MinIO:BucketName"] ?? "calcpad-storage";
        _logger = logger;
        _userService = userService;
    }

    public async Task<string> UploadFileAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null)
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

            // Add structured metadata
            var headers = new Dictionary<string, string>();
            
            // Get user's email for CreatedBy field
            var user = await _userService.GetUserByIdAsync(userContext.UserId);
            var userEmail = user?.Email ?? userContext.Username; // Fallback to username if email not found
            
            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.OriginalFileName))
                    headers["x-amz-meta-original-filename"] = metadata.OriginalFileName;
                if (metadata.DateCreated.HasValue)
                    headers["x-amz-meta-date-created"] = metadata.DateCreated.Value.ToString("O");
                if (metadata.DateUpdated.HasValue)
                    headers["x-amz-meta-date-updated"] = metadata.DateUpdated.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.UpdatedBy))
                    headers["x-amz-meta-updated-by"] = metadata.UpdatedBy;
                if (metadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = metadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = metadata.ReviewedBy;
                if (!string.IsNullOrEmpty(metadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = metadata.TestedBy;
                if (metadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = metadata.DateTested.Value.ToString("O");
            }
            
            // Always set CreatedBy to the logged-in user's email
            headers["x-amz-meta-created-by"] = userEmail;

            // Always set version to 1 for new files
            headers["x-amz-meta-version"] = "1";
            headers["x-amz-meta-base-filename"] = baseFileName;
            headers["x-amz-meta-original-filename"] = baseFileName;
            headers["x-amz-meta-created-by-user-id"] = userContext.UserId;
            headers["x-amz-meta-created-by-username"] = userContext.Username;

            if (headers.Any())
            {
                _logger.LogInformation("Setting headers for upload: {Headers}", string.Join(", ", headers.Select(h => $"{h.Key}={h.Value}")));
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
            
            _logger.LogInformation("Retrieved metadata for file {FileName}: {Metadata}", fileName, 
                objectStat.MetaData != null ? string.Join(", ", objectStat.MetaData.Select(m => $"{m.Key}={m.Value}")) : "No metadata");
            
            var metadata = new Metadata();
            
            if (objectStat.MetaData != null)
            {
                foreach (var kvp in objectStat.MetaData)
                {
                    // Handle both lowercase and capitalized versions of the x-amz-meta prefix
                    // Also handle cases where Minio returns metadata keys without the prefix
                    string key = null;
                    if (kvp.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
                    {
                        key = kvp.Key.Substring("x-amz-meta-".Length);
                    }
                    else if (kvp.Key.StartsWith("X-Amz-Meta-", StringComparison.Ordinal))
                    {
                        key = kvp.Key.Substring("X-Amz-Meta-".Length);
                    }
                    else if (kvp.Key != "Content-Type" && kvp.Key != "Content-Length" && !kvp.Key.StartsWith("X-Amz-"))
                    {
                        // If it's not a standard HTTP header and doesn't start with X-Amz-, treat it as custom metadata
                        key = kvp.Key;
                    }
                    
                    if (key != null)
                    {
                        _logger.LogInformation("Processing metadata key: {OriginalKey} -> {ProcessedKey} = {Value}", kvp.Key, key, kvp.Value);
                        
                        // Parse structured metadata fields
                        switch (key.ToLower())
                        {
                            case "original-filename":
                                metadata.OriginalFileName = kvp.Value;
                                break;
                            case "date-created":
                                if (DateTime.TryParse(kvp.Value, out var dateCreated))
                                    metadata.DateCreated = dateCreated;
                                break;
                            case "date-updated":
                                if (DateTime.TryParse(kvp.Value, out var dateUpdated))
                                    metadata.DateUpdated = dateUpdated;
                                break;
                            case "version":
                                metadata.Version = kvp.Value;
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
                    else
                    {
                        // Log non-x-amz-meta headers for debugging
                        _logger.LogDebug("Non-metadata header found: {Key} = {Value}", kvp.Key, kvp.Value);
                    }
                }
            }

            var tags = await GetFileTagsAsync(fileName, userContext);

            _logger.LogInformation("Final metadata for {FileName}: Version={Version}, OriginalFilename={OriginalFilename}", 
                fileName, metadata.Version, metadata.OriginalFileName);

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

    public async Task<string> CreateNewVersionAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null)
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
            
            // Get user's email for UpdatedBy field (for new versions)
            var user = await _userService.GetUserByIdAsync(userContext.UserId);
            var userEmail = user?.Email ?? userContext.Username; // Fallback to username if email not found

            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.OriginalFileName))
                    headers["x-amz-meta-original-filename"] = metadata.OriginalFileName;
                if (metadata.DateCreated.HasValue)
                    headers["x-amz-meta-date-created"] = metadata.DateCreated.Value.ToString("O");
                if (metadata.DateUpdated.HasValue)
                    headers["x-amz-meta-date-updated"] = metadata.DateUpdated.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.CreatedBy))
                    headers["x-amz-meta-created-by"] = metadata.CreatedBy;
                if (metadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = metadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = metadata.ReviewedBy;
                if (!string.IsNullOrEmpty(metadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = metadata.TestedBy;
                if (metadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = metadata.DateTested.Value.ToString("O");
            }
            
            // Always set UpdatedBy to the current user's email (for new versions)
            headers["x-amz-meta-updated-by"] = userEmail;

            headers["x-amz-meta-version"] = nextVersion.ToString();
            headers["x-amz-meta-base-filename"] = baseFileName;
            headers["x-amz-meta-original-filename"] = baseFileName;
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

    public async Task<VersionCreationResult> CreateNewVersionWithResultAsync(string baseFileName, Stream fileStream, UserContext userContext, string contentType = "application/octet-stream", Dictionary<string, string>? tags = null, MetadataRequest? metadata = null)
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
            
            // Get user's email for UpdatedBy field (for new versions)
            var user = await _userService.GetUserByIdAsync(userContext.UserId);
            var userEmail = user?.Email ?? userContext.Username; // Fallback to username if email not found

            if (metadata != null)
            {
                if (!string.IsNullOrEmpty(metadata.OriginalFileName))
                    headers["x-amz-meta-original-filename"] = metadata.OriginalFileName;
                if (metadata.DateCreated.HasValue)
                    headers["x-amz-meta-date-created"] = metadata.DateCreated.Value.ToString("O");
                if (metadata.DateUpdated.HasValue)
                    headers["x-amz-meta-date-updated"] = metadata.DateUpdated.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.CreatedBy))
                    headers["x-amz-meta-created-by"] = metadata.CreatedBy;
                if (metadata.DateReviewed.HasValue)
                    headers["x-amz-meta-date-reviewed"] = metadata.DateReviewed.Value.ToString("O");
                if (!string.IsNullOrEmpty(metadata.ReviewedBy))
                    headers["x-amz-meta-reviewed-by"] = metadata.ReviewedBy;
                if (!string.IsNullOrEmpty(metadata.TestedBy))
                    headers["x-amz-meta-tested-by"] = metadata.TestedBy;
                if (metadata.DateTested.HasValue)
                    headers["x-amz-meta-date-tested"] = metadata.DateTested.Value.ToString("O");
            }
            
            // Always set UpdatedBy to the current user's email (for new versions)
            headers["x-amz-meta-updated-by"] = userEmail;

            headers["x-amz-meta-version"] = nextVersion.ToString();
            headers["x-amz-meta-base-filename"] = baseFileName;
            headers["x-amz-meta-original-filename"] = baseFileName;
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
            
            return new VersionCreationResult
            {
                VersionedFileName = versionedFileName,
                Version = nextVersion,
                BaseFileName = baseFileName
            };
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
            var versions = allFiles.Where(f => f.Metadata.OriginalFileName == baseFileName)
                                  .OrderBy(f => int.TryParse(f.Metadata.Version, out var v) ? v : 0);
            
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
            var latest = versions.OrderByDescending(f => int.TryParse(f.Metadata.Version, out var v) ? v : 0).FirstOrDefault();
            
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
            
            var highestVersion = versions.Max(f => int.TryParse(f.Metadata.Version, out var v) ? v : 0);
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