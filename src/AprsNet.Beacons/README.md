# AprsNet.Beacons

When an APRS station should beacon next, and why. A schedule does no I/O: it answers with a time and a reason, the app
sends the beacon wherever it goes (APRS-IS, a KISS TNC or both), then tells the schedule it was sent.

```csharp
using AprsNet.Beacons;

// A fixed station: every 30 minutes, the first after a random wait of up to 3 minutes.
var position = new FixedBeaconSchedule(TimeSpan.FromMinutes(30));

// An object: now, then 1, 2, 4, 8, 16 and 30 minutes apart, starting again when it changes.
var @object = new DecayBeaconSchedule(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));

// In the app's loop or timer:
if (position.IsDue)
{
    await SendPositionAsync();
    position.Sent();
}

Console.WriteLine($"Next beacon in {position.DueIn:mm\\:ss} ({position.Next.Reason})"); // Start, Interval, Decay, Changed

@object.Changed(); // moved or edited: due now, and the decay starts again
```

- **`FixedBeaconSchedule`** beacons at a fixed interval. The first beacon comes after a random wait, a tenth of the
  interval and at most 3 minutes by default, so stations that start together don't all beacon at once.
- **`DecayBeaconSchedule`** beacons now, then doubles the wait from a first step up to a ceiling, and starts again on
  `Changed()`. For objects, items and new information.

Each beacon is timed from when the last one was actually sent, so after a gap (transmit off, the connection down, the
app closed) a schedule asks for one beacon now and carries on; it never asks for the ones it missed. A manual "send
now" is a `Sent()` like any other. To start a decay schedule's sequence again from a manual send, call `Changed()`
first.

The schedules take a `TimeProvider`, so tests can drive them with `FakeTimeProvider`. They don't enforce limits such
as a minimum interval on RF or spacing between beacons; that's the app's policy.
