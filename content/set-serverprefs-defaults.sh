#!/bin/sh
# Ensure each ruleset's prefs/serverprefs.cs carries the build-time defaults.
# Currently: $Host::Linux = 1; (flags the dedicated server as running on Linux/Wine).
#
# Usage: set-serverprefs-defaults.sh <GAME_DIR> [ruleset ...]
#   * guarantees prefs/serverprefs.cs exists (with the default) for each <ruleset>,
#   * and also updates any other existing */prefs/serverprefs.cs under <GAME_DIR>.
# Idempotent: never duplicates a line it already added.
set -eu

GAME_DIR="$1"; shift
LINE='$Host::Linux = 1;'

ensure() {
    d="$1"
    mkdir -p "$d"
    # Wine's filesystem is case-INSENSITIVE, so serverPrefs.cs and serverprefs.cs are the same
    # file to the engine. A mod may ship its real config as "serverPrefs.cs" (capital P); we
    # must edit THAT file, not create a second casing. Two files differing only in case make
    # Wine read an ambiguous/wrong one -- e.g. a stub carrying only $Host::Linux would shadow
    # the mod's real serverPrefs.cs and drop $Host::Dedicated, launching a headless client.
    f=""
    for cand in "$d"/[Ss][Ee][Rr][Vv][Ee][Rr][Pp][Rr][Ee][Ff][Ss].cs; do
        [ -f "$cand" ] && { f="$cand"; break; }
    done
    if [ -n "$f" ]; then
        if grep -q 'Host::Linux' "$f"; then
            return 0
        fi
        printf '\n// container default\n%s\n' "$LINE" >> "$f"
    else
        f="$d/serverprefs.cs"
        printf '// container default\n%s\n' "$LINE" > "$f"
    fi
    echo "serverprefs default set: $f"
}

# Guarantee the requested rulesets (creates the prefs dir + file if missing).
for r in "$@"; do
    ensure "$GAME_DIR/$r/prefs"
done

# Sweep any other prefs/ folders that already exist under GameData.
for d in "$GAME_DIR"/*/prefs; do
    [ -d "$d" ] && ensure "$d"
done
