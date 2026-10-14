#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${1:-/home/opc/web/boardempire}"
BASE=/home/opc/web
OUT="$(printf '%s' "$OUT" | sed -E 's#/+#/#g; s#/+$##')"
if [[ -z "$OUT" || "$OUT" == *..* ]]; then
  echo "refusing: invalid OUT '$OUT'" >&2; exit 1
fi
OUT="$(realpath -m -- "$OUT")"
if [[ ! "$OUT" =~ ^/home/opc/web/[^/]+(/[^/]+)*$ ]]; then
  echo "refusing: OUT must be a real subdirectory of $BASE (got '$OUT')" >&2; exit 1
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
