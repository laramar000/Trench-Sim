#!/bin/bash
# Installs claude-mem local-only (no account, no cloud sync, no telemetry) in cloud sessions. Idempotent.
[ "$CLAUDE_CODE_REMOTE" = "true" ] || exit 0
[ -d "$HOME/.claude/plugins/marketplaces/thedotmack" ] && exit 0
export CLAUDE_MEM_ONLINE_OPTIN=false
npx -y claude-mem install --ide claude-code --provider claude --no-auto-start >/dev/null 2>&1
npx -y claude-mem telemetry disable >/dev/null 2>&1
exit 0
