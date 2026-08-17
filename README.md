# ControlCenter Daemon

<div align="center">
  <h3>Zero-Trust Node Telemetry & Orchestration Agent for ControlCenter</h3>
  <p>Lightweight .NET background agent streaming system metrics over encrypted outbound WebSockets.</p>
  <p>
    <a href="https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon"><strong>Repository</strong></a> •
    <a href="https://code.weexnes.dev/ControlCenter"><strong>ControlCenter Web Dashboard</strong></a>
  </p>
</div>

---

## 🌟 Overview

`ControlCenter.Daemon` is the native client agent running on managed machines. It gathers real-time hardware telemetry and establishes a persistent, zero-trust outbound WebSocket (`wss://`) connection to the central ControlCenter Web server.

Because all connections originate from the node toward the ControlCenter server:
- **Zero Inbound Ports Required:** Nodes behind NAT, firewalls, home routers, or mesh VPNs (Netmaker / Tailscale / WireGuard) connect seamlessly without port forwarding.
- **Certificate Fingerprint Pinning:** Supports SHA-256 SSL fingerprint pinning for protection against MITM attacks when communicating over self-signed certificates.
- **Zero-Touch Auto-Enrollment:** Automatically registers new nodes into the ControlCenter approval queue.

---

## 📊 Telemetry Metrics Collected

- **CPU:** Total utilization percentage, core count, frequency, model name.
- **RAM:** Used memory, total memory, usage percentage, swap memory.
- **Disks & Storage:** Mounted filesystems, filesystem types, used/free/total gigabytes, drive names.
- **Network:** Active network interfaces, interface types, local and VPN IPv4/IPv6 addresses, real-time RX/TX transfer bandwidth.
- **System Information:** Hostname, OS platform, kernel version, machine UUID, system uptime in seconds.
- **Processes:** Top CPU and memory-consuming system processes.

---

## 🚀 Installation

### Option 1: One-Line Auto Installer (Linux / Systemd)

```bash
curl -sSL https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon/~raw/master/install.sh | sudo bash
```

### Option 2: Arch Linux / AUR Package

```bash
yay -S controlcenter-daemon
```

### Option 3: Manual Compilation (.NET SDK)

```bash
# Build standalone binary
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

# Install binary to /usr/local/bin
sudo cp ./publish/ControlCenter.Daemon /usr/local/bin/controlcenter-daemon
sudo chmod +x /usr/local/bin/controlcenter-daemon
```

---

## 🛠️ Usage & CLI Commands

```bash
# Connect daemon to ControlCenter server
controlcenter-daemon --address 100.64.0.4:4382

# Connect with a specific pre-shared authentication token
controlcenter-daemon --address 100.64.0.4:4382 --token "your_token_here"

# Connect with certificate fingerprint pinning (SHA-256)
controlcenter-daemon --address 100.64.0.4:4382 --fingerprint "E95CD8594F4BCE1786046E8F1B097F0D1ABD346712873DA1C7E8529CDC5C1991"

# Save persistent configuration options to ~/.controlcenter-daemon.json
controlcenter-daemon setAddress "100.64.0.4:4382"
controlcenter-daemon setToken "your_token_here"
controlcenter-daemon setFingerprint "your_sha256_fingerprint"

# View current daemon status and collected hardware stats
controlcenter-daemon status
```

---

## ⚙️ Systemd Service Configuration

When installed as a service (`controlcenter-daemon.service`):

```ini
[Unit]
Description=ControlCenter Daemon Agent
After=network.target network-online.target
Wants=network-online.target

[Service]
Type=simple
ExecStart=/usr/local/bin/controlcenter-daemon --address 100.64.0.4:4382
Restart=always
RestartSec=5
User=root

[Install]
WantedBy=multi-user.target
```

Manage the service:
```bash
sudo systemctl enable --now controlcenter-daemon
sudo systemctl status controlcenter-daemon
journalctl -u controlcenter-daemon -f
```

---

## 📄 License

GPL-3.0 License.
