using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ControlCenter.Daemon.Services
{
    public static class ServiceInstaller
    {
        private const string ServiceName = "controlcenter-daemon";

        public static void Install(string? serverUrl, string? machineId, string? apiKey, string? certFingerprint)
        {
            string execPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string workingDir = AppContext.BaseDirectory;
            string? currentUser = Environment.GetEnvironmentVariable("SUDO_USER");
            if (string.IsNullOrEmpty(currentUser))
            {
                currentUser = Environment.UserName;
            }

            if (string.IsNullOrEmpty(execPath))
            {
                Console.WriteLine("Error: Unable to determine daemon executable path.");
                return;
            }

            // Auto-generate ID and key credentials if not provided, and save settings securely
            var (resServer, resId, resKey) = DaemonSettingsManager.EnsureCredentials(serverUrl, machineId, apiKey);
            var settings = DaemonSettingsManager.Load();
            if (!string.IsNullOrWhiteSpace(certFingerprint)) settings.CertFingerprint = certFingerprint.Replace(":", "").Trim().ToUpperInvariant();
            DaemonSettingsManager.Save(settings);

            Console.WriteLine($"Installing {ServiceName} service (Zero-Trust Reverse WebSocket)...");
            Console.WriteLine($"  Executable Path:   {execPath}");
            Console.WriteLine($"  Working Directory: {workingDir}");
            Console.WriteLine($"  Server URL:        {settings.ServerUrl}");
            Console.WriteLine($"  Machine ID:        {settings.MachineId}");
            Console.WriteLine($"  Token Configured:  {(!string.IsNullOrEmpty(settings.ApiKey) ? "Yes (Auto-Generated)" : "No")}");
            Console.WriteLine($"  SSL Pinning:       {(!string.IsNullOrEmpty(settings.CertFingerprint) ? settings.CertFingerprint : "TOFU / Automatic")}");
            Console.WriteLine();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Ensure SSH configuration and immediate reload right during service installation
                SshConfigHelper.EnsureSshdConfig();
                InstallLinuxSystemd(execPath, workingDir);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InstallWindowsService(execPath);
            }
            else
            {
                Console.WriteLine("Unsupported OS platform for automatic service installation.");
            }
        }

        public static void Uninstall()
        {
            Console.WriteLine($"Uninstalling {ServiceName} service...");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                UninstallLinuxSystemd();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                UninstallWindowsService();
            }
            else
            {
                Console.WriteLine("Unsupported OS platform for automatic service uninstallation.");
            }
        }

        private static void InstallLinuxSystemd(string execPath, string workingDir)
        {
            string unitFilePath = $"/etc/systemd/system/{ServiceName}.service";

            string unitContent = $@"# ControlCenter Daemon Service Unit (Zero-Trust Reverse Client)
[Unit]
Description=ControlCenter Telemetry & Control Daemon Service
After=network.target network-online.target sshd.service ssh.service
Wants=network-online.target

[Service]
Type=simple
User=root
WorkingDirectory={workingDir}
ExecStart={execPath}
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
";

            try
            {
                File.WriteAllText(unitFilePath, unitContent);
                Console.WriteLine($"[+] Created systemd unit file at: {unitFilePath}");

                RunCommand("systemctl", "daemon-reload");
                RunCommand("systemctl", $"enable --now {ServiceName}.service");

                Console.WriteLine($"[+] Successfully installed and started {ServiceName}.service!");
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"[-] Error: Permission denied writing to {unitFilePath}.");
                Console.WriteLine($"    Please run the installer with root/sudo privileges: sudo {execPath} --install-service");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error installing systemd service: {ex.Message}");
            }
        }

        private static void UninstallLinuxSystemd()
        {
            string unitFilePath = $"/etc/systemd/system/{ServiceName}.service";

            try
            {
                RunCommand("systemctl", $"disable --now {ServiceName}.service");

                if (File.Exists(unitFilePath))
                {
                    File.Delete(unitFilePath);
                    Console.WriteLine($"[+] Removed {unitFilePath}");
                }

                RunCommand("systemctl", "daemon-reload");
                Console.WriteLine($"[+] Successfully uninstalled {ServiceName}.service!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error uninstalling systemd service: {ex.Message}");
            }
        }

        private static void InstallWindowsService(string execPath)
        {
            try
            {
                RunCommand("sc.exe", $"create {ServiceName} binPath= \"{execPath}\" start= auto DisplayName= \"ControlCenter Daemon\"");
                RunCommand("sc.exe", $"start {ServiceName}");
                Console.WriteLine($"[+] Successfully installed and started {ServiceName} Windows service!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error installing Windows service: {ex.Message}");
            }
        }

        private static void UninstallWindowsService()
        {
            try
            {
                RunCommand("sc.exe", $"stop {ServiceName}");
                RunCommand("sc.exe", $"delete {ServiceName}");
                Console.WriteLine($"[+] Successfully uninstalled {ServiceName} Windows service!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Error uninstalling Windows service: {ex.Message}");
            }
        }

        private static void RunCommand(string fileName, string arguments)
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            proc.WaitForExit();
        }
    }
}

