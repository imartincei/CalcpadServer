namespace CalcpadServer.Api.Models;

public class BlobMetadata
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string ETag { get; set; } = string.Empty;
    public Dictionary<string, string> CustomMetadata { get; set; } = new();
    public Dictionary<string, string> Tags { get; set; } = new();
    public StructuredMetadata Structured { get; set; } = new();
}

public class StructuredMetadata
{
    public string? OriginalFileName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? Version { get; set; }
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
    public Dictionary<string, string>? Metadata { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public StructuredMetadataRequest? StructuredMetadata { get; set; }
}

public class StructuredMetadataRequest
{
    public string? OriginalFileName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? Version { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DateReviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public string? TestedBy { get; set; }
    public DateTime? DateTested { get; set; }
}

public class MetadataUpdateRequest
{
    public Dictionary<string, string>? Metadata { get; set; }
    public StructuredMetadataRequest? StructuredMetadata { get; set; }
}

public class TagsUpdateRequest
{
    public Dictionary<string, string> Tags { get; set; } = new();
}

public class StructuredMetadataUpdateRequest
{
    public StructuredMetadataRequest StructuredMetadata { get; set; } = new();
}