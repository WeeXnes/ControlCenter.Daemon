# ControlCenter.Daemon

Zero-trust node telemetry and orchestration agent for [ControlCenter](https://code.weexnes.dev/ControlCenter): a lightweight .NET background agent that streams system metrics over encrypted outbound WebSockets.

[![License](https://img.shields.io/badge/license-GPL--3.0-000000?style=flat-square&labelColor=1f1f1f)](#license)
[![Platform](https://img.shields.io/badge/platform-Linux-000000?style=flat-square&labelColor=1f1f1f)](#install)
[![Stack](https://img.shields.io/badge/stack-.NET-000000?style=flat-square&labelColor=1f1f1f)](#build-from-source)

[Repository](https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon) · [ControlCenter web dashboard](https://code.weexnes.dev/ControlCenter)

`ControlCenter.Daemon` is the native client agent running on managed machines. It gathers real-time hardware telemetry
and keeps a persistent, zero-trust outbound WebSocket (`wss://`) connection to the central ControlCenter web server.

## Features

Because all connections start from the node toward the ControlCenter server:

- **Zero inbound ports required:** nodes behind NAT, firewalls, home routers, or mesh VPNs (Netmaker / Tailscale / WireGuard) connect without port forwarding.
- **Certificate fingerprint pinning:** supports SHA-256 SSL fingerprint pinning against MITM attacks when talking to self-signed certificates.
- **Zero-touch auto-enrollment:** registers new nodes in the ControlCenter approval queue automatically.

### Telemetry collected

- **CPU:** total utilization percentage, core count, frequency, model name.
- **RAM:** used memory, total memory, usage percentage, swap memory.
- **Disks and storage:** mounted filesystems, filesystem types, used/free/total gigabytes, drive names.
- **Network:** active network interfaces, interface types, local and VPN IPv4/IPv6 addresses, real-time RX/TX transfer bandwidth.
- **System information:** hostname, OS platform, kernel version, machine UUID, system uptime in seconds.
- **Processes:** top CPU- and memory-consuming system processes.

## Install

### One-line installer (Linux / systemd)

```bash
curl -sSL https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon/~raw/master/install.sh | sudo bash
```

### Arch Linux / AUR

```bash
yay -S controlcenter-daemon
```

## Usage

```bash
# Connect the daemon to a ControlCenter server
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

### systemd service

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

## Build from source

Prerequisites: .NET SDK.

```bash
# Build the standalone binary
dotnet publish -c Release -r linux-x64 --self-contained false -o ./publish

# Install the binary to /usr/local/bin
sudo cp ./publish/ControlCenter.Daemon /usr/local/bin/controlcenter-daemon
sudo chmod +x /usr/local/bin/controlcenter-daemon
```

## License

GPL-3.0 License.
