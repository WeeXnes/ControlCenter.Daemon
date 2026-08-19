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

// Parse command line arguments
string? cliServer = null;
string? cliId = null;
string? cliKey = null;
string? cliPin = null;

for (int i = 0; i < args.Length; i++)
{
    string arg = args[i];
    string next = i + 1 < args.Length ? args[i + 1] : "";

    if ((arg == "--address" || arg == "-a" || arg == "--server" || arg == "-s") && !string.IsNullOrEmpty(next))
    {
        cliServer = next;
        i++;
    }
    else if ((arg == "--id" || arg == "--machine-id") && !string.IsNullOrEmpty(next))
    {
        cliId = next;
        i++;
    }
    else if ((arg == "--key" || arg == "-k" || arg == "--token" || arg == "-t") && !string.IsNullOrEmpty(next))
    {
        cliKey = next;
        i++;
    }
    else if ((arg == "--pin" || arg == "--cert-fingerprint") && !string.IsNullOrEmpty(next))
    {
        cliPin = next;
        i++;
    }
    else if (i == 0 && (arg.StartsWith("http://") || arg.StartsWith("https://") || arg.StartsWith("ws://") || arg.StartsWith("wss://") || (arg.Contains(":") && !arg.StartsWith("-"))))
    {
        cliServer = arg;
    }
}

// Check for explicit helper subcommands
if (args.Length > 0)
{
    string firstArg = args[0].ToLowerInvariant();

    if (firstArg == "set-server" || firstArg == "setserver")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide a server URL.");
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
            return;
        }
        DaemonSettingsManager.SetMachineId(args[1]);
        return;
    }
    else if (firstArg == "set-token" || firstArg == "settoken")
    {
        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.WriteLine("Error: Please provide an API token.");
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
            return;
        }
        DaemonSettingsManager.SetCertFingerprint(args[1]);
        return;
    }
    else if (firstArg == "--install-service" || firstArg == "-i" || firstArg == "install-service" || firstArg == "install")
    {
        ServiceInstaller.Install(cliServer, cliId, cliKey, cliPin);
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
        Console.WriteLine("  controlcenter-daemon --address <server_address>");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  controlcenter-daemon --address 192.168.178.95:3000");
        Console.WriteLine("  sudo controlcenter-daemon --install-service --address 192.168.178.95:3000");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -a, -s, --address, --server <addr>  ControlCenter server address");
        Console.WriteLine("  -i, --install-service               Install and run as background system service");
        Console.WriteLine("  -u, --uninstall-service             Uninstall background service");
        Console.WriteLine("      --id <machine_id>               Custom Machine ID (optional, auto-generated)");
        Console.WriteLine("  -k, --key, --token <key>            Custom Auth token (optional, auto-generated)");
        Console.WriteLine("      --pin <sha256>                  Pinned SSL Certificate Fingerprint (optional)");
        Console.WriteLine();
        return;
    }
}

// Automatically resolve server and ensure persistent credentials
var (serverUrl, machineId, apiKey) = DaemonSettingsManager.EnsureCredentials(
    cliServer ?? Environment.GetEnvironmentVariable("CONTROL_CENTER_SERVER"),
    cliId ?? Environment.GetEnvironmentVariable("CONTROL_CENTER_MACHINE_ID"),
    cliKey ?? Environment.GetEnvironmentVariable("CONTROL_CENTER_API_KEY")
);

string certPin = (cliPin ?? Environment.GetEnvironmentVariable("CONTROL_CENTER_CERT_PIN") ?? DaemonSettingsManager.Load().CertFingerprint)
    .Replace(":", "").Trim().ToUpperInvariant();

Console.WriteLine("==================================================");
Console.WriteLine("  ControlCenter Daemon (Zero-Trust Client)");
Console.WriteLine("==================================================");

if (string.IsNullOrWhiteSpace(serverUrl))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("[!] No ControlCenter server address configured.");
    Console.WriteLine();
    Console.WriteLine("Quick Start:");
    Console.WriteLine("  controlcenter-daemon --address <ip_or_domain:port>");
    Console.WriteLine();
    Console.WriteLine("Example:");
    Console.WriteLine("  controlcenter-daemon --address 192.168.178.95:3000");
    Console.WriteLine();
    Console.WriteLine("Install as a permanent background service:");
    Console.WriteLine("  sudo controlcenter-daemon --install-service --address 192.168.178.95:3000");
    Console.ResetColor();
    Console.WriteLine("==================================================");
    return;
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
Console.WriteLine($"  Node Hostname:     {Environment.MachineName}");
Console.WriteLine($"  Machine ID:        {machineId}");
Console.WriteLine($"  Token Configured:  {(!string.IsNullOrEmpty(apiKey) ? "Yes (Auto-Generated)" : "No")}");
Console.WriteLine($"  Certificate Pin:   {(!string.IsNullOrEmpty(certPin) ? certPin : "Auto-Pin (TOFU)")}");
Console.WriteLine("==================================================");

// Ensure SSH public key authentication & drop-in configs are active
SshConfigHelper.EnsureSshdConfig();

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

                if (type == "setup_ssh")
                {
                    string publicKey = root.TryGetProperty("publicKey", out var pk) ? pk.GetString() ?? "" : "";
                    string keyName = root.TryGetProperty("keyName", out var kn) ? kn.GetString() ?? "" : "default";

                    if (!string.IsNullOrWhiteSpace(publicKey))
                    {
                        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Received SSH public key '{keyName}' from ControlCenter server.");
                        ProvisionSshPublicKey(ws, publicKey, ct);
                    }
                }
                else if (type == "ssh_setup_error")
                {
                    string errMsg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [!] SSH SETUP NOTICE: {errMsg}");
                    Console.ResetColor();
                }
                else if (type == "action")
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

static void ProvisionSshPublicKey(ClientWebSocket ws, string publicKey, CancellationToken ct)
{
    bool success = false;
    SshConfigHelper.EnsureSshdConfig();

    string targetUser = SystemCollector.GetActiveUsername();
    string cleanKey = publicKey.Trim();

    try
    {
        var targetSshDirs = new List<(string dir, string user)>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (targetUser != "root" && Directory.Exists($"/home/{targetUser}"))
            {
                targetSshDirs.Add(($"/home/{targetUser}/.ssh", targetUser));
            }
            else
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
                {
                    targetSshDirs.Add((Path.Combine(home, ".ssh"), targetUser));
                }
            }

            // Also ensure root has it if running as root
            if (Environment.UserName == "root" && targetUser != "root")
            {
                targetSshDirs.Add(("/root/.ssh", "root"));
            }
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            targetSshDirs.Add((Path.Combine(home, ".ssh"), targetUser));
        }

        foreach (var (sshDir, user) in targetSshDirs)
        {
            if (!Directory.Exists(sshDir))
            {
                Directory.CreateDirectory(sshDir);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try { File.SetUnixFileMode(sshDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
                }
            }

            string authKeysFile = Path.Combine(sshDir, "authorized_keys");
            string existing = File.Exists(authKeysFile) ? File.ReadAllText(authKeysFile) : "";

            if (!existing.Contains(cleanKey))
            {
                File.AppendAllText(authKeysFile, cleanKey + Environment.NewLine);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    try { File.SetUnixFileMode(authKeysFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [+] Successfully authorized ControlCenter SSH public key into {authKeysFile} (User: {user})");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [i] ControlCenter SSH key is already authorized in {authKeysFile} (User: {user})");
            }

            // Fix file ownership if running as root for a non-root user directory
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && Environment.UserName == "root" && user != "root")
            {
                SshConfigHelper.RunCommand("chown", $"-R {user}:{user} \"{sshDir}\"");
            }
        }

        success = true;
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] [-] Failed to install SSH authorized_keys: {ex.Message}");
        Console.ResetColor();
    }

    try
    {
        var response = new
        {
            type = "ssh_provisioned",
            success,
            user = targetUser
        };
        string json = JsonSerializer.Serialize(response, DaemonSerializer.Options);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
    catch { }
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
