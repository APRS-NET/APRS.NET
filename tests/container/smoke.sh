#!/usr/bin/env bash
# Smoke-tests a built aprscli image without a network: the version it was built with, the commands' help, a parse of a
# packet from the corpus, the non-root user and the image's size.
#
#   tests/container/smoke.sh IMAGE VERSION [PLATFORM]
#
# PLATFORM (linux/amd64, linux/arm64) runs the image as that platform, under QEMU when it isn't the host's. Only the
# commands that run aprscli are repeated per platform; the size budget is checked for the image as given.
set -euo pipefail

image="${1:?usage: smoke.sh IMAGE VERSION [PLATFORM]}"
version="${2:?usage: smoke.sh IMAGE VERSION [PLATFORM]}"
platform="${3:-}"

# The image's uncompressed size: the sum of its layers, as 'docker history' reports them. 'docker image ls' shows more
# with the containerd image store, which also counts the compressed layers it keeps, so it isn't used here.
# The budgets leave ~28 MB over what the .NET 10 runtime image and aprscli (~6 MB) took in November 2026: 222 MB on
# amd64 and 251 MB on arm64, whose Ubuntu base layer is 22 MB larger. A jump past one means something unintended got
# into the image; raise them only on purpose.
case "$(docker image inspect --format '{{.Architecture}}' "$image")" in
  arm64) max_bytes="${MAX_IMAGE_BYTES:-280000000}" ;;
  *) max_bytes="${MAX_IMAGE_BYTES:-250000000}" ;;
esac

here="$(cd "$(dirname "$0")" && pwd)"
fixture="$here/../corpus/aprs-is/2026-10-12-sf-r30km.log"
run=(docker run --rm ${platform:+--platform "$platform"})
fail() { echo "::error::$*" >&2; exit 1; }

echo "--- --version"
got="$("${run[@]}" "$image" --version)"
echo "$got"
[ "$got" = "$version" ] || fail "--version printed '$got', expected '$version'"

echo "--- listen --help, serve --help"
# Captured first: grep -q stops reading at the first match, and with pipefail the writer's broken pipe would fail.
help="$("${run[@]}" "$image" listen --help)"
grep -q -- '--log-packets' <<< "$help" || fail "listen --help doesn't list --log-packets"
help="$("${run[@]}" "$image" serve --help)"
grep -q -- '--listen' <<< "$help" || fail "serve --help doesn't list --listen"

echo "--- parse --format json"
# The first packet of a corpus capture, without its timestamp; json gives fields the test can check exactly.
line="$(head -n 1 "$fixture" | cut -d ' ' -f 2-)"
source="${line%%>*}"
json="$("${run[@]}" "$image" parse --format json "$line")"
echo "$json"
echo "$json" | jq -e --arg source "$source" '.source == $source' > /dev/null \
  || fail "parse didn't give source $source"

echo "--- parse --file (a capture, mounted read-only)"
count="$("${run[@]}" -v "$fixture:/data/capture.log:ro,z" "$image" parse --format json -i /data/capture.log | wc -l)"
expected="$(grep -c . "$fixture")"
echo "$count of $expected packets"
[ "$count" -eq "$expected" ] || fail "parse --file printed $count packets, expected $expected"

echo "--- user"
uid="$("${run[@]}" --entrypoint id "$image" -u)"
echo "uid $uid"
[ "$uid" != "0" ] || fail "the image runs as root"

echo "--- size"
bytes="$(docker history --human=false --format '{{.Size}}' "$image" | awk '{ s += $1 } END { printf "%d", s }')"
echo "$((bytes / 1000000)) MB uncompressed (budget $((max_bytes / 1000000)) MB)"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  echo "aprscli image ${platform:-}: $((bytes / 1000000)) MB uncompressed (budget $((max_bytes / 1000000)) MB)" \
    >> "$GITHUB_STEP_SUMMARY"
fi
[ "$bytes" -le "$max_bytes" ] || fail "the image is $bytes bytes, over the budget of $max_bytes"

echo "Smoke tests passed."
