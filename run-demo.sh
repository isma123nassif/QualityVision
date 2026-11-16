#!/usr/bin/env bash
# Runs the demo in the Production environment from a published build.
# (A Debug bin run without Development gives HTTP 500 for _framework/blazor.web.js and scoped CSS:
#  static web assets are only resolved from the manifest in Development.)
# Usage: ./run-demo.sh [--no-build] [--video]   (--video = Plan B, plays samples/demo.mp4)
set -euo pipefail

cd "$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")"

PROJECT=src/QualityVision.Web
PUBLISH_DIR=artifacts/demo
BUILD=1
VIDEO=0
for arg in "$@"; do
  case "$arg" in
    --no-build) BUILD=0 ;;
    --video) VIDEO=1 ;;
    -h|--help) echo "Usage: $0 [--no-build] [--video]"; exit 0 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done

# 1. Publish
if [[ $BUILD -eq 1 ]]; then
  dotnet publish "$PROJECT" -c Release -o "$PUBLISH_DIR"
elif [[ ! -x "$PUBLISH_DIR/QualityVision.Web" ]]; then
  echo "error: $PUBLISH_DIR/QualityVision.Web not found; run without --no-build first." >&2
  exit 1
fi

# 2. Machine-local settings (git-ignored: Kestrel HTTPS cert paths, overrides)
if [[ -f "$PROJECT/appsettings.Local.json" ]]; then
  cp "$PROJECT/appsettings.Local.json" "$PUBLISH_DIR/appsettings.Local.json"
else
  echo "warning: $PROJECT/appsettings.Local.json missing: no HTTPS endpoint (phone page on :5443 unavailable)." >&2
fi

# 3. Rules: content root = working dir = publish dir, so rules.json is read (and hot-reloaded) from there.
if [[ ! -f "$PUBLISH_DIR/rules.json" ]]; then
  echo "error: $PUBLISH_DIR/rules.json missing from publish output." >&2
  exit 1
fi
echo "rules.json (hot-reload, edit this file): $(pwd)/$PUBLISH_DIR/rules.json"
echo "  note: a republish overwrites it only if the source copy is newer."
echo "inspection database (history/counters; deleted by rm -rf artifacts): $(pwd)/$PUBLISH_DIR/data/qualityvision.db"

# 4. Preflight
if command -v nvidia-smi >/dev/null 2>&1 && nvidia-smi >/dev/null 2>&1; then
  echo "GPU: $(nvidia-smi --query-gpu=name --format=csv,noheader | head -n1)"
else
  echo "warning: nvidia-smi not available; the app will fall back to CPU." >&2
fi

for port in 5080 5443; do
  if ss -ltn "sport = :$port" | tail -n +2 | grep -q .; then
    echo "error: port $port is already in use." >&2
    exit 1
  fi
done

IP="$(ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p' | head -n1 || true)"
IP="${IP:-localhost}"
echo
echo "Dashboard: http://$IP:5080"
echo "Phone:     https://$IP:5443/kamera"
echo "Health:    http://$IP:5080/health"
echo

# Plan B: play samples/demo.mp4 (absolute path: the app runs from the publish dir, not the repo root)
if [[ $VIDEO -eq 1 ]]; then
  VIDEO_FILE="$(pwd)/samples/demo.mp4"
  if [[ ! -f "$VIDEO_FILE" ]]; then
    echo "error: $VIDEO_FILE not found (see samples/README.md to record it)." >&2
    exit 1
  fi
  export Camera__Source=VideoFile
  export Camera__VideoFile="$VIDEO_FILE"
  echo "Plan B: video file $VIDEO_FILE"
fi

# 5. Run (exec so SIGTERM reaches the app)
cd "$PUBLISH_DIR"
exec env ASPNETCORE_ENVIRONMENT=Production ./QualityVision.Web
