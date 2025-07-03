using System.Net.Http;
using System.Text;
using System.Text.Json;
using CalcpadViewer.Models;

namespace CalcpadViewer.Services;

public interface IUserService
{
    Task<AuthResponse> LoginAsync(string username, string password);
    Task<List<User>> GetAllUsersAsync();
    Task<User> CreateUserAsync(RegisterRequest request);
    Task<User> UpdateUserAsync(string userId, UpdateUserRequest request);
    Task<bool> DeleteUserAsync(string userId);
    Task<List<PreDefinedTag>> GetAllTagsAsync();
    Task<PreDefinedTag> CreateTagAsync(string tagName);
    Task<bool> DeleteTagAsync(int tagId);
    void SetAuthToken(string token);
}

public class UserService : IUserService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private string? _authToken;

    public UserService(string baseUrl)
    {
        _httpClient = new HttpClient();
        _baseUrl = baseUrl.TrimEnd('/');
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public void SetAuthToken(string token)
    {
        _authToken = token;
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
    }

    public async Task<AuthResponse> LoginAsync(string username, string password)
    {
        try
        {
            var loginRequest = new LoginRequest
            {
                Username = username,
                Password = password
            };
            
            var json = JsonSerializer.Serialize(loginRequest, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/login", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseJson, _jsonOptions);
            
            if (authResponse != null)
            {
                SetAuthToken(authResponse.Token);
            }
            
            return authResponse ?? throw new Exception("Invalid response from server");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to login: {ex.Message}", ex);
        }
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/user");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            var users = JsonSerializer.Deserialize<List<User>>(json, _jsonOptions);
            
            return users ?? new List<User>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get users: {ex.Message}", ex);
        }
    }

    public async Task<User> CreateUserAsync(RegisterRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/auth/register", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var authResponse = JsonSerializer.Deserialize<AuthResponse>(responseJson, _jsonOptions);
            
            return authResponse?.User ?? throw new Exception("Invalid response from server");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create user: {ex.Message}", ex);
        }
    }

    public async Task<User> UpdateUserAsync(string userId, UpdateUserRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PutAsync($"{_baseUrl}/api/user/{userId}", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var user = JsonSerializer.Deserialize<User>(responseJson, _jsonOptions);
            
            return user ?? throw new Exception("Invalid response from server");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to update user: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteUserAsync(string userId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/user/{userId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to delete user: {ex.Message}", ex);
        }
    }

    public async Task<List<PreDefinedTag>> GetAllTagsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<PreDefinedTag>>(json, _jsonOptions) ?? new List<PreDefinedTag>();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get tags: {ex.Message}", ex);
        }
    }

    public async Task<PreDefinedTag> CreateTagAsync(string tagName)
    {
        try
        {
            var request = new { Name = tagName };
            var json = JsonSerializer.Serialize(request, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/tags", content);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<PreDefinedTag>(responseJson, _jsonOptions)!;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create tag: {ex.Message}", ex);
        }
    }

    public async Task<bool> DeleteTagAsync(int tagId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/api/tags/{tagId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to delete tag: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}