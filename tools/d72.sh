#!/usr/bin/env bash
# Puts a Kenwood TH-D72A's built-in TNC into or out of KISS mode over its USB port, or shows what it is sending.
# Nothing here transmits on the air: the commands go to the radio's TNC only.
#
#   tools/d72.sh kiss-on [port]       KISS ON and RESTART (the TNC must be in PACKET mode, not APRS)
#   tools/d72.sh kiss-off [port]      the KISS exit frame (C0 FF C0), back to the cmd: prompt
#   tools/d72.sh raw [seconds] [port] what the radio sends, as hex and text (10 s by default)
#
# The port defaults to /dev/ttyUSB0, at the radio's default 9600 baud.
set -euo pipefail

command=${1:-}
case "$command" in
  raw) seconds=${2:-10}; port=${3:-/dev/ttyUSB0} ;;
  kiss-on | kiss-off) port=${2:-/dev/ttyUSB0} ;;
  *) sed -n '2,8p' "$0" | sed 's/^# \{0,1\}//'; exit 2 ;;
esac

case "$port" in
  /dev/tty*) ;;
  *) echo "Not a serial port: $port" >&2; exit 2 ;;
esac

# Raw 8N1 with no echo; opening the port this way leaves DTR and RTS raised, which the radio needs to send.
stty -F "$port" 9600 raw -echo cs8 -cstopb -parenb -crtscts

case "$command" in
  raw)
    [[ "$seconds" =~ ^[0-9]+$ ]] || { echo "Not a number of seconds: $seconds" >&2; exit 2; }
    timeout "$seconds" cat "$port" | od -A d -t x1z || true
    ;;
  kiss-on)
    # One open port for both commands, reading the radio's replies meanwhile.
    exec 3<>"$port"
    timeout 5 cat <&3 > /dev/null &
    printf '\r' >&3; sleep 1
    printf 'KISS ON\r' >&3; sleep 1.5
    printf 'RESTART\r' >&3
    wait || true
    exec 3>&-
    echo "Sent KISS ON and RESTART to $port. Check with: tools/d72.sh raw 30 (KISS frames start with c0 00)."
    ;;
  kiss-off)
    printf '\xc0\xff\xc0' > "$port"
    echo "Sent the KISS exit frame to $port. Check with: tools/d72.sh raw 10 (text, then a cmd: prompt)."
    ;;
esac
