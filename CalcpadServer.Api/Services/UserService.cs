using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CalcpadServer.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace CalcpadServer.Api.Services;

public interface IUserService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request);
    Task<AuthResponse?> RegisterAsync(RegisterRequest request);
    Task<User?> GetUserByIdAsync(string userId);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<bool> DeleteUserAsync(string userId);
    Task<bool> UpdateUserRoleAsync(string userId, UserRole role);
    Task<UserContext?> GetUserContextFromTokenAsync(string token);
}

public class UserService : IUserService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserService> _logger;
    private readonly Dictionary<string, User> _users; // In-memory storage for demo

    public UserService(IConfiguration configuration, ILogger<UserService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _users = new Dictionary<string, User>();
        
        // Create default admin user
        CreateDefaultAdmin();
    }

    private void CreateDefaultAdmin()
    {
        var adminUser = new User
        {
            Id = "admin-001",
            Username = "admin",
            Email = "admin@calcpad.com",
            PasswordHash = HashPassword("admin123"),
            Role = UserRole.Admin,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };
        
        _users[adminUser.Id] = adminUser;
        _logger.LogInformation("Default admin user created: {Username}", adminUser.Username);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        try
        {
            var user = _users.Values.FirstOrDefault(u => 
                u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase) && 
                u.IsActive);

            if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed for username: {Username}", request.Username);
                return null;
            }

            user.LastLoginAt = DateTime.UtcNow;
            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddHours(24);

            _logger.LogInformation("User {Username} logged in successfully", user.Username);
            
            return new AuthResponse
            {
                Token = token,
                User = user,
                ExpiresAt = expiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for username: {Username}", request.Username);
            return null;
        }
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        try
        {
            // Check if username already exists
            if (_users.Values.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Registration failed - username already exists: {Username}", request.Username);
                return null;
            }

            // Check if email already exists
            if (_users.Values.Any(u => u.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Registration failed - email already exists: {Email}", request.Email);
                return null;
            }

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password),
                Role = request.Role,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _users[user.Id] = user;
            
            var token = GenerateJwtToken(user);
            var expiresAt = DateTime.UtcNow.AddHours(24);

            _logger.LogInformation("User {Username} registered successfully with role {Role}", user.Username, user.Role);

            return new AuthResponse
            {
                Token = token,
                User = user,
                ExpiresAt = expiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for username: {Username}", request.Username);
            return null;
        }
    }

    public async Task<User?> GetUserByIdAsync(string userId)
    {
        _users.TryGetValue(userId, out var user);
        return await Task.FromResult(user);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        var user = _users.Values.FirstOrDefault(u => 
            u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        return await Task.FromResult(user);
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await Task.FromResult(_users.Values.Where(u => u.IsActive));
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        try
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.IsActive = false;
                _logger.LogInformation("User {Username} deactivated", user.Username);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, UserRole role)
    {
        try
        {
            if (_users.TryGetValue(userId, out var user))
            {
                user.Role = role;
                _logger.LogInformation("User {Username} role updated to {Role}", user.Username, role);
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating role for user {UserId}", userId);
            return false;
        }
    }

    public async Task<UserContext?> GetUserContextFromTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? "your-super-secret-key-here-change-in-production");
            
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            
            var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = principal.FindFirst(ClaimTypes.Name)?.Value;
            var roleString = principal.FindFirst(ClaimTypes.Role)?.Value;

            if (userId == null || username == null || roleString == null)
                return null;

            if (!Enum.TryParse<UserRole>(roleString, out var role))
                return null;

            return await Task.FromResult(new UserContext
            {
                UserId = userId,
                Username = username,
                Role = role
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Invalid token provided");
            return null;
        }
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? "your-super-secret-key-here-change-in-production");
        
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString())
            }),
            Expires = DateTime.UtcNow.AddHours(24),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        
        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + "salt"));
        return Convert.ToBase64String(hashedBytes);
    }

    private bool VerifyPassword(string password, string hash)
    {
        var passwordHash = HashPassword(password);
        return passwordHash == hash;
    }
}