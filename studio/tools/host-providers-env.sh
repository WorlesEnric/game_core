#!/usr/bin/env bash
# Generate etosd's provider environment file on the Linux host (SADR-018).
#
#   studio/tools/host-providers-env.sh        (run on the host)
#
# Reads the `export ECHO_API_KEY`, `BAILIAN_API_KEY` and `DEEPSEEK_API_KEY` lines of ~/.bashrc
# (they sit after the file's early return for non-interactive shells, so the file is not
# sourced) and writes them as KEY=value lines to ~/.config/gamecore-studio/providers.env with
# mode 0600. Never prints, logs or copies a value anywhere else; reports only names and whether
# the file changed. Exit status 3 means the file changed (install.sh then restarts etosd).
set -euo pipefail
umask 077

SRC="${PROVIDERS_SOURCE:-$HOME/.bashrc}"
DIR="$HOME/.config/gamecore-studio"
OUT="$DIR/providers.env"
NAMES=(ECHO_API_KEY BAILIAN_API_KEY DEEPSEEK_API_KEY)

mkdir -p "$DIR"
chmod 0700 "$DIR"
tmp="$(mktemp "$DIR/.providers.env.XXXXXX")"
trap 'rm -f "$tmp"' EXIT
(
    # Only `export NAME=…` lines for the three names are evaluated, in a subshell.
    eval "$(grep -E '^export (ECHO|BAILIAN|DEEPSEEK)_API_KEY[=]' "$SRC")"
    for n in "${NAMES[@]}"; do
        v="${!n:-}"
        if [ -z "$v" ]; then
            echo "host-providers-env: $n is not set in $SRC" >&2
            continue
        fi
        case "$v" in *[[:space:]\"\'\\]*) echo "host-providers-env: $n has characters an EnvironmentFile cannot hold" >&2; exit 1 ;; esac
        printf '%s=%s\n' "$n" "$v"
    done
) > "$tmp"
chmod 0600 "$tmp"
present="$(cut -d= -f1 "$tmp" | tr '\n' ' ')"
if cmp -s "$tmp" "$OUT"; then
    echo "providers.env unchanged ($OUT, mode $(stat -c %a "$OUT"); names: $present)"
    exit 0
fi
mv "$tmp" "$OUT"
trap - EXIT
echo "providers.env written ($OUT, mode 0600; names: $present)"
exit 3
