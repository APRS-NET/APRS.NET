# Messaging design

Status: **agreed** (#190); sending and receiving (#191, #192) and bulletins (#193) are implemented. This is the shape of `AprsNet.Messaging`, a new package that handles APRS message
conversations for one station: message IDs, retries until an ack or rej, reply-acks, acks for what it receives, retries
delivered once, and the bulletins it hears. AprsMap's transmit phase builds messaging (F2.4), bulletins (F2.5), the
email and query helpers (F2.6) and auto-reply (F2.10) on it.

It follows APRS101 chapter 14 and the APRS 1.1 reply-ack rules (`docs/spec/aprs11/messaging.md`, A11-RACK-01 to 06).

## Shape

The engine does no I/O. The app hands it every packet it receives, from any source, and sends the packets the engine
gives back. It works the same over APRS-IS, a KISS TNC or both.

```csharp
var station = new MessagingStation(Callsign.Parse("N0CALL-9"), new MessagingStationOptions());

// Everything heard, from every source. The engine picks out what concerns it.
station.Receive(packet, source: "aprs-is");

// What to send, as it becomes due: new messages, retries, acks and rejs.
await foreach (var outgoing in station.ReadOutgoingAsync(cancellationToken))
{
    var address = outgoing.Source == "aprs-is" ? isAddress : rfAddress;
    await Send(address.Create(outgoing.Payload), outgoing.Source);
}

// Sending, and following one message.
OutgoingMessage message = station.Send(Callsign.Parse("WU2Z"), "Hello");
message.StateChanged += (_, _) => Console.WriteLine(message.State); // Sent, Retrying, Delivered, Rejected, Failed

// Messages for this station, each delivered once.
station.MessageReceived += (_, e) => Console.WriteLine($"{e.Message.From}: {e.Message.Text}");
```

| Type | Role |
| --- | --- |
| `MessagingStation` | One station's conversations: `Send`, `Receive`, `Reject`, `ReadOutgoingAsync`, events |
| `MessagingStationOptions` | Retry profile, ID seed, ack repeats, receive window, `TimeProvider` |
| `OutgoingMessage` | One message sent: correspondent, text, ID, `State`, attempts, next retry, `Cancel()` |
| `IncomingMessage` | One message received: sender, text, ID, when, source |
| `OutgoingPacket` | A payload to send now, and the source the correspondent was last heard on |
| `RetryProfile` | A named retry schedule (below) |
| `BulletinBoard` | The bulletins and announcements heard, from anyone (#193) |

## Decisions for review

Each has a recommendation; the alternatives considered follow it.

1. **The engine runs its own timer** on `TimeProvider` and hands out packets through `ReadOutgoingAsync`, as the
   connections hand out packets through `ReadPacketsAsync`. Tests drive it with `FakeTimeProvider`.
   *Alternative:* the app calls `Poll(now)` and sets its own timer from `NextDue`. Purer, but every app writes the same
   timer loop.

2. **The engine returns payloads, not packets.** `OutgoingPacket.Payload` is a `MessagePayload`; the app builds the
   packet with its own `PacketAddress`, since only it knows the TOCALL and which path suits which source (a `WIDE`
   path on RF, none on APRS-IS). `OutgoingPacket.Source` says where the correspondent was last heard, from any of its
   packets, so the app can reply the same way, or send everywhere when it is `null`. The station remembers this for
   up to 10,000 stations, forgetting the half heard longest ago when it fills.

3. **Message IDs are two characters**, from `0-9A-Za-z` (3,844 values), one sequence for the station. Reply-acks need
   `{MM}AA` to fit the 5-character limit, so `MM` can't be longer. The app saves `MessagingStation.LastMessageId` (an
   event fires when it changes) and passes it back as `MessagingStationOptions.LastMessageId` after a restart, so IDs
   don't repeat. *Alternatives:* a store interface (more ceremony for one string); a sequence per correspondent (no
   real gain, and more to save).

4. **Reply-acks always.** Every outgoing message ID is `{MM}AA`: `AA` is the latest ID owed to that station, added at
   the moment of sending, so it can change between retries (A11-RACK-01). A received reply-ack counts as an ack
   (A11-RACK-02), and the explicit ack still goes out, echoing the whole ID (A11-RACK-04). Stations that don't
   understand reply-acks still see an ordinary ID.

5. **One message at a time per correspondent.** A second message to the same station waits until the first is
   delivered, rejected or failed, as APRS radios expect (messages.txt: later lines wait for the ack). Messages to
   different stations go out side by side.

6. **Retry profiles, named so a UI can offer them.** Each retry is randomised by ±10 % (A12-NET-01), but never comes
   less than 30 s after the last send, since APRS-IS drops a copy inside 30 s as a duplicate.

   | Profile | Waits before each retry | Fails after the last send | Sends |
   | --- | --- | --- | --- |
   | `Default` | 30 s, 1 min, 2 min, 4 min, 8 min | 8 min | 6, failing after about 23 minutes |
   | `AprsIs` | 30 s, 2 min, 5 min | 5 min | 4. The internet leg is reliable; retries cover the far IGate's RF leg |
   | `SlowHf` | 2 min, 4 min, 8 min, 16 min | 16 min | 5, for 300-baud HF |
   | `None` | none | 2 min | 1 |

   The first retry at 30 s is slower than the 15 s that replyacks.txt suggests, because of APRS-IS's duplicate
   window; an app on RF only can make its own profile.

7. **Retry when heard.** A message that failed gets one more try when its correspondent is heard again within an hour
   (A12-NET-03, "retry on heard"). It goes back to `Retrying`, then to `Delivered` or `Failed` again.

8. **Every copy is acked, each message delivered once.** A received message with an ID is acked each time a copy
   arrives, since the sender may have missed the earlier ack. `MessageReceived` fires once per sender and ID (with any
   `}AA` part stripped, A11-RACK-03) within `ReceiveWindow`, an hour by default. A message without an ID is delivered
   and not acked. `MessagingStationOptions.AckRepeats` (1 by default, 2 is common on RF) sends each ack that many
   times, 30 s apart.

9. **Duplicates across sources are handled, not prevented.** The same message heard on RF and APRS-IS is delivered
   once and acked once per copy; an app that already merges its sources (`PacketMerger`, as AprsMap does) sends fewer
   acks. *Alternative:* the engine merges sources itself. It would duplicate `PacketMerger`.

10. **Rejecting is the app's choice.** `Reject(IncomingMessage)` sends `rej`. The engine never rejects on its own, since
    what the app can handle is its business.

11. **One station per engine.** Messages count as addressed to the station when the addressee is its callsign and SSID
    (no SSID and `-0` are the same). An app with several callsigns makes several engines.

12. **Text is limited to 67 characters**, as APRS101 sets. `Send` refuses longer text rather than splitting it;
    splitting and the warnings in A11-MSG-01 to 05 belong to the UI. Characters `|`, `~` and `{` are refused too.

13. **Nothing else is persisted.** Messages waiting for an ack are lost on a restart, as on a radio; an app that wants
    to keep them sends them again. Only the last message ID needs saving.

## Bulletins

`BulletinBoard` is separate from `MessagingStation` because bulletins aren't addressed to anyone. It is fed every
packet, keeps bulletins (`BLN0`–`BLN9`), group bulletins (`BLN4WX`), announcements (`BLNA`–`BLNZ`) and NWS bulletins by
sender and ID, replaces one when a new copy differs, and forgets one not heard for `Expiry` (a day by default). Its
`Changed` event says what was added, replaced or removed. NWS bulletins are kept per sender, addressee and message ID
(or text, without one), since one sender sends many different warnings to `NWS-WARN`; `NWS_` addressees count too. At
most `MaxBulletins` (1,000) are kept, dropping the one heard longest ago. Sending bulletins is a `MessagingStation` job with no ID and
no acks, retried on the `Default` schedule's decay.

## Out of scope

- The email gateway, ANSRVR and auto-reply rules (A11-AA): AprsMap builds them on `Send` and `MessageReceived`. An
  auto-reply has no message ID and no retries, which `Send` allows with `RetryProfile.None` and `withId: false`.
- ITEM-in-MSG and the 1.2 message servers (A12-MSG-01, 02).
- Gating messages between APRS-IS and RF: the two-way IGate (0.7.0, #214).

## Order

1. This note (#190).
2. Sending (#191) and receiving (#192), side by side.
3. Bulletins (#193).
4. `aprscli message` (#194), the live check.
