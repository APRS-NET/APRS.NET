# Dire Wolf from Debian, as a KISS TCP TNC on port 8001.
FROM debian:trixie-slim
RUN apt-get update \
    && apt-get install -y --no-install-recommends direwolf \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --groups audio direwolf
USER direwolf
EXPOSE 8001
