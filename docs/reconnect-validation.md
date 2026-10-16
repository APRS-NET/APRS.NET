# Reconnect validation against live APRS-IS

How `AprsIsConnection` recovers from losing the network, checked on the live APRS-IS network as well as through the
proxy that cuts, stalls and refuses connections in the tests (#113). This records the live check for #156.

## What was expected

- A dead link is noticed when the operating system reports the socket failed, or after `IdleTimeout` (90 s) with
  nothing received, since APRS-IS servers send a `#` keepalive about every 20 seconds.
- While the network is down, each attempt fails and the wait doubles from `ReconnectDelay` (5 s) up to
  `MaxReconnectDelay` (60 s), varied by up to 20%.
- Once the network is back, the next attempt connects and logs in again with the same filter.

## Live check, October 2026

**Setup.** AprsMap on a laptop, connected to APRS-IS over the home Wi-Fi, showing the connection state as a pill.

**Run.** Driving away from the house, the laptop lost the home Wi-Fi.

| Step | Seen |
| --- | --- |
| Wi-Fi lost | Packets stopped. |
| Some time later (not timed) | The APRS-IS connection reported the link lost and the pill turned grey, with a countdown to the next attempt. |
| Phone hotspot turned on | The laptop joined the hotspot, a different network with a different address. |
| Next countdown ended | APRS-IS reconnected, with no restart of the app. |

**Result.** Passed: the connection noticed the loss without help, kept retrying while there was no network, and
recovered on the first attempt after a network came back, even though that network was a different one.

How long the loss took to notice wasn't timed; it felt like 30 s or more, which fits either the operating system
reporting the socket failed or the 90 s idle timeout. The waits between attempts and the packets missed weren't recorded
either; the proxy tests (#113) cover the backoff and the idle timeout precisely.

No bugs were found, so no issues were opened.
