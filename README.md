# ControlCenter Daemon

The lightweight telemetry agent for ControlCenter. It runs in the background on your target machines, monitoring hardware statistics and securely exposing them to your central ControlCenter Web dashboard.

## Installation

You can automatically download, build, and install the daemon as a systemd service using our one-line setup script. You will be prompted to optionally provide an API Key to secure the daemon during installation.

```bash
curl -sSL https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon/~raw/master/install.sh | sudo bash
```

### Requirements
The automated installation script requires `git` and the `.NET SDK` to be installed on your target machine to compile the daemon.

## Manual Usage
If you prefer to run it manually without installing a background service:

```bash
# Set an API token in your user configuration
./ControlCenter.Daemon setToken "your_secret_key"

# Run it on a custom port
./ControlCenter.Daemon --port 8080 --key "your_secret_key"
```
