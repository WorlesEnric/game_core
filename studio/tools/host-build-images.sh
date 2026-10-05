#!/usr/bin/env bash
# Build the GameCore Studio container images on the Linux host (docs/studio/04-etos-integration.md §8.3).
#
#   studio/tools/host-build-images.sh          (run on the host, from the game_core checkout)
#
#   localhost/etos-default:latest   etos image/default (the node's default task image)
#   localhost/gc-designer:current   studio/images/gc-designer
#   localhost/gc-mechanic:current   studio/images/gc-mechanic (FROM gc-designer)
#
# The node adds the etos layer on top of each at task start. Builds use Docker's cache, so a
# second run rebuilds nothing; each image reports whether its id changed. The build containers
# reach package mirrors through the host's local HTTP proxy when one listens on 127.0.0.1:7897.
set -euo pipefail

HERE="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
IMAGES="$HERE/../images"
SRC="${ETOS_STUDIO_SRC:-$HOME/wkspace/etos-studio}"
# Pinned by digest (the manifest list of mcr.microsoft.com/dotnet/sdk:8.0 on 2026-10-04; SDK 8.0.425).
DOTNET_SDK_IMAGE="${DOTNET_SDK_IMAGE:-mcr.microsoft.com/dotnet/sdk:8.0@sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743}"

# No provenance attestation: it carries the build time, so the image id would change on every
# run even when every layer comes from the cache.
build_args=(--network host --provenance=false --sbom=false)
if ss -ltn 2>/dev/null | grep -q '127.0.0.1:7897 '; then
    for v in http_proxy https_proxy HTTP_PROXY HTTPS_PROXY; do
        build_args+=(--build-arg "$v=http://127.0.0.1:7897")
    done
    build_args+=(--build-arg "no_proxy=localhost,127.0.0.1" --build-arg "NO_PROXY=localhost,127.0.0.1")
fi

id_of() { docker image inspect --format '{{.Id}}' "$1" 2>/dev/null || echo none; }
build() { # tag context [args...]
    local tag="$1" ctx="$2"; shift 2
    local before after log
    before="$(id_of "$tag")"
    log="$(mktemp)"
    echo "== $tag"
    if ! docker build "${build_args[@]}" "$@" -t "$tag" "$ctx" >"$log" 2>&1; then
        tail -n 40 "$log"; rm -f "$log"
        echo "image build failed: $tag" >&2
        exit 1
    fi
    rm -f "$log"
    after="$(id_of "$tag")"
    if [ "$before" = "$after" ]; then echo "  unchanged ($after)"; else echo "  built ($after)"; fi
}

build localhost/etos-default:latest "$SRC/image/default" -f "$SRC/image/default/Containerfile"
build localhost/gc-designer:current "$IMAGES/gc-designer"
build localhost/gc-mechanic-base:current "$IMAGES/gc-mechanic" \
    --build-arg DESIGNER_IMAGE=localhost/gc-designer:current \
    --build-arg DOTNET_SDK_IMAGE="$DOTNET_SDK_IMAGE"
build localhost/gc-mechanic:current "$HERE/../etos/workers" \
    --build-arg MECHANIC_IMAGE=localhost/gc-mechanic-base:current

echo "== smoke"
docker run --rm localhost/gc-designer:current bash -c 'python3 --version && jq --version && git --version && ffmpeg -hide_banner -version | sed -n 1p && { magick -version 2>/dev/null || convert -version; } | sed -n 1p'
docker run --rm --network none localhost/gc-mechanic:current bash -c 'dotnet --version && ls /opt/nuget/packages | tr "\n" " " && echo'
