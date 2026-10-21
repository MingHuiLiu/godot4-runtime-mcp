# Runtime Black Box — opt-in observed input/signal channel

Implement a bounded, opt-in event channel alongside existing periodic timeline diffs. Observable *actual* signal callbacks and input event metadata must be captured on Godot's main thread, timestamped, and archived with observed checkpoint batches. Defaults off; no text/Unicode keystroke payloads; no claims of comprehensive input/signals or deterministic replay.

TDD tasks:
1. Typed `ObservedRuntimeEvent` with frame/time/args and `TimelineBatch.ObservedEvents`, `DroppedObservedEvents`; JSON AOT source generation; pure schema roundtrip/archival checksum test.
2. `RuntimeTimelineController` explicit `captureSignals`, `captureInput`, bounded FIFO, `ObserveSignal`/`ObserveInput`, sanitize potentially sensitive fields, drain at snapshots and persist dropped count. Add Godot Headless negative/positive probe.
3. Wire backend `_Input` and existing zero-argument signal callback plus emitted tool-controlled signals to this opt-in channel. Update canonical Manifest and repeat addon sync.
4. Validate CLI and offline MCP query returns actual observed events after game exits; document scope; full .NET, old Snapshot + timeline Headless, native AOT and ZIP verification, commit separately.

Important limits: Godot's current generic signal watcher auto-connects only zero-argument signals; no broad interception of arbitrary method/OS input, key text, or input methods. Signal values include bounded nontext metadata only; input events record codes/positions but never Unicode text. Event archive events are observation logs, not replay instructions, and app script may still handle events in addition to the recorder.

## Implementation evidence

- Typed/AOT-safe observed event batch + session opt-in flags, `05aabb4`, 95 .NET assertions.
- Main-thread live signal/input capture, bounded 256-record queue, Unicode/text redaction, persisted loss reporting; Godot Headless real-signal and real-input probe.
- Backend `_Input`, existing global zero-arg signal listener, and successful `emit_signal` parameter invocation integrated with opt-in timeline.
- Verified default-off second session and tail flush on stop, game exit, then offline CLI event-page validation; source docs explicitly limit capture coverage.
