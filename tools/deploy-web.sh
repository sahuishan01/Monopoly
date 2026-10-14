#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/home/opc/web/boardempire}"
if [[ -z "$OUT" || "$OUT" == "/" || "$OUT" != /home/opc/web/* || "$OUT" == *..* ]]; then
  echo "refusing: OUT must be under /home/opc/web/ (got '$OUT')" >&2
  exit 1
fi
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
dotnet publish "$ROOT/src/Game.Web" -c Release -o "$STAGE"
if [[ ! -f "$STAGE/wwwroot/index.html" || ! -f "$STAGE/wwwroot/_framework/blazor.webassembly.js" ]]; then
  echo "refusing: publish output incomplete, live site left untouched" >&2
  exit 1
fi
mkdir -p "$OUT"
rsync -a --delete "$STAGE/wwwroot/" "$OUT/"
echo "Deployed to $OUT"
