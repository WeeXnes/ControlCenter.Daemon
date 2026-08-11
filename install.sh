#!/usr/bin/env bash
set -e

echo "=================================================="
echo " ControlCenter Daemon Installer"
echo "=================================================="

if [ "$EUID" -ne 0 ]; then
    echo "[-] Error: This script must be run as root (or with sudo) to install the systemd service."
    exit 1
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

# Ask for token
read -p "Enter API Token (leave empty for none): " API_TOKEN

TMP_DIR="/tmp/ControlCenter.Daemon.Build"
INSTALL_DIR="/opt/controlcenter-daemon"

echo "[+] Cloning repository..."
rm -rf "$TMP_DIR"
git clone https://code.weexnes.dev/ControlCenter/ControlCenter.Daemon.git "$TMP_DIR"

echo "[+] Building daemon..."
cd "$TMP_DIR"
dotnet publish -c Release -o "$INSTALL_DIR"

echo "[+] Installing background service..."
cd "$INSTALL_DIR"

# Make executable in case it lost permissions
chmod +x ControlCenter.Daemon

echo "[+] Adding to system PATH (/usr/local/bin/controlcenter-daemon)..."
ln -sf "$INSTALL_DIR/ControlCenter.Daemon" /usr/local/bin/controlcenter-daemon

if [ -z "$API_TOKEN" ]; then
    echo "[+] Configuring without API key..."
    SUDO_USER="$SUDO_USER" ./ControlCenter.Daemon --install-service
else
    echo "[+] Configuring with API key..."
    SUDO_USER="$SUDO_USER" ./ControlCenter.Daemon --install-service --key "$API_TOKEN"
fi

echo "[+] Cleaning up..."
rm -rf "$TMP_DIR"

echo "=================================================="
echo " [+] Installation complete!"
echo "     The daemon is now running in the background."
echo "     View status with: systemctl status controlcenter-daemon"
echo "=================================================="
