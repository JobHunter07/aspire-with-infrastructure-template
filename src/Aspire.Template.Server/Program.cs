using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddRedisClientBuilder("cache")
    .WithOutputCache();

// Add services to the container.
builder.Services.AddProblemDetails();

// Configure authentication using OpenID Connect (Keycloak)
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
    .AddCookie()
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        // Authority points to the realm in Keycloak
        var kcUrl = builder.Configuration["KEYCLOAK_URL"] ?? string.Empty;
        var kcRealm = builder.Configuration["KEYCLOAK_REALM"] ?? string.Empty;

        options.Authority = string.IsNullOrWhiteSpace(kcUrl)
            ? string.Empty
            : $"{kcUrl}/realms/{kcRealm}";

        options.ClientId = builder.Configuration["KEYCLOAK_ID"];
        options.ClientSecret = builder.Configuration["KEYCLOAK_SECRET"];
        options.ResponseType = "code";
        options.GetClaimsFromUserInfoEndpoint = true;
        options.SaveTokens = true;

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username"
        };
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseOutputCache();

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

var api = app.MapGroup("/api");
api.MapGet("weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.CacheOutput(p => p.Expire(TimeSpan.FromSeconds(5)))
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

// Enable authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();

// Protect the SPA root (index.html) so the main page requires login
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    // If the request is for the root or the index.html file, require authentication
    if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
    {
        if (!context.User?.Identity?.IsAuthenticated ?? true)
        {
            // Challenge will trigger OpenID Connect sign-in
            await context.ChallengeAsync();
            return;
        }
    }

    await next();
});

app.UseFileServer();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
