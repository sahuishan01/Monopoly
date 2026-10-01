#!/usr/bin/env bash
# Sets the game version everywhere it is recorded.
#   tools/set-version.sh 0.2.0
# Android only installs an update when versionCode grows, so the code is derived from the
# version (MAJOR*10000 + MINOR*100 + PATCH) and is identical for local and CI builds.
set -euo pipefail
version="${1:?usage: set-version.sh MAJOR.MINOR.PATCH}"
[[ "$version" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || { echo "version must look like 1.2.3" >&2; exit 1; }
code=$(( BASH_REMATCH[1] * 10000 + BASH_REMATCH[2] * 100 + BASH_REMATCH[3] ))
root="$(cd "$(dirname "$0")/.." && pwd)"
sed -i -E "s|^config/version=.*|config/version=\"$version\"|" "$root/client/project.godot"
sed -i -E "s|^version/name=.*|version/name=\"$version\"|; s|^version/code=.*|version/code=$code|" "$root/client/export_presets.cfg"
echo "version $version (Android versionCode $code)"
