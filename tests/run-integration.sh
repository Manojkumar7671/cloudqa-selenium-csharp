#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "BLOCKED: .NET 8 SDK is required to run Selenium integration tests." >&2
  exit 2
fi
python3 -m http.server 8765 --bind 127.0.0.1 --directory "$ROOT/tests/fixtures" > /tmp/cloudqa-fixture-server.log 2>&1 &
SERVER_PID=$!
cleanup() { kill "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true; }
trap cleanup EXIT
for _ in $(seq 1 50); do
  if curl -fsS http://127.0.0.1:8765/fixture-1.html >/dev/null; then break; fi
  sleep 0.1
done
curl -fsS http://127.0.0.1:8765/fixture-1.html >/dev/null
dotnet build src/CloudQaSelenium --nologo
for fixture in fixture-1.html fixture-2.html fixture-3.html; do
  echo "=== Selenium run: $fixture ==="
  output="$(dotnet run --no-build --project src/CloudQaSelenium -- \
    --url "http://127.0.0.1:8765/$fixture" --first-name Avery \
    --country 'United States' --state California --headless true)" || {
      printf '%s\n' "$output"
      exit 1
    }
  printf '%s\n' "$output"
  for field in 'First Name' 'Gender (Male)' 'Country' 'State'; do
    grep -Fq "PASS $field:" <<<"$output" || { echo "FAIL: missing PASS for $field" >&2; exit 1; }
  done
done
echo "PASS all fixture integration runs"
