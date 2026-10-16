#!/usr/bin/env bash
# Wait for the newest game log to reach a login result, then summarize:
# non-200 requests since launch and the relevant error lines.
# Usage: check.sh <server stdout file>
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
L=$(ls -t "$ROOT"/logs/player-*.log | head -1)
O="$1"
start=$(wc -l < "$O")
for i in $(seq 1 40); do
  grep -q "Login failed\|Connection error\|Loading room\|RoomLoad\|Dorm\|dorm\|spawned" "$L" 2>/dev/null && break
  sleep 3
done
sleep 12
echo "== log: $L"
echo "== non-200 requests"
tail -n +$((start + 1)) "$O" | grep -v '^200\|TLS error\|"type":6' | cut -c1-200 | sort | uniq -c | head -40
echo "== errors"
grep '^\[\|Malformed\|expected:\|returned null\|^[A-Za-z]*Exception: \|^[A-Z]\{11\}: \|Expected ' "$L" \
  | grep -i 'error\|code:\|room\|dorm\|malformed\|expected\|spawn\|login\|null\|exception' \
  | grep -v 'OpenVR\|Rudderstack\|app_version\|TlsException\|TrustFailure\|regions for ping' | cut -c1-220 | head -30
