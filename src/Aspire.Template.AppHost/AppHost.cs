// PostGres: https://github.com/dotnet/aspire-samples/blob/main/samples/aspire-shop/AspireShop.AppHost/AppHost.cs
// Infisical: https://github.com/FracturedCode/Infisical/blob/master/AppHost/Program.cs
// KeyCloak: https://github.com/jonathanpotts/AspireNextjsKeycloak/blob/main/AspireNextjsKeycloak.AppHost/AppHost.cs

using System.Security.Cryptography;

var builder = DistributedApplication.CreateBuilder(args);

// var pgPassword    = builder.AddParameter("POSTGRES-PASSWORD", () => new Guid(RandomNumberGenerator.GetBytes(16)).ToString(), true);
var encryptionKey = builder.AddParameter("infisical-encryptionKey", () => RandomNumberGenerator.GetHexString(32), true);
var authSecret    = builder.AddParameter("infisical-AUTH-SECRET", () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), true);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

if (builder.ExecutionContext.IsRunMode)
{
    // Data volumes don't work on ACA for Postgres so only add when running
    postgres.WithDataVolume();
}

var infisicaldb = postgres.AddDatabase("infisicaldb");
var templateDb = postgres.AddDatabase("templatedb");

var cache = builder.AddRedis("redis-infisical");

var infisical = builder.AddContainer("infisical", "infisical/infisical:latest")
    .WithEnvironment("INFISICAL_PORT", "8080")
    .WithEnvironment("REDIS_URL", () => $"{cache.Resource.Name}:{cache.Resource.PrimaryEndpoint.TargetPort}")
    .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
    .WithEnvironment("AUTH_SECRET", authSecret)
    .WithEnvironment("PORT", "80")
    .WithEnvironment("POSTGRES_DB", "infisicaldb")
    .WithReference(infisicaldb)
    .WaitFor(infisicaldb)
    .WithHttpEndpoint(targetPort: 80, port: int.Parse(args.LastOrDefault() ?? "5005"), name: "infisical");
    //.WithHttpHealthCheck("/api/status", 8080)

var keycloak = builder
    .AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("./KeycloakRealms");

var keycloakRealm = builder.AddParameter("keycloak-realm", "AspireNextjsKeycloak");
var keycloakClientId = builder.AddParameter("keycloak-client-id", "apiservice");


var server = builder.AddProject<Projects.Aspire_Template_Server>("apiservice")
    .WithEnvironment("KEYCLOAK__REALM", keycloakRealm)
    .WithEnvironment("KEYCLOAK__CLIENTID", keycloakClientId)
    .WithReference(templateDb)
    .WaitFor(templateDb)
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var betterAuthSecret = builder.AddParameter(
    "better-auth-secret",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true
);

var betterAuthUrl = builder.AddParameter(
    "better-auth-url",
    "http://webfrontend-aspirenextjskeycloak.dev.localhost:3000"
);

var keycloakId = builder.AddParameter("keycloak-id", "webfrontend");
var keycloakSecret = builder.AddParameter("keycloak-secret", "O94wFQrYPY4Eg2AZvMUQFR71203FwC1r", secret: true);

var keycloakScope = builder.AddParameter("keycloak-scope", "apiservice");

var webfrontend = builder.AddViteApp("WebUI", "../frontend")
    .WithReference(server)
    .WaitFor(server)
    .WithExternalHttpEndpoints()
    .WithEnvironment("BETTER_AUTH_SECRET", betterAuthSecret)
    .WithEnvironment("BETTER_AUTH_URL", betterAuthUrl)
    .WithEnvironment("KEYCLOAK_REALM", keycloakRealm)
    .WithEnvironment("KEYCLOAK_ID", keycloakId)
    .WithEnvironment("KEYCLOAK_SECRET", keycloakSecret)
    .WithEnvironment("KEYCLOAK_SCOPE", keycloakScope)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
