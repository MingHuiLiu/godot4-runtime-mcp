# Runtime Black Box — opt-in timeline sampling (incremental implementation)

**Baseline:** c55a61a (approved snapshot/export implementation), clean isolated worktree.

## Delivery boundary

Add explicitly opt-in and bounded periodic checkpoint capture while a Godot game runs; compute observable changes between snapshots by persistent node identity; commit immutable on-disk frame/event batches; read them offline through CLI/MCP after game exits. This is *observed* sampling, not deterministic replay, a signal trace, or a movie. Skipped intermediate transitions must never be described as complete event history.

## Tasks and RED→GREEN gates

1. Pure .NET `TimelineDeltaBuilder` plus typed v1 contracts (added/removed/reparented/properties and sample span). Reject unknown versions. Unit test two synthetic snapshots and cap changes.
2. Durable `TimelineArchive` writes per-sample change batch and checkpoint with atomic file publication, checksums, bounded quotas and explicit dropped/error markers. Unit tests missing/corrupt/session isolation and restart read.
3. `RuntimeTimelineController` opt-in `start/status/stop` with `sampleEveryFrames`, `maxSamples`, main-thread snapshots and serialized writer; use the active Runtime Transport instance ID; no blocking I/O on `_Process`. Integrate manifest and Godot Headless dynamic-node mutation probe.
4. Offline `godot_timeline_sessions` and `godot_timeline_events` with project-scoped validated paths, pagination, and Native AOT JSON metadata. MCP/CLI smoke and end-to-end test.
5. Docs, complete regression, Native AOT and addon ZIP tests; commits per gate.

## Guardrails

Snapshots capture observed node properties only (possibly expensive); stable identities through ConditionalWeakTable for the runtime process. Sampling cannot see intra-window events and does not hook arbitrary signals/input. Never mutate live nodes for capture/export. Reject traversal/symlink paths. `maxSamples` and `sampleEveryFrames` bounds; explicitly mark partial/dropped frames, completed vs stopped vs failed sessions. Keep v1 checkpoint and its 7 snapshot tools backward-compatible.

## Evidence / implementation notes

- T1 committed as `4a6abcf`: pure typed observable snapshot deltas, 75 .NET assertions.
- T2 committed as `1463cb2`: checksum-verified append-only timeline sample archives, 81 assertions.
- T3 committed as `1d7cde8`: opt-in runtime start/stop/status; actual Godot Headless 4-sample dynamic-node test; 83 assertions.
- T4 committed as `035ffa3`: project-isolated offline MCP/CLI list, info and event queries; actual CLI after Godot quits; 89 assertions.
- Follow-up: resource/diagnostic/group differences, source docs, security audit, Native AOT and package verification are addressed in task 5.
