# The aprscli image: run 'aprscli serve' as a gateway, or any other aprscli command.
#
#   docker build -t aprscli .
#   docker run -p 14580:14580 -p 14590:14590 aprscli serve -c YOURCALL -f "r/38.98/-94.67/300" \
#     --listen 0.0.0.0:14580 --http 0.0.0.0:14590
#
# See docs/guides/container.md. The release workflow builds it with VERSION set to the release.

# The base images are pinned by digest, so a build gets the same bases until Dependabot's docker update moves them.
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
WORKDIR /src
COPY . .
ARG VERSION=0.0.0-dev
# MinVer reads git tags, which the build context leaves out, so the version comes from VERSION instead.
RUN dotnet publish apps/AprsNet.AprsIsCli -c Release -o /app \
    -p:MinVerSkip=true -p:Version=${VERSION} -p:PackAsTool=false

FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:80a51954ac70925e5783cb1ed35129a4243dcb1cddef20066b67346e17db284d
WORKDIR /app
COPY --from=build /app .
# The image's non-root user.
USER $APP_UID
# 14580: APRS-IS clients. 14590: WebSocket clients and /health, with --http 0.0.0.0:14590.
EXPOSE 14580 14590
# Asks the gateway's /health, so it needs 'serve --http 0.0.0.0:14590' (or 127.0.0.1:14590).
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD ["dotnet", "/app/aprscli.dll", "health", "http://127.0.0.1:14590/health"]
ENTRYPOINT ["dotnet", "/app/aprscli.dll"]
CMD ["--help"]
