using CalcpadServer.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CalcpadServer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthInfoController : ControllerBase
{
    private readonly AuthenticationConfig _authConfig;

    public AuthInfoController(IOptions<AuthenticationConfig> authConfig)
    {
        _authConfig = authConfig.Value;
    }

    [HttpGet]
    public IActionResult GetAuthInfo()
    {
        return Ok(new
        {
            Provider = _authConfig.Provider,
            LocalEnabled = _authConfig.Local.Enabled,
            OidcEnabled = _authConfig.OIDC.Enabled,
            SamlEnabled = _authConfig.SAML.Enabled,
            AllowUserRegistration = _authConfig.Local.AllowUserRegistration,
            RequireEmailConfirmation = _authConfig.Local.RequireEmailConfirmation
        });
    }
}