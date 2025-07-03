using CalcpadServer.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace CalcpadServer.Api.Extensions;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddConfigurableAuthentication(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        var authConfig = configuration.GetSection("Authentication").Get<AuthenticationConfig>()
            ?? new AuthenticationConfig();

        // Register the auth config for DI
        services.Configure<AuthenticationConfig>(configuration.GetSection("Authentication"));

        var authProvider = Enum.TryParse<AuthProvider>(authConfig.Provider, true, out var provider) 
            ? provider 
            : AuthProvider.Local;

        switch (authProvider)
        {
            case AuthProvider.Local:
                services.AddLocalAuthentication(authConfig.Local);
                break;
            case AuthProvider.OIDC:
                services.AddOidcAuthentication(authConfig.OIDC);
                break;
            case AuthProvider.SAML:
                services.AddSamlAuthentication(authConfig.SAML);
                break;
            default:
                throw new InvalidOperationException($"Unknown authentication provider: {authConfig.Provider}");
        }

        return services;
    }

    private static IServiceCollection AddLocalAuthentication(
        this IServiceCollection services, 
        LocalAuthConfig config)
    {
        if (!config.Enabled)
        {
            throw new InvalidOperationException("Local authentication is not enabled");
        }

        // Validate JWT config
        if (string.IsNullOrEmpty(config.Jwt.Secret) || config.Jwt.Secret.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(config.Jwt.Secret)),
                    ValidateIssuer = !string.IsNullOrEmpty(config.Jwt.Issuer),
                    ValidIssuer = config.Jwt.Issuer,
                    ValidateAudience = !string.IsNullOrEmpty(config.Jwt.Audience),
                    ValidAudience = config.Jwt.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        return services;
    }

    private static IServiceCollection AddOidcAuthentication(
        this IServiceCollection services, 
        OidcAuthConfig config)
    {
        if (!config.Enabled)
        {
            throw new InvalidOperationException("OIDC authentication is not enabled");
        }

        // Validate OIDC config
        if (string.IsNullOrEmpty(config.Authority) || 
            string.IsNullOrEmpty(config.ClientId) || 
            string.IsNullOrEmpty(config.ClientSecret))
        {
            throw new InvalidOperationException("OIDC configuration is incomplete");
        }

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "oidc";
        })
        .AddCookie("Cookies")
        .AddOpenIdConnect("oidc", options =>
        {
            options.Authority = config.Authority;
            options.ClientId = config.ClientId;
            options.ClientSecret = config.ClientSecret;
            options.ResponseType = config.ResponseType;
            options.CallbackPath = config.CallbackPath;
            
            options.Scope.Clear();
            foreach (var scope in config.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                options.Scope.Add(scope);
            }

            options.SaveTokens = true;
        });

        return services;
    }

    private static IServiceCollection AddSamlAuthentication(
        this IServiceCollection services, 
        SamlAuthConfig config)
    {
        if (!config.Enabled)
        {
            throw new InvalidOperationException("SAML authentication is not enabled");
        }

        // TODO: Implement SAML authentication
        // This would typically use a library like ITfoxtec.Identity.Saml2
        throw new NotImplementedException("SAML authentication is not yet implemented");
    }
}