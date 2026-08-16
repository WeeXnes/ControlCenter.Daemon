#!/usr/bin/env bash
set -e

echo "=================================================="
echo " ControlCenter Daemon Installer (Zero-Trust Agent)"
echo "=================================================="

if [ "$EUID" -ne 0 ]; then
    echo "[-] Error: This script must be run as root (or with sudo) to install the systemd service."
    exit 1
fi

SERVER_URL=""
MACHINE_ID=""
API_KEY=""
CERT_PIN=""

# Parse command line flags
while [[ $# -gt 0 ]]; do
    case "$1" in
        -s|--server)
            SERVER_URL="$2"
            shift 2
            ;;
        --id|--machine-id)
            MACHINE_ID="$2"
            shift 2
            ;;
        -k|--key|--token)
            API_KEY="$2"
            shift 2
            ;;
        --pin|--cert-fingerprint)
            CERT_PIN="$2"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done

# If arguments not provided via CLI, prompt interactively
if [ -z "$SERVER_URL" ]; then
    read -p "Enter ControlCenter Server WebSocket URL (e.g. wss://controlcenter.vpn/ws/daemon): " SERVER_URL
fi

if [ -z "$MACHINE_ID" ]; then
    read -p "Enter Machine ID (UUID from ControlCenter Web): " MACHINE_ID
fi

if [ -z "$API_KEY" ]; then
    read -p "Enter Pre-Shared Token (optional, press enter for none): " API_KEY
fi

if [ -z "$CERT_PIN" ]; then
    read -p "Enter SSL Certificate SHA-256 Pin (optional, press enter for Auto-Pin/TOFU): " CERT_PIN
fi

if ! command -v git &> /dev/null; then
    echo "[-] Error: 'git' is required but not installed."
    exit 1
fi

if ! command -v dotnet &> /dev/null; then
    echo "[-] Error: '.NET SDK' is required to build the daemon but not installed."
    echo "    Please install the .NET SDK to continue."
    exit 1
fi

TMP_DIR="/tmp/ControlCenter.Daemon.Build"
INSTALL_DIR="/opt/controlcenter-daemon"

echo "[+] Cloning repository..."
rm -rf "$TMP_DIR"
git clone https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon.git "$TMP_DIR"

echo "[+] Building daemon..."
cd "$TMP_DIR"
dotnet publish ControlCenter.Daemon.csproj -c Release -o "$INSTALL_DIR"

echo "[+] Installing background service..."
cd "$INSTALL_DIR"

# Make executable in case it lost permissions
chmod +x ControlCenter.Daemon

echo "[+] Adding to system PATH (/usr/local/bin/controlcenter-daemon)..."
ln -sf "$INSTALL_DIR/ControlCenter.Daemon" /usr/local/bin/controlcenter-daemon

CMD_ARGS=("--install-service")
if [ -n "$SERVER_URL" ]; then CMD_ARGS+=("--server" "$SERVER_URL"); fi
if [ -n "$MACHINE_ID" ]; then CMD_ARGS+=("--id" "$MACHINE_ID"); fi
if [ -n "$API_KEY" ]; then CMD_ARGS+=("--key" "$API_KEY"); fi
if [ -n "$CERT_PIN" ]; then CMD_ARGS+=("--pin" "$CERT_PIN"); fi

SUDO_USER="$SUDO_USER" ./ControlCenter.Daemon "${CMD_ARGS[@]}"

echo "[+] Cleaning up..."
rm -rf "$TMP_DIR"

echo "=================================================="
echo " [+] Installation complete!"
echo "     The daemon is now running in the background."
echo "     View status with: systemctl status controlcenter-daemon"
echo "=================================================="
