#!/bin/bash
# Installs Godot headless-capable binary for cloud sessions (idempotent).
set -e
[ "$CLAUDE_CODE_REMOTE" = "true" ] || exit 0
VERSION="${GODOT_VERSION:-4.4.1}"
BIN="/opt/godot/Godot_v${VERSION}-stable_linux.x86_64"
if [ ! -x "$BIN" ]; then
  mkdir -p /opt/godot
  tmp=$(mktemp -d)
  curl -sSL -o "$tmp/g.zip" "https://github.com/godotengine/godot/releases/download/${VERSION}-stable/Godot_v${VERSION}-stable_linux.x86_64.zip"
  unzip -oq "$tmp/g.zip" -d /opt/godot
  rm -rf "$tmp"
  chmod +x "$BIN"
fi
ln -sf "$BIN" /usr/local/bin/godot
