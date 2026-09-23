using System.Text.Json;
using CodedThought.Core.Data.Interfaces;
using CodedThought.Core.Data.MySql;
using K3SManager.Data;
using K3SManager.Kubernetes;
using K3SManager.Web.Infrastructure;
using K3SManager.Web.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// CodedThought.Core reads its connections from the CoreSettings section of appsettings.json.
// AddCoreSettingsConfiguration appends that file to the end of the chain, so the sources that are
// meant to outrank it are re-applied afterwards - otherwise a container could not override the
// connection string with CoreSettings__Connections__MYSQL__ConnectionString.
builder.Configuration.AddCoreSettingsConfiguration(
    env: builder.Environment,
    settingsFileName: "appsettings.json",
    optional: false,
    reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

builder.Configuration.AddEnvironmentVariables();

// Structured logging: JSON on the console so k3s log collection gets fields, not prose.
builder.Logging.ClearProviders();
if (builder.Environment.IsDevelopment())
{
    builder.Logging.AddSimpleConsole(options =>
    {
        options.SingleLine = true;
        options.TimestampFormat = "HH:mm:ss ";
    });
}
else
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
    });
}

builder.Services.AddControllersWithViews(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression();
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Running behind Traefik in the cluster: honour the forwarded scheme so generated links stay https.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// CodedThought.Core caches its ORM map and provider lookups here.
builder.Services.AddMemoryCache();

// The provider owns a mutable connection, so each data store gets its own instance. This is
// deliberately not AddCoreDataProvider, which registers the provider as a singleton and would
// leave every request in the process sharing one MySqlConnection.
builder.Services.AddTransient<IDatabaseObject, K3SManagerMySqlProvider>();

builder.Services.AddK3SManagerData(builder.Configuration);
if (!DataServiceCollectionExtensions.IsPersistenceConfigured(builder.Configuration))
    throw new InvalidOperationException("Local authentication requires an enabled CodedThought.Core database connection in appsettings.json.");
builder.Services.AddLocalAuthentication(builder.Configuration);
builder.Services.AddK3SManagerKubernetes(builder.Configuration);

builder.Services.AddSingleton(new PersistenceState(
    DataServiceCollectionExtensions.IsPersistenceConfigured(builder.Configuration)));
builder.Services.AddScoped<ICurrentActor, HttpContextCurrentActor>();
builder.Services.AddScoped<ISafetyGate, SafetyGate>();

builder.Services
    .AddHealthChecks()
    .AddCheck<ClusterHealthCheck>("cluster", tags: ["ready"]);

var app = builder.Build();

if (args.Contains("--create-admin", StringComparer.Ordinal))
{
    await AdminCommand.RunAsync(app.Services);
    return;
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();

app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
}).AllowAnonymous();

// A namespace or node name is not an id, so it gets its own segment rather than riding on {id?}.
app.MapControllerRoute(
    name: "namespaceDetail",
    pattern: "Namespaces/Details/{name}",
    defaults: new { controller = "Namespaces", action = "Details" });

app.MapControllerRoute(
    name: "nodeDetail",
    pattern: "Nodes/Details/{name}",
    defaults: new { controller = "Nodes", action = "Details" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("K3SManager.Startup");
startupLogger.LogInformation(
    "K3S Manager starting in {Environment}; persistence {PersistenceState}",
    app.Environment.EnvironmentName,
    DataServiceCollectionExtensions.IsPersistenceConfigured(builder.Configuration) ? "enabled" : "disabled (read-only)");

await app.RunAsync().ConfigureAwait(false);

public partial class Program { }
