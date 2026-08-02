# syntax=docker/dockerfile:1.7
###############################################################################
# Construction dedicated server image.
#
# Derives from the base Tribes 2 server image and applies the Construction mod.
# Construction_v0.70a.exe is a RAR self-extracting installer whose payload has a
# top-level "Construction/" folder; extracting it into GameData yields
# GameData/Construction (matching `-mod Construction`).
#
# RAR self-extractors need a full RAR decoder; the base 7-Zip's RAR support is incomplete
# for this archive ("Unsupported Method"), so we use `unar` (free) for the build layer.
#
# Sets SERVER_RULESET=Construction so the panel/supervisor launches with
# -mod Construction (override via the SERVER_RULESET env or the panel setup screen).
###############################################################################
ARG BASE_IMAGE=tribes2-server:base
FROM ${BASE_IMAGE}

COPY content/Construction_v0.70a.exe /tmp/construction.exe
RUN apt-get update \
 && apt-get install -y --no-install-recommends unar \
 && rm -rf /var/lib/apt/lists/* \
 && unar -q -f -o "${GAME_DIR}" /tmp/construction.exe \
 && rm -f /tmp/construction.exe \
 && apt-get purge -y unar && apt-get autoremove -y && rm -rf /var/lib/apt/lists/* \
 && test -d "${GAME_DIR}/Construction/scripts"

# serverprefs.cs build defaults for the Construction prefs/ (helper baked into the base image).
# PureBuild on by default: the mod re-reads $Host::Purebuild at every mission load
# (Construction/scripts/server.cs -> loadMissionStage2) and calls purebuildOn()/purebuildOff(),
# so setting the pref is enough -- the pure armor/deployable/vehicle rules follow automatically.
# Single-quoted so the shell passes $Host:: through literally (RUN vars are expanded by the
# shell, not by Docker, so single quotes are enough).
RUN sh /usr/local/bin/set-serverprefs-defaults.sh "${GAME_DIR}" \
      --pref '$Host::Purebuild = 1;' Construction

ENV SERVER_RULESET="Construction"
