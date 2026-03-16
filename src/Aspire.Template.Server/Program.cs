using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var keycloakRealm =
    builder.Configuration["Keycloak:Realm"]
    ?? throw new Exception("Keycloak:Realm was not provided.");

var keycloakClientId = builder.Configuration["Keycloak:ClientId"]
    ?? throw new Exception("Keycloak:ClientId was not provided.");

var keycloakClientSecret = builder.Configuration["Keycloak:ClientSecret"]; // optional for public clients

// Authority can be overridden via configuration. Fallback to a common localhost path for development.
var keycloakAuthority = builder.Configuration["Keycloak:Authority"]
    ?? $"http://localhost:8080/auth/realms/{keycloakRealm}";

// Use explicit Keycloak metadata address provided by configuration/user
var keycloakMetadata = "http://localhost:8080/realms/AspireNextjsKeycloak/.well-known/openid-configuration";

// BFF approach: use cookie authentication for the browser and OIDC to talk to Keycloak.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "Aspire.Bff";
        // In development we allow insecure cookies for convenience
        if (builder.Environment.IsDevelopment())
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
        }
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        // Use the resolved metadata address so middleware can fetch discovery regardless of Keycloak distribution
        options.MetadataAddress = keycloakMetadata;
        // Also set Authority to the metadata base (without the .well-known suffix) so token validation behaves normally
        var idx = keycloakMetadata.IndexOf("/.well-known", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            options.Authority = keycloakMetadata.Substring(0, idx);
        }
        else
        {
            options.Authority = keycloakAuthority;
        }
        options.ClientId = keycloakClientId;
        if (!string.IsNullOrWhiteSpace(keycloakClientSecret))
        {
            options.ClientSecret = keycloakClientSecret;
            options.ResponseType = OpenIdConnectResponseType.Code;
        }

        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.TokenValidationParameters.NameClaimType = JwtRegisteredClaimNames.Name;

        if (builder.Environment.IsDevelopment())
        {
            options.RequireHttpsMetadata = false;
        }

        // Allow Keycloak identity provider hints to be supplied via the authentication properties
        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                if (context.Properties?.Parameters != null && context.Properties.Parameters.TryGetValue("kc_idp_hint", out var idpObj))
                {
                    var idp = idpObj?.ToString();
                    if (!string.IsNullOrEmpty(idp))
                    {
                        context.ProtocolMessage.SetParameter("kc_idp_hint", idp);
                    }
                }

                return Task.CompletedTask;
            },
            OnRedirectToIdentityProviderForSignOut = context =>
            {
                // Ensure post logout redirect is set so Keycloak redirects back properly
                if (!string.IsNullOrEmpty(context.Properties?.RedirectUri))
                {
                    context.ProtocolMessage.PostLogoutRedirectUri = context.Properties.RedirectUri;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

string[] summaries =
[
    "Freezing",
    "Bracing",
    "Chilly",
    "Cool",
    "Mild",
    "Warm",
    "Balmy",
    "Hot",
    "Sweltering",
    "Scorching",
];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

// BFF auth endpoints - the frontend should not handle auth. The server proxies OIDC interactions with Keycloak.
app.MapGet("/auth/user", (HttpContext http) =>
{
    var user = http.User;
    if (user?.Identity is null || !user.Identity.IsAuthenticated)
    {
        return Results.Unauthorized();
    }

    var profile = new
    {
        name = user.Identity.Name,
        claims = user.Claims.Select(c => new { c.Type, c.Value })
    };

    return Results.Json(profile);
});

app.MapGet("/auth/login", (HttpContext http, string? returnUrl) =>
{
    var props = new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = returnUrl ?? "/" };
    return Results.Challenge(props, new[] { OpenIdConnectDefaults.AuthenticationScheme });
});

app.MapGet("/auth/logout", async (HttpContext http, string? returnUrl) =>
{
    // Sign out of the OIDC provider and clear the local cookie
    await http.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect(returnUrl ?? "/");
});

app.MapGet(
        "/weatherforecast",
        () =>
        {
            var forecast = Enumerable
                .Range(1, 5)
                .Select(index => new WeatherForecast(
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    Random.Shared.Next(-20, 55),
                    summaries[Random.Shared.Next(summaries.Length)]
                ))
                .ToArray();
            return forecast;
        }
    )
    .WithName("GetWeatherForecast")
    .RequireAuthorization();

app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
