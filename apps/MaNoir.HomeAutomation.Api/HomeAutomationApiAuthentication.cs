using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Text;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Api;

/// <summary>
/// Plugs the validation of Core user tokens into the ASP.NET Core host.
/// There is no login here: users sign in on the Core, which sets the cookie.
/// </summary>
internal static class HomeAutomationApiAuthentication
{
    internal static void Configure(WebApplicationBuilder builder)
    {
        HomeAutomationApiAuthenticationOptions options = ResolveOptions(builder.Configuration, builder.Environment);

        builder.Services.AddAuthorization();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwtOptions =>
            {
                jwtOptions.MapInboundClaims = false;
                jwtOptions.TokenValidationParameters = new TokenValidationParameters()
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                jwtOptions.Events = new JwtBearerEvents()
                {
                    OnMessageReceived = context =>
                    {
                        // A bearer header wins; otherwise the browser sends the Core session cookie.
                        if (string.IsNullOrWhiteSpace(context.Request.Headers.Authorization)
                            && context.Request.Cookies.TryGetValue(options.CookieName, out string cookieToken)
                            && !string.IsNullOrWhiteSpace(cookieToken))
                        {
                            context.Token = cookieToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });
    }

    private static HomeAutomationApiAuthenticationOptions ResolveOptions(IConfiguration configuration, IHostEnvironment environment)
    {
        HomeAutomationApiAuthenticationOptions options = new HomeAutomationApiAuthenticationOptions();
        configuration.GetSection(HomeAutomationApiAuthenticationOptions.ConfigurationSectionName).Bind(options);

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            options.SigningKey = Environment.GetEnvironmentVariable(HomeAutomationApiAuthenticationOptions.SigningKeyEnvironmentVariableName);

        if (string.IsNullOrWhiteSpace(options.SigningKey) && environment.IsDevelopment())
            options.SigningKey = HomeAutomationApiAuthenticationOptions.DevelopmentSigningKey;

        if (string.IsNullOrWhiteSpace(options.SigningKey) || options.SigningKey.Length < 32)
            throw new InvalidOperationException($"Home Automation API authentication requires a signing key of at least 32 characters, given by configuration or by the {HomeAutomationApiAuthenticationOptions.SigningKeyEnvironmentVariableName} environment variable.");

        if (string.IsNullOrWhiteSpace(options.CookieName))
            options.CookieName = HomeAutomationApiAuthenticationOptions.DefaultCookieName;

        return options;
    }
}
