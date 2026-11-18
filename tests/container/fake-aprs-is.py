"""A stand-in APRS-IS server for the container end-to-end test: one client, no internet.

It answers a login the way aprsc does (a banner, then '# logresp'), checks the login line asks for the filter it was
given, sends the packets from a capture file (listen --capture format: '<UTC timestamp> <raw line>'), then keeps the
connection open until the client closes it. The login line is written to LOGIN_OUT for the test to inspect.
"""

import os
import socket
import sys

PORT = int(os.environ.get("PORT", "14580"))
CAPTURE = os.environ["CAPTURE"]
COUNT = int(os.environ.get("COUNT", "20"))
FILTER = os.environ["FILTER"]
LOGIN_OUT = os.environ.get("LOGIN_OUT", "/out/login.txt")


def main() -> int:
    with open(CAPTURE, encoding="latin-1") as capture:
        packets = [line.rstrip("\r\n").split(" ", 1)[1] for line in capture if line.strip()][:COUNT]

    server = socket.create_server(("0.0.0.0", PORT))
    print(f"fake APRS-IS listening on {PORT} with {len(packets)} packets", flush=True)
    client, address = server.accept()
    with client:
        print(f"client from {address[0]}", flush=True)
        client.sendall(b"# aprsc 2.1.19 fake-aprs-is\r\n")
        reader = client.makefile("rb")
        login = reader.readline().decode("latin-1").rstrip("\r\n")
        print("login received", flush=True)  # the test reads it from LOGIN_OUT; a passcode is never printed
        with open(LOGIN_OUT, "w", encoding="latin-1") as out:
            out.write(login + "\n")
        parts = login.split()
        if len(parts) < 2 or parts[0] != "user" or f"filter {FILTER}" not in login:
            client.sendall(b"# login refused by fake-aprs-is\r\n")
            print("unexpected login line", file=sys.stderr, flush=True)
            return 1
        client.sendall(f"# logresp {parts[1]} unverified, server FAKE\r\n".encode("latin-1"))
        for packet in packets:
            client.sendall(packet.encode("latin-1") + b"\r\n")
        # Hold the connection, as a real server would, until the client has read what it wants and closes it.
        while reader.readline():
            pass
    print("client closed the connection", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
