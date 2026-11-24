# The aprscli image: run 'aprscli serve' as a gateway, or any other aprscli command.
#
#   docker build -t aprscli .
#   docker run -p 14580:14580 -p 14590:14590 aprscli serve -c YOURCALL -f "r/38.98/-94.67/300" \
#     --listen 0.0.0.0:14580 --http 0.0.0.0:14590
#
# See docs/guides/container.md. The release workflow builds it with VERSION set to the release.

# The base images are pinned by digest, so a build gets the same bases until Dependabot's docker update moves them.
# The build stage runs on the builder's own platform for every target: aprscli is framework-dependent IL, so the same
# output runs on amd64 and arm64, and an arm64 image builds without emulating the SDK.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d AS build
WORKDIR /src
COPY . .
ARG VERSION=0.0.0-dev
# MinVer reads git tags, which the build context leaves out, so the version comes from VERSION instead. No apphost:
# it would be built for the builder's platform, and the image starts aprscli with 'dotnet aprscli.dll' anyway.
RUN dotnet publish apps/AprsNet.AprsIsCli -c Release -o /app \
    -p:MinVerSkip=true -p:Version=${VERSION} -p:PackAsTool=false -p:UseAppHost=false

# Chiseled: only the .NET runtime and what it needs, with no shell, package manager or coreutils, so the image carries
# none of the OS packages aprscli never runs (util-linux, tar, dash, gnutls, systemd libraries...) or their CVEs. It
# runs in .NET's invariant globalization mode, as aprscli formats everything with the invariant culture anyway, and
# sets the non-root user's APP_UID. Every command below is exec form, so nothing needs a shell.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:1b234cef150624fe8cc7d027e8d742c0f03a16aef2484dc469eb8c872167255e
WORKDIR /app
COPY --from=build /app .
# The image's non-root user.
USER $APP_UID
# 14580: APRS-IS clients. 14590: WebSocket clients and /health, with --http 0.0.0.0:14590.
EXPOSE 14580 14590
# 'listen' and 'serve' keep this file current while they receive (listen: each line, keepalives included; serve: while
# any source is up), and the HEALTHCHECK fails once it is older than 90s. One-shot commands ignore it. If you change
# the variable, change the HEALTHCHECK's path too.
ENV APRSCLI_HEALTH_FILE=/tmp/aprscli.health
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "/app/aprscli.dll", "health", "--file", "/tmp/aprscli.health"]
ENTRYPOINT ["dotnet", "/app/aprscli.dll"]
CMD ["--help"]
