using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ControlCenter.Daemon.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Check command line arguments for service installation / management / configuration commands
if (args.Length > 0)
{
    string firstArg = args[0].ToLowerInvariant();
    if (firstArg == "settoken" || firstArg == "--set-token" || firstArg == "set-token" || firstArg == "-t")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide an API token.");
            Console.WriteLine("Usage: ./ControlCenter.Daemon setToken <your-secret-token>");
            return;
        }
        DaemonSettingsManager.SetToken(args[1]);
        return;
    }
    else if (firstArg == "--install-service" || firstArg == "-i" || firstArg == "install-service" || firstArg == "install")
    {
        var savedSettings = DaemonSettingsManager.Load();
        int customPort = savedSettings.Port;
        if (int.TryParse(Environment.GetEnvironmentVariable("CONTROL_CENTER_PORT"), out var pEnv)) customPort = pEnv;
        string? customApiKey = Environment.GetEnvironmentVariable("CONTROL_CENTER_API_KEY") ?? savedSettings.ApiKey;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[i + 1], out var pArg))
            {
                customPort = pArg;
            }
            if ((args[i] == "--key" || args[i] == "-k") && i + 1 < args.Length)
            {
                customApiKey = args[i + 1];
            }
        }

        ServiceInstaller.Install(customPort, customApiKey);
        return;
    }
    else if (firstArg == "--uninstall-service" || firstArg == "-u" || firstArg == "uninstall-service" || firstArg == "uninstall")
    {
        ServiceInstaller.Uninstall();
        return;
    }
    else if (firstArg == "--help" || firstArg == "-h" || firstArg == "help")
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("  ControlCenter C# Daemon CLI");
        Console.WriteLine("==================================================");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- [options]");
        Console.WriteLine("  ./ControlCenter.Daemon [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  setToken <token>          Set API key token in ~/.ControlCenterDaemon/settings.json");
        Console.WriteLine("  -i, --install-service     Install as background systemd (Linux) or Windows service.");
        Console.WriteLine("  -u, --uninstall-service   Uninstall background service.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -p, --port <port>         Specify listening port (default: 9100).");
        Console.WriteLine("  -k, --key <api_key>       Specify API key authentication token.");
        Console.WriteLine("  -h, --help                Show command line help.");
        Console.WriteLine();
        return;
    }
}

var daemonSettings = DaemonSettingsManager.Load();
var builder = WebApplication.CreateBuilder(args);

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure listening port (hierarchy: env var -> cli arg -> settings.json -> default 9100)
int port = daemonSettings.Port;
if (int.TryParse(Environment.GetEnvironmentVariable("CONTROL_CENTER_PORT"), out var p))
{
    port = p;
}

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[i + 1], out var pArg))
    {
        port = pArg;
    }
}

builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var app = builder.Build();

app.UseCors();

// Resolve API Key (hierarchy: env var -> cli arg -> settings.json)
string? configuredApiKey = Environment.GetEnvironmentVariable("CONTROL_CENTER_API_KEY");

if (string.IsNullOrEmpty(configuredApiKey))
{
    for (int i = 0; i < args.Length; i++)
    {
        if ((args[i] == "--key" || args[i] == "-k") && i + 1 < args.Length)
        {
            configuredApiKey = args[i + 1];
        }
    }
}

if (string.IsNullOrEmpty(configuredApiKey))
{
    configuredApiKey = daemonSettings.ApiKey;
}

// API Key Security Middleware
app.Use(async (context, next) =>
{
    if (!string.IsNullOrEmpty(configuredApiKey))
    {
        var apiKeyHeader = context.Request.Headers["X-API-Key"].ToString();
        var authHeader = context.Request.Headers["Authorization"].ToString();

        bool valid = apiKeyHeader == configuredApiKey ||
                     (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ") && authHeader.Substring(7) == configuredApiKey);

        if (!valid && context.Request.Path != "/health")
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized: Invalid or missing API Key" });
            return;
        }
    }
    await next();
});

// Endpoints
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ControlCenter.Daemon", timestamp = DateTime.UtcNow }));

app.MapGet("/api/v1/telemetry", async () =>
{
    var telemetry = await SystemCollector.CollectAsync();
    return Results.Ok(telemetry);
});

app.MapPost("/api/v1/control/restart", () =>
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("shutdown", "-r now");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("shutdown", "/r /t 0");
        }
        return Results.Ok(new { status = "success", message = "System reboot initiated." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to restart system: {ex.Message}");
    }
});

app.MapPost("/api/v1/control/shutdown", () =>
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("shutdown", "-h now");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("shutdown", "/s /t 0");
        }
        return Results.Ok(new { status = "success", message = "System shutdown initiated." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to shutdown system: {ex.Message}");
    }
});

Console.WriteLine($"==================================================");
Console.WriteLine($"  ControlCenter C# Daemon (ASP.NET Core Kestrel)");
Console.WriteLine($"  Running on Port {port}");
Console.WriteLine($"==================================================");

app.Run();
