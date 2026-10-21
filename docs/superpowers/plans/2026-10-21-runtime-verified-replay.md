# Runtime Black Box — isolated verified input replay

Scope: run in a trusted, independent Godot .NET process, reconstruct a checkpoint as baseline, synthesize only bounded key/mouse/joy input metadata from authenticated timeline batches, and compare observed scene properties at recorded sample boundaries. Return `matched`, `diverged`, `inconclusive`, `failed` and a structured report. Never promise full engine determinism, time travel, private input text, physics state or arbitrary game behavior. User must explicitly initiate replay (non-mutating to live game; worker loads trusted original game scripts).

Tests/gates:
1. Pure host independent timeline `ReplayPlanBuilder` and `ReplayStateComparer` with typed schema, strict input validation, zero-drop recording requirements, expected checkpoint ordering and resource integrity; negative tests for divergence, missing event, stale/missing checkpoint, unsupported inputs and nontrivial sampling gaps.
2. Headless standalone `ReplayWorker` reconstructs baseline scene and adds to an isolated tree, dispatches only allowlisted synthetic InputEvent keys/buttons at monotonic relative process frames, captures an independently observed checkpoint at each target and writes durable report of expected/actual state and divergence issues. No attempt to rewind global Engine.GetProcessFrames. Use GDScript/C# probe node with exported state mutated only on `_Input`. Include success, injected tamper/divergence failure and non-replayable recording cases.
3. Host `ReplayJobManager` launches isolated mono scene with safe config path, bounded runtime, polling status, output report; MCP/CLI start & status tools + end-to-end real process tests. Resist symlink traversal, prevent parallel overload and never change existing recorded checkpoints.
4. Complete docs, all checkpoint/timeline/Gallery headless regressions, Native AOT packaging & tool counts, clean commits. No push/merge.

Validation profile: only `sampleEveryFrames=1..120`, `captureInput=true`, no skipped/dropped samples/events, 2..60 checkpoints and 1200 recorded process-frame span. Signals and custom scripts are not injected; property state may depend on physics, random, timers, external files, window focus or environment; if any of these prevent observational match, report divergence. Unsupported input types => explicit `inconclusive`, never `matched`.

## Implementation ledger

- `7a6761b`: typed replay contracts, bounded plan and cross-process comparison; 120/120 assertions.
- `f76fa8a`: isolated Godot worker, real Key K replay, matched vs changed-behavior divergence fixture, 120/120.
- `3b206d4`: offline CLI/MCP job start/status/report and Resource/reference/group comparison; 130/130 assertions.
- Follow-up: preserve recorded per-event Process frame timing (131/131); reject missing input capture at manager preflight (132/132).
- Real macOS GUI Godot compatibility renderer emitted four valid PNGs (zero unavailable), and decoded images contained the visually drawn red `ColorRect` test patch. Headless tests still verify honest lack of renderer support.
- Final steps: old checkpoint and timeline regressions, Native AOT release packaging, actual native CLI replay and secure documentation. No GitHub push/merge/release.
