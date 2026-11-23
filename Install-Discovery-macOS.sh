#!/bin/sh
set -eu

if [ "$(uname -s)" != Darwin ]; then
  printf '%s\n' 'The macOS installer requires macOS.' >&2
  exit 1
fi
if ! command -v python3 >/dev/null 2>&1; then
  printf '%s\n' 'Python 3.8 or newer is required for automatic discovery.' >&2
  exit 1
fi
root=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
exec python3 -I -B "$root/native/macos/discovery.py" --install "$root/extension/manifest.json"
