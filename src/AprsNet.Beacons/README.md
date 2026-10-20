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

`Drifted()` asks for a beacon now, as `Changed()` does, with the reason `Drift`: see dead reckoning below.

Each beacon is timed from when the last one was actually sent, so after a gap (transmit off, the connection down, the
app closed) a schedule asks for one beacon now and carries on; it never asks for the ones it missed. A manual "send
now" is a `Sent()` like any other. To start a decay schedule's sequence again from a manual send, call `Changed()`
first.

The schedules take a `TimeProvider`, so tests can drive them with `FakeTimeProvider`. They don't enforce limits such
as a minimum interval on RF or spacing between beacons; that's the app's policy.

## Dead reckoning

Other stations show a moving station carrying on from its last beacon at the same course and speed. `DeadReckoning`
works out that assumed position, and how far it is from where the station really is, so an app can show the drift and
beacon again once it is too far:

```csharp
using AprsNet.Packets;

// From the beacon just sent (or new DeadReckoning(position, course, speedKnots, sentAt)).
DeadReckoning.TryFromReport(report, sentAt, out var lastBeacon);

// On each GPS fix:
var error = lastBeacon.ErrorAt(fix, now); // DistanceKm, Bearing, and the Assumed position
if (error.DistanceKm > 0.5)
{
    position.Drifted(); // due now, with the reason Drift
}
```

`PositionAt(time)` gives the assumed position alone, for showing where a heard station probably is now. A report with
a course of `000` gives no dead reckoning, since APRS allows it to mean unknown. The estimate assumes the station never
turns or stops, so how long to trust it is the app's choice.
