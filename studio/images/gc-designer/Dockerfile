# GameCore Studio designer worker image (docs/studio/04-etos-integration.md §4).
#
# A plain Debian image without the etos layer: the node layers `etos`, the entrypoint, the
# `agent` user, git configuration and the broker CA on top of it (etos image/layer/).
# Built and tagged localhost/gc-designer:current by studio/tools/host-build-images.sh.
ARG DEBIAN_IMAGE=public.ecr.aws/docker/library/debian:trixie-slim
FROM ${DEBIAN_IMAGE}

LABEL org.opencontainers.image.title="gc-designer" \
      org.opencontainers.image.description="GameCore Studio designer worker: configure/compose change sets, asset post-processing"

ENV LANG=C.UTF-8 \
    DEBIAN_FRONTEND=noninteractive

SHELL ["/bin/bash", "-euo", "pipefail", "-c"]

RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      bash coreutils procps less file tar xz-utils unzip ca-certificates curl \
      git tmux sudo tini \
      python3 python3-venv python-is-python3 jq \
      imagemagick ffmpeg \
 && rm -rf /var/lib/apt/lists/* /var/cache/apt/archives/*.deb \
 && python3 --version && jq --version \
 && { command -v magick || command -v convert; } && ffmpeg -hide_banner -version >/dev/null

WORKDIR /work
