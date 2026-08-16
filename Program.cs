using System;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ControlCenter.Daemon.Services;

// Check command line arguments for management and setup commands
if (args.Length > 0)
{
    string firstArg = args[0].ToLowerInvariant();

    if (firstArg == "set-server" || firstArg == "setserver" || firstArg == "-s")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide a server URL.");
            Console.WriteLine("Usage: controlcenter-daemon set-server wss://controlcenter.vpn/ws/daemon");
            return;
        }
        DaemonSettingsManager.SetServer(args[1]);
        return;
    }
    else if (firstArg == "set-id" || firstArg == "setid")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide a machine ID.");
            Console.WriteLine("Usage: controlcenter-daemon set-id <uuid>");
            return;
        }
        DaemonSettingsManager.SetMachineId(args[1]);
        return;
    }
    else if (firstArg == "set-token" || firstArg == "settoken" || firstArg == "-t")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide an API token.");
            Console.WriteLine("Usage: controlcenter-daemon set-token <token>");
            return;
        }
        DaemonSettingsManager.SetToken(args[1]);
        return;
    }
    else if (firstArg == "set-pin" || firstArg == "setpin")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide an SSL SHA-256 certificate fingerprint.");
            Console.WriteLine("Usage: controlcenter-daemon set-pin <sha256_hash>");
            return;
        }
        DaemonSettingsManager.SetCertFingerprint(args[1]);
        return;
    }
    else if (firstArg == "--install-service" || firstArg == "-i" || firstArg == "install-service" || firstArg == "install")
    {
        var saved = DaemonSettingsManager.Load();
        string? instServer = Environment.GetEnvironmentVariable("CONTROL_CENTER_SERVER") ?? saved.ServerUrl;
        string? instId = Environment.GetEnvironmentVariable("CONTROL_CENTER_MACHINE_ID") ?? saved.MachineId;
        string? instKey = Environment.GetEnvironmentVariable("CONTROL_CENTER_API_KEY") ?? saved.ApiKey;
        string? instPin = Environment.GetEnvironmentVariable("CONTROL_CENTER_CERT_PIN") ?? saved.CertFingerprint;

        for (int i = 1; i < args.Length; i++)
        {
            if ((args[i] == "--server" || args[i] == "-s") && i + 1 < args.Length)
            {
                instServer = args[i + 1];
            }
            if ((args[i] == "--id" || args[i] == "--machine-id") && i + 1 < args.Length)
            {
                instId = args[i + 1];
            }
            if ((args[i] == "--key" || args[i] == "-k" || args[i] == "--token") && i + 1 < args.Length)
            {
                instKey = args[i + 1];
            }
            if ((args[i] == "--pin" || args[i] == "--cert-fingerprint") && i + 1 < args.Length)
            {
                instPin = args[i + 1];
            }
        }

        ServiceInstaller.Install(instServer, instId, instKey, instPin);
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
        Console.WriteLine("  ControlCenter Daemon (Zero-Trust Reverse Client)");
        Console.WriteLine("==================================================");
        Console.WriteLine("Usage:");
        Console.WriteLine("  controlcenter-daemon [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  --install-service [opts]  Install and start as background system service");
        Console.WriteLine("  --uninstall-service       Uninstall background service");
        Console.WriteLine("  set-server <url>          Set ControlCenter server WebSocket URL");
        Console.WriteLine("  set-id <uuid>             Set Machine ID");
        Console.WriteLine("  set-token <token>         Set authentication token");
        Console.WriteLine("  set-pin <sha256>          Set pinned SSL certificate fingerprint");
        Console.WriteLine();
        Console.WriteLine("Options for --install-service or standalone run:");
        Console.WriteLine("  -s, --server <url>        Server URL (e.g. wss://controlcenter.vpn/ws/daemon)");
        Console.WriteLine("      --id <machine_id>     Machine ID (UUID)");
        Console.WriteLine("  -k, --key, --token <key>  Authentication token");
        Console.WriteLine("      --pin <sha256>        Server SSL Certificate SHA-256 fingerprint");
        Console.WriteLine("  -h, --help                Show command line help");
        Console.WriteLine();
        return;
    }
}

// Load saved settings
var daemonSettings = DaemonSettingsManager.Load();

// Resolve active configuration (CLI args -> Env Vars -> Settings.json)
string serverUrl = Environment.GetEnvironmentVariable("CONTROL_CENTER_SERVER") ?? daemonSettings.ServerUrl;
string machineId = Environment.GetEnvironmentVariable("CONTROL_CENTER_MACHINE_ID") ?? daemonSettings.MachineId;
string apiKey = Environment.GetEnvironmentVariable("CONTROL_CENTER_API_KEY") ?? daemonSettings.ApiKey;
string certPin = Environment.GetEnvironmentVariable("CONTROL_CENTER_CERT_PIN") ?? daemonSettings.CertFingerprint;

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--server" || args[i] == "-s") && i + 1 < args.Length) serverUrl = args[i + 1];
    if ((args[i] == "--id" || args[i] == "--machine-id") && i + 1 < args.Length) machineId = args[i + 1];
    if ((args[i] == "--key" || args[i] == "-k" || args[i] == "--token") && i + 1 < args.Length) apiKey = args[i + 1];
    if ((args[i] == "--pin" || args[i] == "--cert-fingerprint") && i + 1 < args.Length) certPin = args[i + 1];
}

// Ensure certificate pin is formatted cleanly
certPin = certPin.Replace(":", "").Trim().ToUpperInvariant();

Console.WriteLine("==================================================");
Console.WriteLine("  ControlCenter Daemon v2.0 (Zero-Trust Client)");
Console.WriteLine("==================================================");

if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(machineId))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[!] Daemon configuration incomplete.");
    Console.WriteLine("    Please configure the server URL and Machine ID:");
    Console.WriteLine("    controlcenter-daemon set-server wss://<your-controlcenter-url>/ws/daemon");
    Console.WriteLine("    controlcenter-daemon set-id <your-machine-id>");
    Console.WriteLine("    controlcenter-daemon set-token <your-token>");
    Console.ResetColor();
    Console.WriteLine();
    Console.WriteLine("Or install the background service in one command:");
    Console.WriteLine("    sudo controlcenter-daemon --install-service --server wss://... --id ... --key ...");
    Console.WriteLine();
    return;
}

// Normalize WebSocket URL
if (serverUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    serverUrl = "wss://" + serverUrl.Substring(8);
}
else if (serverUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
{
    serverUrl = "ws://" + serverUrl.Substring(7);
}
else if (!serverUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase) && !serverUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase))
{
    serverUrl = "wss://" + serverUrl;
}

if (!serverUrl.Contains("/ws/daemon") && !serverUrl.Contains("/ws/"))
{
    serverUrl = serverUrl.TrimEnd('/') + "/ws/daemon";
}

// Construct target URI with authentication parameters
var uriBuilder = new UriBuilder(serverUrl);
string queryParam = $"machineId={Uri.EscapeDataString(machineId)}";
if (!string.IsNullOrEmpty(apiKey))
{
    queryParam += $"&token={Uri.EscapeDataString(apiKey)}";
}
uriBuilder.Query = queryParam;
Uri targetUri = uriBuilder.Uri;

Console.WriteLine($"  Target Server:     {serverUrl}");
Console.WriteLine($"  Machine ID:        {machineId}");
Console.WriteLine($"  Token Configured:  {(!string.IsNullOrEmpty(apiKey) ? "Yes" : "No")}");
Console.WriteLine($"  Certificate Pin:   {(!string.IsNullOrEmpty(certPin) ? certPin : "Auto-Pin (TOFU)")}");
Console.WriteLine("==================================================");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    Console.WriteLine("\n[+] Daemon stopping...");
};

int backoffSeconds = 2;

// Main resilient connection loop
while (!cts.IsCancellationRequested)
{
    using var ws = new ClientWebSocket();
    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

    // TLS Certificate Pinning & Validation
    ws.Options.RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
    {
        if (certificate == null) return false;

        byte[] certHash = certificate.GetCertHash(HashAlgorithmName.SHA256);
        string presentedFingerprint = Convert.ToHexString(certHash).ToUpperInvariant();

        if (!string.IsNullOrEmpty(certPin))
        {
            bool match = string.Equals(presentedFingerprint, certPin, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[SECURITY ALERT] Server SSL Certificate Fingerprint Mismatch!");
                Console.WriteLine($"  Expected Pin: {certPin}");
                Console.WriteLine($"  Received:     {presentedFingerprint}");
                Console.WriteLine($"  Aborting connection to prevent Man-In-The-Middle attack.");
                Console.ResetColor();
            }
            return match;
        }

        // TOFU (Trust-On-First-Use) Mode: Auto-pin the certificate fingerprint
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[TOFU PINNING] Automatically pinned server SSL certificate: {presentedFingerprint}");
        Console.ResetColor();
        certPin = presentedFingerprint;
        DaemonSettingsManager.SetCertFingerprint(presentedFingerprint);
        return true;
    };

    try
    {
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Connecting to ControlCenter server...");
        await ws.ConnectAsync(targetUri, cts.Token);

        backoffSeconds = 2; // Reset backoff on successful connection
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [+] Connected successfully! Telemetry streaming active.");
        Console.ResetColor();

        // Run send telemetry and receive command loops concurrently
        var telemetryTask = SendTelemetryLoopAsync(ws, cts.Token);
        var receiveTask = ReceiveCommandsLoopAsync(ws, cts.Token);

        await Task.WhenAny(telemetryTask, receiveTask);
    }
    catch (OperationCanceledException)
    {
        break;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Connection issue: {ex.Message}");
        Console.ResetColor();
    }

    if (!cts.IsCancellationRequested)
    {
        Console.WriteLine($"Reconnecting in {backoffSeconds}s...");
        try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), cts.Token); } catch { }
        backoffSeconds = Math.Min(30, backoffSeconds * 2);
    }
}

// Background loop to collect and push system telemetry
static async Task SendTelemetryLoopAsync(ClientWebSocket ws, CancellationToken ct)
{
    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        try
        {
            var telemetry = await SystemCollector.CollectAsync();
            var payload = new
            {
                type = "telemetry",
                data = telemetry
            };

            string json = JsonSerializer.Serialize(payload, DaemonSerializer.Options);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
        }
        catch (Exception)
        {
            break;
        }

        await Task.Delay(3000, ct);
    }
}

// Background loop to receive control actions from server
static async Task ReceiveCommandsLoopAsync(ClientWebSocket ws, CancellationToken ct)
{
    var buffer = new byte[8192];

    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        try
        {
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                break;
            }

            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeElem))
            {
                string type = typeElem.GetString() ?? "";

                if (type == "action")
                {
                    string action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
                    string actionId = root.TryGetProperty("actionId", out var aid) ? aid.GetString() ?? "" : "";

                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Received remote command: '{action}'");
                    ExecuteRemoteAction(ws, action, actionId, ct);
                }
                else if (type == "ping")
                {
                    byte[] pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                    await ws.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Text, true, ct);
                }
            }
        }
        catch (Exception)
        {
            break;
        }
    }
}

static void ExecuteRemoteAction(ClientWebSocket ws, string action, string actionId, CancellationToken ct)
{
    bool success = false;
    string message = "";

    try
    {
        if (action == "restart")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("shutdown", "-r now");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("shutdown", "/r /t 0");
            }
            success = true;
            message = "System reboot initiated.";
        }
        else if (action == "shutdown")
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("shutdown", "-h now");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("shutdown", "/s /t 0");
            }
            success = true;
            message = "System shutdown initiated.";
        }
        else
        {
            message = $"Unknown action '{action}'";
        }
    }
    catch (Exception ex)
    {
        message = $"Execution failed: {ex.Message}";
    }

    try
    {
        var response = new
        {
            type = "action_result",
            actionId,
            success,
            message
        };
        string json = JsonSerializer.Serialize(response, DaemonSerializer.Options);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    catch { }
}

public static class DaemonSerializer
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
