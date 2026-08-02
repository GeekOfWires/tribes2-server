#!/bin/sh
# Ensure each ruleset's prefs/serverprefs.cs carries the build-time defaults.
# Always applied: $Host::Linux = 1; (flags the dedicated server as running on Linux/Wine).
# Extra defaults can be passed with --pref; those apply ONLY to the rulesets named on the
# command line (e.g. Construction's PureBuild), never to the blanket sweep.
#
# Usage: set-serverprefs-defaults.sh <GAME_DIR> [--pref '$Host::Foo = 1;']... [ruleset ...]
#   * guarantees prefs/serverprefs.cs exists (with the defaults) for each <ruleset>,
#   * and also applies the always-on defaults to any other */prefs/serverprefs.cs.
# Idempotent per setting: a $Host:: variable already present in the file is left alone, so an
# operator's own value is never overwritten on rebuild.
set -eu

GAME_DIR="$1"; shift

BASE_PREFS='$Host::Linux = 1;'
EXTRA_PREFS=''

while [ $# -gt 0 ]; do
    case "$1" in
        --pref) EXTRA_PREFS="${EXTRA_PREFS}$2
"; shift 2 ;;
        *) break ;;
    esac
done

# Append one pref statement unless its $Host:: variable is already defined in the file.
apply() {
    f="$1"; line="$2"
    var=$(printf '%s' "$line" | sed -n 's/^[[:space:]]*\(\$Host::[A-Za-z0-9_:]*\).*/\1/p')
    [ -n "$var" ] || return 0
    # Fixed-string match on the variable name so "$Host::Linux" can't match "$Host::LinuxFoo".
    if grep -qF "$var" "$f" 2>/dev/null; then
        return 0
    fi
    if [ "$header_done" = 0 ]; then
        printf '\n// container defaults\n' >> "$f"
        header_done=1
    fi
    printf '%s\n' "$line" >> "$f"
    echo "  set $var -> ${f##*/}"
}

# ensure <prefs-dir> <prefs-block>
ensure() {
    d="$1"; block="$2"
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
    if [ -z "$f" ]; then
        f="$d/serverprefs.cs"
        : > "$f"
    fi
    header_done=0
    old_ifs=$IFS
    IFS='
'
    for line in $block; do
        [ -n "$line" ] && apply "$f" "$line"
    done
    IFS=$old_ifs
}

# Requested rulesets get the always-on defaults PLUS any --pref extras.
for r in "$@"; do
    ensure "$GAME_DIR/$r/prefs" "$BASE_PREFS
$EXTRA_PREFS"
done

# Every other ruleset gets only the always-on defaults.
for d in "$GAME_DIR"/*/prefs; do
    [ -d "$d" ] && ensure "$d" "$BASE_PREFS"
done
