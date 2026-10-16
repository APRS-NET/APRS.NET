# The aprscli image: run 'aprscli serve' as a gateway, or any other aprscli command.
#
#   docker build -t aprscli .
#   docker run -p 14580:14580 -p 14590:14590 aprscli serve -c YOURCALL -f "r/38.98/-94.67/300" \
#     --listen 0.0.0.0:14580 --http 0.0.0.0:14590
#
# See docs/guides/container.md. The release workflow builds it with VERSION set to the release.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
ARG VERSION=0.0.0-dev
# MinVer reads git tags, which the build context leaves out, so the version comes from VERSION instead.
RUN dotnet publish apps/AprsNet.AprsIsCli -c Release -o /app \
    -p:MinVerSkip=true -p:Version=${VERSION} -p:PackAsTool=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
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
