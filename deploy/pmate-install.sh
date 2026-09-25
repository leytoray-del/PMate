#!/usr/bin/env bash
# Installs a new PMate version on the server. scripts/deploy.ps1 uploads the build and runs:
#   sudo pmate-install /tmp/pmate.tar.gz
# 1. Stops the app and copies the database (in case the new version breaks it).
# 2. Swaps in the new version and starts it.
# 3. If it does not answer within 60 seconds, puts the old version back.
set -euo pipefail

archive="${1:?Usage: pmate-install <build.tar.gz>}"
base=/opt/pmate
data=/var/lib/pmate
health() { curl -fsS -H 'Host: localhost' http://127.0.0.1:5178/api/health >/dev/null 2>&1; }
wait_healthy() {
  for _ in $(seq 60); do health && return 0; sleep 1; done
  return 1
}

rm -rf "$base/app.new"
mkdir -p "$base/app.new"
tar -xzf "$archive" -C "$base/app.new"
# Files come from a Windows build. Make them owned by root and readable by the pmate user.
chown -R root:root "$base/app.new"
chmod -R a+rX,go-w "$base/app.new"
chmod +x "$base/app.new/PMate"

systemctl stop pmate || true

if [ -f "$data/pmate.db" ]; then
  copy="$data/backups/pre-deploy-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$copy"
  cp -p "$data"/pmate.db* "$copy/"
  chown -R pmate:pmate "$copy"
  # Keep the 5 newest copies.
  ls -1dt "$data"/backups/pre-deploy-* | tail -n +6 | xargs -r rm -rf
  echo "Database copied to $copy"
fi

rm -rf "$base/app.prev"
[ -d "$base/app" ] && mv "$base/app" "$base/app.prev"
mv "$base/app.new" "$base/app"
systemctl start pmate

if wait_healthy; then
  rm -f "$archive"
  echo "PMate is running the new version."
  exit 0
fi

echo "The new version did not start. Last log lines:" >&2
journalctl -u pmate -n 30 --no-pager >&2 || true
if [ -d "$base/app.prev" ]; then
  echo "Putting the old version back." >&2
  systemctl stop pmate || true
  rm -rf "$base/app.failed"
  mv "$base/app" "$base/app.failed"
  mv "$base/app.prev" "$base/app"
  systemctl start pmate
  wait_healthy && echo "The old version is running again." >&2
  echo "If the new version already changed the database, restore it from ${copy:-the backups folder}." >&2
fi
exit 1
