#!/usr/bin/env bash
# vendor-etos-sdk.sh <etos-checkout>
#
# Refresh studio/agent/vendor/etos-sdk/ from the etos Rust SDK (sdk/rust, Apache-2.0) at the
# commit named in studio/etos/etos.lock (a line `commit = "<sha>"`, `etos_commit=<sha>` or
# `commit: <sha>`; the first one wins), or the checkout's HEAD when the lock is absent or names
# no commit. ETOS_COMMIT=<rev> overrides both. The copy is taken from the commit itself
# (`git archive`), never from the working tree, so local edits in the checkout do not leak in.
#
# Copied: src/, Cargo.toml, README.md, and LICENSE*/NOTICE* from sdk/rust or the repository
# root when present. Left out: examples/, tests/, target/, and in Cargo.toml the
# [dev-dependencies] and [[example]] sections (they name files that are not vendored).
# Rewritten: vendor/README.md (source, commit, licence, what was copied) and ETOS_PIN.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$here/../.." && pwd)"
checkout="${1:-}"
[ -n "$checkout" ] || { echo "usage: $0 <etos-checkout>" >&2; exit 2; }
checkout="$(cd "$checkout" && pwd)"
git -C "$checkout" rev-parse --git-dir >/dev/null 2>&1 || { echo "$checkout is not a git checkout" >&2; exit 2; }

lock="$repo_root/studio/etos/etos.lock"
want="${ETOS_COMMIT:-}"
source_of_rev="ETOS_COMMIT"
if [ -z "$want" ] && [ -f "$lock" ]; then
    want="$(sed -nE 's/^[[:space:]]*(etos_)?commit[[:space:]]*[=:][[:space:]]*"?([0-9a-fA-F]{7,40})"?.*$/\2/p' "$lock" | head -n 1)"
    source_of_rev="studio/etos/etos.lock"
fi
if [ -z "$want" ]; then
    want="HEAD"
    source_of_rev="the checkout's HEAD (no studio/etos/etos.lock commit)"
fi
commit="$(git -C "$checkout" rev-parse --verify "$want^{commit}")" ||
    { echo "commit $want is not in $checkout (fetch it first)" >&2; exit 1; }
git -C "$checkout" cat-file -e "$commit:sdk/rust/Cargo.toml" 2>/dev/null ||
    { echo "$commit has no sdk/rust/Cargo.toml" >&2; exit 1; }

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
git -C "$checkout" archive "$commit" sdk/rust | tar -x -C "$tmp"
for f in LICENSE LICENSE-APACHE LICENSE.md LICENSE.txt NOTICE; do
    if git -C "$checkout" cat-file -e "$commit:$f" 2>/dev/null && [ ! -e "$tmp/sdk/rust/$f" ]; then
        git -C "$checkout" show "$commit:$f" >"$tmp/sdk/rust/$f"
    fi
done

dest="$here/vendor/etos-sdk"
rm -rf "$dest"
mkdir -p "$dest"
cp -R "$tmp/sdk/rust/src" "$dest/src"
cp "$tmp/sdk/rust/README.md" "$dest/README.md"
copied="src/, Cargo.toml (trimmed), README.md"
for f in LICENSE LICENSE-APACHE LICENSE.md LICENSE.txt NOTICE; do
    if [ -f "$tmp/sdk/rust/$f" ]; then
        cp "$tmp/sdk/rust/$f" "$dest/$f"
        copied="$copied, $f"
    fi
done
# Cargo.toml without [dev-dependencies] and [[example]] (a section runs to the next header).
awk '
    /^\[\[?[^]]+\]\]?[[:space:]]*$/ { skip = ($0 ~ /^\[dev-dependencies\]/ || $0 ~ /^\[\[example\]\]/) }
    !skip { print }
' "$tmp/sdk/rust/Cargo.toml" >"$dest/Cargo.toml"

licence="$(sed -nE 's/^license[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$dest/Cargo.toml" | head -n 1)"
version="$(sed -nE 's/^version[[:space:]]*=[[:space:]]*"([^"]+)".*/\1/p' "$dest/Cargo.toml" | head -n 1)"
subject="$(git -C "$checkout" log -1 --format='%s' "$commit")"
date="$(git -C "$checkout" log -1 --format='%cI' "$commit")"

cat >"$here/vendor/README.md" <<EOF
# Vendored: etos Rust SDK

\`vendor/etos-sdk/\` is a copy of \`sdk/rust\` from the **etos** repository, used by
\`studio/agent\` as \`etos-sdk = { path = "vendor/etos-sdk" }\`. Do not edit it by hand:
refresh it with \`studio/agent/vendor-etos-sdk.sh <etos-checkout>\`.

| | |
|---|---|
| Source repository | the etos repository, directory \`sdk/rust\` |
| Commit | \`$commit\` |
| Commit subject | $subject |
| Commit date | $date |
| Selected by | $source_of_rev |
| Crate | \`etos-sdk\` $version |
| Licence | $licence (declared in the crate's \`Cargo.toml\`$( [ -e "$dest/LICENSE" ] || [ -e "$dest/LICENSE-APACHE" ] || echo "; the repository carries no LICENSE file at this commit" )) |
| Copied | $copied |
| Left out | \`examples/\`, \`tests/\`, \`target/\`, \`clippy.toml\`, \`.gitignore\`; the \`[dev-dependencies]\` and \`[[example]]\` sections of \`Cargo.toml\` |
EOF

printf '%s\n' "$commit" >"$here/ETOS_PIN"
echo "vendored etos-sdk $version at $commit into $dest"
