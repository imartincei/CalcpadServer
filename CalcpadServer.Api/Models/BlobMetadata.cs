namespace CalcpadServer.Api.Models;

public class BlobMetadata
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string ETag { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = new();
    public Metadata Metadata { get; set; } = new();
}

public class Metadata
{
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DateReviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public string? TestedBy { get; set; }
    public DateTime? DateTested { get; set; }
}

public class UploadRequest
{
    public IFormFile File { get; set; } = null!;
    public Dictionary<string, string>? Tags { get; set; }
    public MetadataRequest? Metadata { get; set; }
}

public class MetadataRequest
{
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DateReviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public string? TestedBy { get; set; }
    public DateTime? DateTested { get; set; }
    public string? FileCategory { get; set; }
}

public class MetadataUpdateRequest
{
    public MetadataRequest? Metadata { get; set; }
}

public class TagsUpdateRequest
{
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class FileVersion
{
    public string VersionId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public bool IsLatest { get; set; }
    public string ETag { get; set; } = string.Empty;
}

public class PreDefinedTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}