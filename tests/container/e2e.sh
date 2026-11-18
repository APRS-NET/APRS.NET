#!/usr/bin/env bash
# End-to-end test of an aprscli image as a station monitor, offline (see compose.yaml):
#
#   tests/container/e2e.sh IMAGE VERSION
#
# A fake APRS-IS server sends 20 packets from a corpus capture; 'aprscli listen --log-packets -q -n 20' exports them over
# OTLP to an OpenTelemetry Collector, which writes them to a file. The test then checks the collector got a log record
# for each packet, with its Source, and the AprsNet.* metrics, all from service aprscli at VERSION.
set -euo pipefail

image="${1:?usage: e2e.sh IMAGE VERSION}"
version="${2:?usage: e2e.sh IMAGE VERSION}"

here="$(cd "$(dirname "$0")" && pwd)"
fixture="$here/../corpus/aprs-is/2026-10-12-sf-r30km.log"
export APRSCLI_IMAGE="$image" PACKETS=20 FILTER="r/37.77/-122.42/30"
OUT_DIR="$(mktemp -d)"
export OUT_DIR
chmod 0777 "$OUT_DIR" # the collector writes as its own non-root user
compose=(docker compose -f "$here/compose.yaml")
fail() { echo "::error::$*" >&2; exit 1; }
cleanup() {
  "${compose[@]}" logs --no-color > "$OUT_DIR/compose.log" 2>&1 || true
  if [ "${status:-1}" -ne 0 ]; then cat "$OUT_DIR/compose.log"; fi
  "${compose[@]}" down --volumes --timeout 5 > /dev/null 2>&1 || true
  rm -rf "$OUT_DIR"
}
trap 'status=$?; cleanup' EXIT

"${compose[@]}" up -d aprs-is collector
# Not 'logs | grep -q': grep stops at the first match, and with pipefail the broken pipe fails the pipeline.
ready() { grep -q 'Everything is ready' <<< "$("${compose[@]}" logs collector 2>&1)"; }
for _ in $(seq 60); do
  ready && break
  sleep 1
done
ready || fail "the collector didn't start"

echo "--- aprscli listen"
"${compose[@]}" run --rm --no-deps aprscli || fail "aprscli listen exited with $?"

# Stopping the collector flushes the file exporter.
"${compose[@]}" stop --timeout 10 collector > /dev/null
otel="$OUT_DIR/otel.jsonl"
[ -s "$otel" ] || fail "the collector wrote nothing"

echo "--- login"
cat "$OUT_DIR/login.txt"
grep -q "vers aprscli $version filter $FILTER" "$OUT_DIR/login.txt" || fail "unexpected login line"

echo "--- resource"
# Every batch must come from aprscli at the version under test.
jq -e -s --arg v "$version" '
  [.[] | (.resourceLogs // .resourceMetrics)[] | .resource.attributes
    | map({(.key): .value.stringValue}) | add
    | select(.["service.name"] == "aprscli" and .["service.version"] == $v and .["service.instance.id"] == "e2e")]
  | length > 0' "$otel" > /dev/null || fail "no telemetry from service aprscli $version"

echo "--- packet logs"
# The packets as the fake server sent them, and their sources, in order.
mapfile -t sources < <(grep . "$fixture" | head -n "$PACKETS" | cut -d ' ' -f 2- | cut -d '>' -f 1)
mapfile -t logged < <(jq -r '
  .resourceLogs[]?.scopeLogs[] | select(.scope.name == "aprscli.packets") | .logRecords[]
  | (.attributes | map({(.key): .value.stringValue}) | add) | .Source' "$otel")
echo "${#logged[@]} packet log records; first sources: ${logged[*]:0:5}"
[ "${#logged[@]}" -eq "$PACKETS" ] || fail "expected $PACKETS packet log records, got ${#logged[@]}"
[ "${logged[*]}" = "${sources[*]}" ] || fail "the logged sources differ from the packets sent"
jq -e -s --arg s "${sources[0]}" '
  [.[] | .resourceLogs[]?.scopeLogs[] | select(.scope.name == "aprscli.packets") | .logRecords[]
    | select(.body.stringValue | startswith("Packet from \($s): \($s)>"))] | length > 0' "$otel" > /dev/null \
  || fail "no log record with the formatted packet line"

echo "--- metrics"
jq -r '.resourceMetrics[]?.scopeMetrics[] | .scope.name + " " + (.metrics[].name)' "$otel" | sort -u
# listen reads lines, not parsed packets, so its packet count is lines.received{kind=packet} (cumulative: take the last).
received="$(jq -s '
  [.[] | .resourceMetrics[]?.scopeMetrics[] | select(.scope.name == "AprsNet.AprsIsTcpClient") | .metrics[]
    | select(.name == "aprsnet.aprsis.lines.received") | .sum.dataPoints[]
    | select(any(.attributes[]; .key == "kind" and .value.stringValue == "packet")) | .asInt | tonumber] | max // 0' \
  "$otel")"
echo "aprsnet.aprsis.lines.received{kind=packet} = $received"
[ "$received" -eq "$PACKETS" ] || fail "aprsnet.aprsis.lines.received{kind=packet} is $received, expected $PACKETS"
jq -e -s '[.[] | .resourceMetrics[]?.scopeMetrics[].metrics[] | select(.name == "aprsnet.aprsis.login.results")]
  | length > 0' "$otel" > /dev/null || fail "no aprsnet.aprsis.login.results metric"

echo "End-to-end test passed."
