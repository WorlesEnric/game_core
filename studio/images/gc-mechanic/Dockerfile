# GameCore Studio mechanic worker image (docs/studio/04-etos-integration.md §4): gc-designer plus
# the .NET 8 SDK and a NuGet cache pre-restored for a netstandard2.1 class library and its
# NUnit test project, so `dotnet build` / `dotnet test` work in an offline task container.
#
# Built and tagged localhost/gc-mechanic:current by studio/tools/host-build-images.sh (after
# gc-designer). The SDK is copied from Microsoft's SDK image; DOTNET_SDK_IMAGE is pinned by
# digest in that script and recorded in studio/etos/etos.lock.
ARG DESIGNER_IMAGE=localhost/gc-designer:current
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
FROM ${DOTNET_SDK_IMAGE} AS sdk

FROM ${DESIGNER_IMAGE}

LABEL org.opencontainers.image.title="gc-mechanic" \
      org.opencontainers.image.description="GameCore Studio mechanic worker: mechanism packages built and tested with .NET 8"

SHELL ["/bin/bash", "-euo", "pipefail", "-c"]

# The SDK's native dependencies on Debian trixie.
RUN apt-get update \
 && apt-get install -y --no-install-recommends \
      libicu76 libssl3t64 zlib1g libgcc-s1 libstdc++6 tzdata \
 && rm -rf /var/lib/apt/lists/* /var/cache/apt/archives/*.deb

COPY --from=sdk /usr/share/dotnet /usr/share/dotnet

# A machine-wide, world-readable package cache: the task's `agent` user (created by the etos
# layer) restores from it without network access.
ENV DOTNET_ROOT=/usr/share/dotnet \
    PATH=/usr/share/dotnet:${PATH} \
    NUGET_PACKAGES=/opt/nuget/packages \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=false

COPY prewarm/ /opt/gc-prewarm/

RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet \
 && dotnet --info >/dev/null \
 && cd /opt/gc-prewarm \
 && dotnet test Prewarm.Tests/Prewarm.Tests.csproj -c Release \
 && rm -rf Prewarm/bin Prewarm/obj Prewarm.Tests/bin Prewarm.Tests/obj /root/.dotnet /root/.local/share/NuGet /tmp/* \
 && chmod -R a+rX /opt/nuget /opt/gc-prewarm

WORKDIR /work
