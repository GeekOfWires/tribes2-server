# syntax=docker/dockerfile:1.7
###############################################################################
# Construction dedicated server image.
#
# Derives from the base Tribes 2 server image and applies the Construction mod.
# Construction_v0.70a.exe is a RAR self-extracting installer whose payload has a
# top-level "Construction/" folder; extracting it into GameData yields
# GameData/Construction (matching `-mod Construction`).
#
# RAR extraction needs the full 7-Zip port (`7zz` from the `7zip` package);
# the base image's p7zip-full lacks the RAR codec.
#
# Sets SERVER_RULESET=Construction so the panel/supervisor launches with
# -mod Construction (override via the SERVER_RULESET env or the panel setup screen).
###############################################################################
ARG BASE_IMAGE=tribes2-server:base
FROM ${BASE_IMAGE}

COPY content/Construction_v0.70a.exe /tmp/construction.exe
RUN apt-get update \
 && apt-get install -y --no-install-recommends 7zip \
 && rm -rf /var/lib/apt/lists/* \
 && 7zz x -y /tmp/construction.exe -o"${GAME_DIR}" \
 && rm -f /tmp/construction.exe \
 && apt-get purge -y 7zip && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
 && test -d "${GAME_DIR}/Construction/scripts"

ENV SERVER_RULESET="Construction"
