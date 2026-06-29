# syntax=docker/dockerfile:1.7
###############################################################################
# Tribes 2 dedicated server, OWNED by an ASP.NET Core 10 control panel.
#
# The image is fundamentally an ASP.NET Core app (FROM mcr.microsoft.com/dotnet/
# aspnet:10.0). Wine + the Tribes 2 game are layered on top, and the panel (PID 1)
# manages the game via a hosted Worker Service -- so the panel stays up even when
# the game stops/crashes.
#
# Dependency strategy (per ChocoTaco1/docker-tribesnext-server, Wine branch):
#   * OLD Win32/VC++6 runtime  -> the game's own bundled MSVCRT.dll + Tribes2.exe.local
#   * NEW Win32 APIs (QoL)     -> real VC++ 2022 DLLs (vcrun22) dropped into system32
#   No winetricks, no xvfb, no Ruby (the 2025 QoL is native code in IFC22.dll).
###############################################################################

# ---------------------------------------------------------------- React SPA build
FROM node:22-bookworm-slim AS spa-build
WORKDIR /spa
COPY panel/ClientApp/package*.json ./
RUN npm install
COPY panel/ClientApp/ ./
RUN npm run build           # vite outDir ../wwwroot -> /wwwroot

# ------------------------------------------------ ASP.NET Core publish (framework-dependent)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS app-build
WORKDIR /src
COPY panel/TribesServerPanel.csproj ./
RUN dotnet restore
COPY panel/ ./
COPY --from=spa-build /wwwroot ./wwwroot
RUN dotnet publish -c Release -o /app/publish

# ---------------------------------------------------------------- runtime image (ASP.NET owner)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root

ARG PATCH_URL="https://www.tribesnext.com/files/TribesNEXT_20250922_preview.exe"
ARG PATCH_SHA256=""
# Microsoft VC++ 2022 x86 redistributable (official). Fetched at build time so Microsoft
# is the distributor and no Microsoft binaries are vendored in this repo.
ARG VCREDIST_URL="https://aka.ms/vs/17/release/vc_redist.x86.exe"
ARG VCREDIST_SHA256=""
ARG WINE_BRANCH=stable
# Pinned to Wine 10 (the Tribes 2-on-Wine community's proven version); Wine 11
# regresses the T2 mission-start path. Blank = latest for the branch.
ARG WINE_VERSION=10.0.0.0

ENV DEBIAN_FRONTEND=noninteractive \
    WINEPREFIX=/opt/wineprefix \
    WINEARCH=win32 \
    WINEDEBUG=-all \
    GAME_DIR=/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData \
    WINEDLLOVERRIDES="msvcp140,msvcp140_1,msvcp140_2,msvcp140_atomic_wait,msvcp140_codecvt_ids,concrt140,vcamp140,vccorlib140,vcomp140,vcruntime140=n,b"

# 1. i386 multiarch + tooling (python3 = build-time PE patcher; p7zip = archives).
#    universe is enabled because Wine's deps (e.g. libfaudio0) live there on Ubuntu.
RUN dpkg --add-architecture i386 \
 && apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates wget gnupg p7zip-full python3 procps tini software-properties-common \
 && ( . /etc/os-release; [ "$ID" = "ubuntu" ] && add-apt-repository -y universe || true ) \
 && rm -rf /var/lib/apt/lists/*

# 2. WineHQ repository + Wine, matched to this image's distro/codename (the .NET base is
#    Ubuntu noble; the official per-distro .sources file selects the right URIs).
RUN mkdir -pm755 /etc/apt/keyrings \
 && wget -O /etc/apt/keyrings/winehq-archive.key https://dl.winehq.org/wine-builds/winehq.key \
 && . /etc/os-release \
 && wget -NP /etc/apt/sources.list.d/ "https://dl.winehq.org/wine-builds/${ID}/dists/${VERSION_CODENAME}/winehq-${VERSION_CODENAME}.sources" \
 && apt-get update \
 && if [ -n "${WINE_VERSION}" ]; then \
        PKGVER="${WINE_VERSION}~${VERSION_CODENAME}-1"; \
        apt-get install -y --install-recommends \
          "winehq-${WINE_BRANCH}=${PKGVER}" "wine-${WINE_BRANCH}=${PKGVER}" \
          "wine-${WINE_BRANCH}-amd64=${PKGVER}" "wine-${WINE_BRANCH}-i386=${PKGVER}"; \
    else \
        apt-get install -y --install-recommends "winehq-${WINE_BRANCH}"; \
    fi \
 && rm -rf /var/lib/apt/lists/*

# 3. initialize the 32-bit prefix headlessly (no winetricks, no xvfb)
RUN wineboot --init && wineserver -w

# 4. NEWER Windows APIs the QoL patch needs: the real Microsoft VC++ 2022 runtime DLLs.
#    (OLDER VC++6 comes from the game's bundled MSVCRT.dll + Tribes2.exe.local.)
#    The redist is a WiX "Burn" bundle; cabextract pulls its payload cabs, which hold the
#    runtime as *.dll_x86. We copy only the set the QoL patch needs into system32.
RUN apt-get update \
 && apt-get install -y --no-install-recommends cabextract \
 && rm -rf /var/lib/apt/lists/* \
 && wget -O /tmp/vc.exe "${VCREDIST_URL}" \
 && if [ -n "${VCREDIST_SHA256}" ]; then echo "${VCREDIST_SHA256}  /tmp/vc.exe" | sha256sum -c -; fi \
 && cabextract -q -d /tmp/ce /tmp/vc.exe \
 && for c in /tmp/ce/a*; do cabextract -q -d /tmp/vcr "$c" 2>/dev/null || true; done \
 && for n in concrt140 msvcp140 msvcp140_1 msvcp140_2 msvcp140_atomic_wait \
             msvcp140_codecvt_ids vcamp140 vccorlib140 vcomp140 vcruntime140; do \
        cp -f "/tmp/vcr/${n}.dll_x86" "${WINEPREFIX}/drive_c/windows/system32/${n}.dll"; \
    done \
 && apt-get purge -y cabextract && apt-get autoremove -y \
 && rm -rf /tmp/vc.exe /tmp/ce /tmp/vcr \
 && test -f "${WINEPREFIX}/drive_c/windows/system32/vcruntime140.dll"

# 5. extract the game; 7z root is GameData/, so extract into the parent (bind-mounted,
#    so the 453 MB archive never becomes an image layer).
RUN --mount=type=bind,source=content/tribesinstall.7z,target=/tmp/tribesinstall.7z \
    mkdir -p "${WINEPREFIX}/drive_c/Dynamix/Tribes2" \
 && 7z x -y /tmp/tribesinstall.7z -o"${WINEPREFIX}/drive_c/Dynamix/Tribes2" \
 && test -f "${GAME_DIR}/Tribes2.exe"

# 6. overlay the Tribes 2 NSIS patch payload (QoL in IFC22.dll; no Ruby)
RUN wget -O /tmp/tnext.exe "${PATCH_URL}" \
 && if [ -n "${PATCH_SHA256}" ]; then echo "${PATCH_SHA256}  /tmp/tnext.exe" | sha256sum -c -; fi \
 && mkdir -p /tmp/tn \
 && 7z x -y -xr'!*.nsis' /tmp/tnext.exe -o/tmp/tn >/dev/null \
 && rm -rf '/tmp/tn/$PLUGINSDIR' \
 && cp -rf /tmp/tn/. "${GAME_DIR}/" \
 && rm -rf /tmp/tnext.exe /tmp/tn \
 && test "$(stat -c%s "${GAME_DIR}/IFC22.dll")" -gt 1000000

# 7. python PE patcher (AllocConsole NOP + GUI->CUI) so the dedicated server is headless
COPY content/tribes_dual_patcher.py /opt/patcher/tribes_dual_patcher.py
RUN python3 /opt/patcher/tribes_dual_patcher.py --exe "${GAME_DIR}/Tribes2.exe" --backup \
 && python3 /opt/patcher/tribes_dual_patcher.py --exe "${GAME_DIR}/Tribes2.exe" --dry-run

# 8. the ASP.NET Core panel (PID 1; owns the game via the GameSupervisor worker)
COPY --from=app-build /app/publish /app/panel
RUN mkdir -p /data
WORKDIR /app/panel

# SERVER_RULESET selects the -mod parameter at runtime (empty/"base" => no -mod).
# Derived mod images override this (Classic, Construction); the panel can also set it
# during first-time setup, defaulting to this value.
ENV LAUNCH_PARAMS="-online -dedicated" \
    SERVER_RULESET="" \
    HTTP_PORT=8080 \
    HTTPS_PORT=8443 \
    PANEL_DB_PATH=/data/panel.db

EXPOSE 8080/tcp 8443/tcp 28000/udp

# tini reaps the Wine child tree; the .NET panel is the real PID-1 logic.
ENTRYPOINT ["/usr/bin/tini", "--", "dotnet", "/app/panel/TribesServerPanel.dll"]
