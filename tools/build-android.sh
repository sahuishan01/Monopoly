#!/usr/bin/env bash
# Builds the Android APK signed with the one permanent BoardEmpire key, so that every build
# installs as an update over the previous one.
#   tools/build-android.sh [preset]        preset defaults to "Android"
# Needs: GODOT (path to the Godot .NET editor binary) and the signing settings, either in the
# environment or in ~/.config/boardempire/signing.env:
#   BOARDEMPIRE_KEYSTORE, BOARDEMPIRE_KEY_ALIAS, BOARDEMPIRE_KEY_PASSWORD
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
preset="${1:-Android}"
env_file="${BOARDEMPIRE_SIGNING_ENV:-$HOME/.config/boardempire/signing.env}"
if [ -z "${BOARDEMPIRE_KEYSTORE:-}" ] && [ -f "$env_file" ]; then
  set -a; . "$env_file"; set +a
fi
: "${BOARDEMPIRE_KEYSTORE:?no signing key configured; refusing to build with a throwaway key}"
: "${BOARDEMPIRE_KEY_ALIAS:?}" "${BOARDEMPIRE_KEY_PASSWORD:?}"
[ -f "$BOARDEMPIRE_KEYSTORE" ] || { echo "keystore not found: $BOARDEMPIRE_KEYSTORE" >&2; exit 1; }
godot="${GODOT:-$(command -v godot || true)}"
[ -x "$godot" ] || { echo "set GODOT to the Godot .NET editor binary" >&2; exit 1; }

# The same key signs debug and release exports.
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH="$BOARDEMPIRE_KEYSTORE"
export GODOT_ANDROID_KEYSTORE_RELEASE_USER="$BOARDEMPIRE_KEY_ALIAS"
export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD="$BOARDEMPIRE_KEY_PASSWORD"
export GODOT_ANDROID_KEYSTORE_DEBUG_PATH="$BOARDEMPIRE_KEYSTORE"
export GODOT_ANDROID_KEYSTORE_DEBUG_USER="$BOARDEMPIRE_KEY_ALIAS"
export GODOT_ANDROID_KEYSTORE_DEBUG_PASSWORD="$BOARDEMPIRE_KEY_PASSWORD"

version="$(sed -n -E 's/^config\/version="(.*)"/\1/p' "$root/client/project.godot")"
out="$root/client/build/BoardEmpire-$version.apk"
mkdir -p "$root/client/build"
cd "$root/client"
"$godot" --headless --path . --import >/dev/null 2>&1 || true
"$godot" --headless --path . --export-release "$preset" "$out"
[ -s "$out" ] || { echo "export produced no APK" >&2; exit 1; }

# Refuse to hand out an APK signed with anything but the pinned key.
expected="$(tr -d ' \n:' < "$root/deploy/android-signing-sha256.txt" | tr 'A-F' 'a-f')"
apksigner="$(ls "${ANDROID_HOME:-$HOME/Android/Sdk}"/build-tools/*/apksigner | sort -V | tail -1)"
actual="$("$apksigner" verify --print-certs "$out" 2>/dev/null | sed -n -E 's/^Signer #1 certificate SHA-256 digest: //p')"
if [ "$actual" != "$expected" ]; then
  echo "APK not signed with pinned key (got '$actual'); signing with apksigner..."
  "$apksigner" sign \
    --ks "$BOARDEMPIRE_KEYSTORE" \
    --ks-key-alias "$BOARDEMPIRE_KEY_ALIAS" \
    --ks-pass "pass:$BOARDEMPIRE_KEY_PASSWORD" \
    "$out"
  actual="$("$apksigner" verify --print-certs "$out" 2>/dev/null | sed -n -E 's/^Signer #1 certificate SHA-256 digest: //p')"
fi
if [ "$actual" != "$expected" ]; then
  echo "signature mismatch: got $actual, expected $expected" >&2
  "$apksigner" verify --verbose "$out" || true
  rm -f "$out"
  exit 1
fi
echo "built $out (version $version, signing key verified)"

