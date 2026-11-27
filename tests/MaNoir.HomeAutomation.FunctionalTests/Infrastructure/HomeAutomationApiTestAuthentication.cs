using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

/// <summary>
/// Gives a test host its own token settings and mints tokens shaped like the Core ones,
/// since this repository has no login endpoint.
/// </summary>
internal static class HomeAutomationApiTestAuthentication
{
    internal const string Issuer = "tests.manoir.core";
    internal const string Audience = "tests.manoir.core.users";
    internal const string SigningKey = "tests-only-signing-key-value-with-more-than-32-chars";

    internal static void Configure(WebApplicationBuilder builder)
    {
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>()
        {
            ["MaNoir:Authentication:UsersJwt:Issuer"] = Issuer,
            ["MaNoir:Authentication:UsersJwt:Audience"] = Audience,
            ["MaNoir:Authentication:UsersJwt:SigningKey"] = SigningKey,
        });
    }

    internal static HttpClient CreateAuthenticatedClient(WebApplication app)
    {
        HttpClient client = app.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateAccessToken());
        return client;
    }

    internal static string CreateAccessToken(string signingKey = SigningKey, string audience = Audience, DateTime? expiresUtc = null)
    {
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, "sarah"),
            new Claim(ClaimTypes.NameIdentifier, "sarah"),
            new Claim("manoir_auth_kind", "user"),
        ];

        DateTime expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(30);
        JwtSecurityToken token = new(
            issuer: Issuer,
            audience: audience,
            claims: claims,
            notBefore: expires.AddHours(-1),
            expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
