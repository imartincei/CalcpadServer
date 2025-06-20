using CalcpadServer.Api.Attributes;
using CalcpadServer.Api.Models;
using CalcpadServer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CalcpadServer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IUserService userService, ILogger<AuthController> logger)
    {
        _userService = userService;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var authResponse = await _userService.LoginAsync(request);
            
            if (authResponse == null)
            {
                return Unauthorized(new { message = "Invalid username or password" });
            }

            return Ok(authResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("register")]
    [AuthorizeRole(UserRole.Admin)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var authResponse = await _userService.RegisterAsync(request);
            
            if (authResponse == null)
            {
                return BadRequest(new { message = "Username or email already exists" });
            }

            return Ok(authResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("profile")]
    [AuthorizeRole(UserRole.Viewer)]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userContext = (UserContext)HttpContext.Items["UserContext"]!;
            var user = await _userService.GetUserByIdAsync(userContext.UserId);
            
            if (user == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profile");
            return StatusCode(500, "Internal server error");
        }
    }
}