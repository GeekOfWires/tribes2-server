# syntax=docker/dockerfile:1.7
###############################################################################
# Classic dedicated server image.
#
# Derives from the base Tribes 2 server image and layers the Classic mod:
#   1. classic_v152 (a zip-in-a-zip) overlaid onto GameData, MERGING the inner
#      lowercase "classic/" into the existing capitalized "Classic/" (the Linux
#      filesystem is case-sensitive, unlike Windows/Wine) and "base/" into "base/".
#   2. TacoServer (https://github.com/ChocoTaco1/TacoServer) cloned and its
#      Classic/ tree overlaid on top of the Classic content.
#   3. TacoMaps (https://github.com/ChocoTaco1/TacoMaps) cloned; its .vl2 map
#      packs flattened into base/ (the engine only mounts .vl2 files placed
#      directly in base/, not in subfolders, so the CTF/DM/LCTF/Lak folders are
#      flattened).
#
# Launches: -online -mod Classic -dedicated
###############################################################################
ARG BASE_IMAGE=tribes2-server:base
FROM ${BASE_IMAGE}

ARG TACOSERVER_URL=https://github.com/ChocoTaco1/TacoServer.git
ARG TACOSERVER_REF=
ARG TACOMAPS_URL=https://github.com/ChocoTaco1/TacoMaps.git
ARG TACOMAPS_REF=

# 1. Classic v1.52 overlay (outer zip -> inner classic_files_v152.zip)
COPY content/classic_v152.zip /tmp/classic_v152.zip
RUN 7z x -y /tmp/classic_v152.zip -o/tmp/cl \
 && 7z x -y /tmp/cl/classic_files_v152.zip -o/tmp/cf \
 && cp -rf /tmp/cf/base/. "${GAME_DIR}/base/" \
 && cp -rf /tmp/cf/classic/. "${GAME_DIR}/Classic/" \
 && ( cp -f /tmp/cf/*.bat "${GAME_DIR}/" 2>/dev/null || true ) \
 && rm -rf /tmp/classic_v152.zip /tmp/cl /tmp/cf \
 && test -d "${GAME_DIR}/Classic/scripts"

# 2. TacoServer (Classic config/scripts) + 3. TacoMaps (.vl2 map packs into base/).
#    git is needed only for this layer; it is removed again afterwards.
RUN apt-get update \
 && apt-get install -y --no-install-recommends git \
 && rm -rf /var/lib/apt/lists/* \
 && git clone --depth 1 "${TACOSERVER_URL}" /tmp/taco \
 && if [ -n "${TACOSERVER_REF}" ]; then git -C /tmp/taco fetch --depth 1 origin "${TACOSERVER_REF}" && git -C /tmp/taco checkout FETCH_HEAD; fi \
 && cp -rf /tmp/taco/Classic/. "${GAME_DIR}/Classic/" \
 && git clone --depth 1 "${TACOMAPS_URL}" /tmp/tacomaps \
 && if [ -n "${TACOMAPS_REF}" ]; then git -C /tmp/tacomaps fetch --depth 1 origin "${TACOMAPS_REF}" && git -C /tmp/tacomaps checkout FETCH_HEAD; fi \
 && find /tmp/tacomaps -type f -name '*.vl2' -exec cp -f {} "${GAME_DIR}/base/" \; \
 && rm -rf /tmp/taco /tmp/tacomaps \
 && apt-get purge -y git && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
 && ls "${GAME_DIR}/base/"*.vl2 >/dev/null

ENV LAUNCH_PARAMS="-online -mod Classic -dedicated"
