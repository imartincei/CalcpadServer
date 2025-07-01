using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CalcpadServer.Api.Data;
using CalcpadServer.Api.Models;
using Microsoft.EntityFrameworkCore;
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
    Task<User?> UpdateUserAsync(string userId, UpdateUserRequest request);
    Task<bool> UpdateUserRoleAsync(string userId, UserRole role);
    Task<UserContext?> GetUserContextFromTokenAsync(string token);
    Task EnsureDefaultAdminAsync();
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserService> _logger;

    public UserService(ApplicationDbContext context, IConfiguration configuration, ILogger<UserService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task EnsureDefaultAdminAsync()
    {
        // Check if admin user already exists
        var adminExists = await _context.Users.AnyAsync(u => u.Username == "admin");
        
        if (!adminExists)
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
            
            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Default admin user created: {Username}", adminUser.Username);
        }
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);
            
            if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
            {
                return null;
            }

            // Update last login
            user.LastLoginAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            
            _logger.LogInformation("User {Username} logged in successfully", user.Username);
            
            return new AuthResponse
            {
                Token = token,
                User = user,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user {Username}", request.Username);
            return null;
        }
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        try
        {
            // Check if user already exists
            var existingUser = await _context.Users.AnyAsync(u => u.Username == request.Username || u.Email == request.Email);
            if (existingUser)
            {
                return null;
            }

            var user = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password),
                Role = request.Role,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            
            _logger.LogInformation("User {Username} registered successfully with role {Role}", user.Username, user.Role);
            
            return new AuthResponse
            {
                Token = token,
                User = user,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for user {Username}", request.Username);
            return null;
        }
    }

    public async Task<User?> GetUserByIdAsync(string userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<IEnumerable<User>> GetAllUsersAsync()
    {
        return await _context.Users.ToListAsync();
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("User {Username} deleted successfully", user.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return false;
        }
    }

    public async Task<User?> UpdateUserAsync(string userId, UpdateUserRequest request)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return null;
            }

            user.Role = request.Role;
            user.IsActive = request.IsActive;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("User {Username} updated: Role={Role}, IsActive={IsActive}", user.Username, request.Role, request.IsActive);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating user {UserId}", userId);
            return null;
        }
    }

    public async Task<bool> UpdateUserRoleAsync(string userId, UserRole role)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            user.Role = role;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("User {Username} role updated to {Role}", user.Username, role);
            return true;
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
            var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? "calcpad-jwt-secret-key-change-in-production-minimum-32-characters");
            
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            var userId = jwtToken.Claims.First(x => x.Type == "nameid").Value;
            var username = jwtToken.Claims.First(x => x.Type == "unique_name").Value;
            var role = Enum.Parse<UserRole>(jwtToken.Claims.First(x => x.Type == "role").Value);

            // Verify user still exists and is active
            var user = await _context.Users.FindAsync(userId);
            if (user == null || !user.IsActive)
            {
                return null;
            }

            return new UserContext
            {
                UserId = userId,
                Username = username,
                Role = role
            };
        }
        catch
        {
            return null;
        }
    }

    private string GenerateJwtToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Secret"] ?? "calcpad-jwt-secret-key-change-in-production-minimum-32-characters");
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

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(hashedBytes);
    }

    private static bool VerifyPassword(string password, string hash)
    {
        var hashOfInput = HashPassword(password);
        return hashOfInput == hash;
    }
}