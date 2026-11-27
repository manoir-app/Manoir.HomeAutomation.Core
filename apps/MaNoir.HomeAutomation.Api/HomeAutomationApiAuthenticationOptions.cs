namespace MaNoir.HomeAutomation.Api;

/// <summary>
/// Defines how the user tokens issued by MaNoir.Core.Api are validated.
/// The values must match the Core API ones, so that the session cookie it sets is accepted here.
/// </summary>
public sealed class HomeAutomationApiAuthenticationOptions
{
    public const string ConfigurationSectionName = "MaNoir:Authentication:UsersJwt";
    public const string SigningKeyEnvironmentVariableName = "HOMEAUTOMATION_AUTH_JWT_SIGNING_KEY";
    public const string DevelopmentSigningKey = "development-only-manoir-users-jwt-signing-key-2026";
    public const string DefaultCookieName = "manoir_users_access_token";

    public string Issuer { get; set; } = "manoir.core";

    public string Audience { get; set; } = "manoir.core.users";

    public string SigningKey { get; set; }

    public string CookieName { get; set; } = DefaultCookieName;
}
