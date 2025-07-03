namespace CalcpadServer.Api.Models;

public class AuthenticationConfig
{
    public string Provider { get; set; } = "Local";
    public LocalAuthConfig Local { get; set; } = new();
    public OidcAuthConfig OIDC { get; set; } = new();
    public SamlAuthConfig SAML { get; set; } = new();
}

public class LocalAuthConfig
{
    public bool Enabled { get; set; } = true;
    public bool RequireEmailConfirmation { get; set; } = false;
    public bool AllowUserRegistration { get; set; } = true;
    public JwtConfig Jwt { get; set; } = new();
}

public class JwtConfig
{
    public string Secret { get; set; } = string.Empty;
    public int ExpiryInHours { get; set; } = 24;
    public string Issuer { get; set; } = "CalcpadServer";
    public string Audience { get; set; } = "CalcpadClients";
}

public class OidcAuthConfig
{
    public bool Enabled { get; set; } = false;
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid profile email";
    public string ResponseType { get; set; } = "code";
    public string CallbackPath { get; set; } = "/signin-oidc";
}

public class SamlAuthConfig
{
    public bool Enabled { get; set; } = false;
    public string EntityId { get; set; } = string.Empty;
    public string SignOnUrl { get; set; } = string.Empty;
    public string Certificate { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/signin-saml";
}

public enum AuthProvider
{
    Local,
    OIDC,
    SAML
}