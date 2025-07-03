namespace CalcpadViewer.Models;

public class BlobMetadata
{
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string ETag { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

public class KeyValueDisplay
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

// User management models
public enum UserRole
{
    Viewer = 1,      // Read-only access to all files and metadata
    Contributor = 2, // Can read files, add new versions, update metadata  
    Admin = 3        // Full control: delete files, manage users, update roles
}

public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Contributor;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Contributor;
}

public class UpdateUserRequest
{
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
}

public class FileVersion
{
    public string VersionId { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long Size { get; set; }
    public bool IsLatest { get; set; }
    public string ETag { get; set; } = string.Empty;
}