#!/usr/bin/env bash
# One-time setup of an Ubuntu server for PMate. Run on the server:
#   sudo bash server-setup.sh pmate-yourname.duckdns.org
# Safe to run again. It does not open ports 80 and 443. Do that in the cloud firewall,
# after you have created your account (see docs/deploy-gcp.md, why).
set -euo pipefail

domain="${1:?Usage: sudo bash server-setup.sh <domain name>}"
here="$(cd "$(dirname "$0")" && pwd)"
[ "$(id -u)" -eq 0 ] || { echo "Run with sudo." >&2; exit 1; }

echo "== Time zone"
timedatectl set-timezone Asia/Singapore

echo "== Packages"
export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get upgrade -yq
apt-get install -yq curl gnupg debian-keyring debian-archive-keyring apt-transport-https unattended-upgrades

if ! command -v caddy >/dev/null; then
  echo "== Caddy (web server that handles HTTPS)"
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -q
  apt-get install -yq caddy
fi

# The smallest free server has 1 GB memory. Swap space stops the app from crashing when memory is short.
mem_kb=$(awk '/MemTotal/ {print $2}' /proc/meminfo)
if [ "$mem_kb" -lt 2000000 ] && ! swapon --show | grep -q .; then
  echo "== 2 GB swap file"
  fallocate -l 2G /swapfile
  chmod 600 /swapfile
  mkswap /swapfile
  swapon /swapfile
  grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >> /etc/fstab
fi

echo "== PMate user and folders"
id pmate >/dev/null 2>&1 || useradd --system --home-dir /var/lib/pmate --shell /usr/sbin/nologin pmate
mkdir -p /opt/pmate /var/lib/pmate/backups
chown -R pmate:pmate /var/lib/pmate
chmod 700 /var/lib/pmate

install -m 755 "$here/pmate-install.sh" /usr/local/bin/pmate-install
sed "s/__DOMAIN__/$domain/g" "$here/pmate.service" > /etc/systemd/system/pmate.service
sed "s/__DOMAIN__/$domain/g" "$here/Caddyfile" > /etc/caddy/Caddyfile
systemctl daemon-reload
systemctl enable pmate
systemctl enable caddy
systemctl reload caddy || systemctl restart caddy

echo
echo "Done. Next: run scripts/deploy.ps1 on your PC to install the app."
