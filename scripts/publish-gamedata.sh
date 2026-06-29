#!/usr/bin/env bash
# Publish content/tribesinstall.7z as a GitHub Release asset and point CI at it.
#
# The game data is too large for git, so CI pulls it from a Release asset instead.
# Run this once (and again whenever the data changes). Requires the `gh` CLI,
# authenticated against the repo (`gh auth login`).
#
# Usage: scripts/publish-gamedata.sh [release-tag]   (default tag: gamedata-v1)
set -euo pipefail

TAG="${1:-gamedata-v1}"
FILE="content/tribesinstall.7z"

if [ ! -f "$FILE" ]; then
  echo "ERROR: $FILE not found. Place the game archive there first." >&2
  exit 1
fi

# Create the release if missing, then upload (overwriting any existing asset).
if gh release view "$TAG" >/dev/null 2>&1; then
  gh release upload "$TAG" "$FILE" --clobber
else
  gh release create "$TAG" "$FILE" --title "Game data" --notes "tribesinstall.7z game data for the container build"
fi

# Tell the build workflow which release to download from.
gh variable set GAMEDATA_RELEASE_TAG --body "$TAG"

echo "Done. CI will fetch game data from release '$TAG' (GAMEDATA_RELEASE_TAG set)."
