# syntax=docker/dockerfile:1
# Centurion — subtitle generation CLI in a container.
#
# Multi-arch: build with buildx for linux/amd64 and linux/arm64
#   (see .github/workflows/docker.yml). R2R is disabled on purpose: crossgen
#   under qemu emulation is slow and flaky; framework-dependent publish keeps
#   the app layer small because the runtime image carries the .NET runtime.
#
# Runtime behavior:
#   - WORKDIR is /data (mount your media here)
#   - ffmpeg / mkvtoolnix / aria2 are installed system-wide; Centurion finds
#     them on PATH (BinaryLocator falls back to PATH)
#   - model/tool caches go to /var/cache/centurion (XDG_CACHE_HOME) — mount a
#     volume to avoid re-downloading on every run
#   - configuration: /data/centurion.config.json or CENTURION_* env vars

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CENTURION_BUILD_NUMBER=0
WORKDIR /src
COPY Directory.Build.props Centurion.slnx ./
COPY src/ src/
RUN dotnet publish src/Centurion.Cli -c Release -r linux-x64 \
      --self-contained false \
      -o /app \
      -p:CenturionBuildNumber=${CENTURION_BUILD_NUMBER} \
      -p:PublishReadyToRun=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
         ffmpeg \
         mkvtoolnix \
         aria2 \
         ca-certificates \
         curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /data
COPY --from=build /app /opt/centurion

RUN mkdir -p /var/cache/centurion /var/log/centurion \
    && useradd --system --uid 1001 --home-dir /data centurion \
    && chown -R centurion:centurion /data /opt/centurion /var/cache/centurion /var/log/centurion

USER centurion
ENV XDG_CACHE_HOME=/var/cache/centurion \
    PATH="/opt/centurion:$PATH"

VOLUME ["/data", "/var/cache/centurion"]
ENTRYPOINT ["Centurion"]
CMD ["--help"]
