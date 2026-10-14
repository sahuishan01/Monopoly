#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/home/opc/web/boardempire}"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
dotnet publish "$ROOT/src/Game.Web" -c Release -o "$STAGE"
mkdir -p "$OUT"
rsync -a --delete "$STAGE/wwwroot/" "$OUT/"
echo "Deployed to $OUT"
