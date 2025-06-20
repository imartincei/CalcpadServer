namespace CalcpadServer.Api.Models;

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

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}

public class UserContext
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}