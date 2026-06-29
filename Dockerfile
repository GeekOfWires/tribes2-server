# syntax=docker/dockerfile:1.7
###############################################################################
# Tribes 2 dedicated server + ASP.NET Core 10 control panel (single image).
#
# The panel is PID 1 and owns the game lifecycle via a hosted Worker Service
# (GameSupervisor). Build ordering:
#   1. Debian base + i386 multiarch
#   2. WineHQ (32-bit components)
#   3. winetricks runtimes (MSVC6 era + modern UCRT)
#   4. extract tribesinstall.7z so GameData lands at C:\Dynamix\Tribes2\GameData
#   5. overlay the Tribes 2 NSIS patch payload (URL is a build ARG = the variable)
#   6. run the python PE patcher over Tribes2.exe (AllocConsole NOP + GUI->CUI)
#   7. ship the ASP.NET Core panel (with the built React SPA) as the entrypoint
###############################################################################

# ---------------------------------------------------------------- React SPA build
FROM node:22-bookworm-slim AS spa-build
WORKDIR /spa
COPY panel/ClientApp/package*.json ./
RUN npm install
COPY panel/ClientApp/ ./
RUN npm run build           # vite outDir ../wwwroot -> /wwwroot

# ------------------------------------------------ ASP.NET Core publish (self-contained)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS app-build
WORKDIR /src
COPY panel/TribesServerPanel.csproj ./
RUN dotnet restore -r linux-x64
COPY panel/ ./
COPY --from=spa-build /wwwroot ./wwwroot
RUN dotnet publish -c Release -r linux-x64 --self-contained true -o /app/publish

# ---------------------------------------------------------------- runtime image
FROM debian:trixie-slim AS runtime

# The Tribes 2 QoL patch (the "variable"). NSIS installer .exe; 7z extracts its
# payload deterministically, so we overlay files without running the GUI installer.
ARG PATCH_URL="https://www.tribesnext.com/files/TribesNEXT_20250922_preview.exe"
ARG PATCH_SHA256=""
ARG WINE_BRANCH=stable             # stable | staging | devel

ENV DEBIAN_FRONTEND=noninteractive \
    WINEPREFIX=/opt/wineprefix \
    WINEARCH=win32 \
    WINEDEBUG=-all \
    GAME_DIR=/opt/wineprefix/drive_c/Dynamix/Tribes2/GameData

# 1. base tooling + i386 multiarch (python3 is used by the build-time PE patcher)
RUN dpkg --add-architecture i386 \
 && apt-get update \
 && apt-get install -y --no-install-recommends \
      ca-certificates wget gnupg p7zip-full cabextract \
      python3 procps tini libstdc++6 libssl3 zlib1g \
 && rm -rf /var/lib/apt/lists/*

# 2. WineHQ repository (amd64 + i386) and Wine
RUN mkdir -p /etc/apt/keyrings \
 && wget -qO- https://dl.winehq.org/wine-builds/winehq.key | gpg --dearmor -o /etc/apt/keyrings/winehq.gpg \
 && . /etc/os-release \
 && printf 'Types: deb\nURIs: https://dl.winehq.org/wine-builds/debian/\nSuites: %s\nComponents: main\nArchitectures: amd64 i386\nSigned-By: /etc/apt/keyrings/winehq.gpg\n' "$VERSION_CODENAME" \
      > /etc/apt/sources.list.d/winehq.sources \
 && apt-get update \
 && apt-get install -y --install-recommends winehq-${WINE_BRANCH} \
 && rm -rf /var/lib/apt/lists/*

# 3. winetricks (upstream) + prefix init + runtimes.
#    winetricks runs GUI redistributable installers, so these steps need a virtual
#    display -- BUT ONLY AT BUILD TIME. xvfb is a build dependency here; the runtime
#    game server is headless via the PE patch (step 6) and never uses xvfb.
RUN apt-get update && apt-get install -y --no-install-recommends xvfb xauth \
 && rm -rf /var/lib/apt/lists/* && mkdir -p /tmp/xdg && chmod 700 /tmp/xdg
ENV XDG_RUNTIME_DIR=/tmp/xdg
RUN wget -qO /usr/local/bin/winetricks https://raw.githubusercontent.com/Winetricks/winetricks/master/src/winetricks \
 && chmod +x /usr/local/bin/winetricks
RUN xvfb-run -a wineboot --init && wineserver -w
# vcrun6 -> MSVC6-era runtime the 2002 engine links against;
# vcrun2015 -> UCRT/vcruntime140 surface for the rebuilt IFC22.dll loader
RUN xvfb-run -a winetricks -q --force win7 vcrun6 vcrun2015 && wineserver -w
RUN xvfb-run -a wine reg add 'HKCU\Software\Wine\DllOverrides' /v msvcrt /d native,builtin /f \
 && wineserver -w

# 4. extract the game; 7z root is GameData/, so extract into the parent. Bind-mounted
#    (not COPYed) so the 453 MB archive never becomes an image layer.
RUN --mount=type=bind,source=content/tribesinstall.7z,target=/tmp/tribesinstall.7z \
    mkdir -p "${WINEPREFIX}/drive_c/Dynamix/Tribes2" \
 && 7z x -y /tmp/tribesinstall.7z -o"${WINEPREFIX}/drive_c/Dynamix/Tribes2" \
 && test -f "${GAME_DIR}/Tribes2.exe"

# 5. overlay the Tribes 2 NSIS patch payload
RUN wget -O /tmp/tnext.exe "${PATCH_URL}" \
 && if [ -n "${PATCH_SHA256}" ]; then echo "${PATCH_SHA256}  /tmp/tnext.exe" | sha256sum -c -; fi \
 && mkdir -p /tmp/tn \
 && 7z x -y -xr'!*.nsis' /tmp/tnext.exe -o/tmp/tn \
 && rm -rf '/tmp/tn/$PLUGINSDIR' \
 && cp -rf /tmp/tn/. "${GAME_DIR}/" \
 && rm -rf /tmp/tnext.exe /tmp/tn \
 && test "$(stat -c%s "${GAME_DIR}/IFC22.dll")" -gt 1000000   # patched IFC22.dll ~2 MB

# 6. python PE patcher (AllocConsole NOP + GUI->CUI) so the dedicated server is headless
COPY content/tribes_dual_patcher.py /opt/patcher/tribes_dual_patcher.py
RUN python3 /opt/patcher/tribes_dual_patcher.py --exe "${GAME_DIR}/Tribes2.exe" --backup \
 && python3 /opt/patcher/tribes_dual_patcher.py --exe "${GAME_DIR}/Tribes2.exe" --dry-run

# 7. ASP.NET Core panel (PID 1; owns the game via the GameSupervisor worker)
COPY --from=app-build /app/publish /app/panel
RUN mkdir -p /data && chmod +x /app/panel/TribesServerPanel
WORKDIR /app/panel

ENV LAUNCH_PARAMS="-online -dedicated" \
    TELNET_PORT=23000 \
    HTTP_PORT=8080 \
    HTTPS_PORT=8443 \
    PANEL_DB_PATH=/data/panel.db

EXPOSE 8080/tcp 8443/tcp 28000/udp

# tini reaps the Wine child tree; the .NET panel is the real PID-1 logic.
ENTRYPOINT ["/usr/bin/tini", "--", "/app/panel/TribesServerPanel"]
