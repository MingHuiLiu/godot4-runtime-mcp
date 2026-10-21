# Runtime Black Box — opt-in screenshot sampling and offline visual timeline

Base: `9922adf` (observed events), clean isolated worktree. Development only in `feat/runtime-timeline-visual` until tests and review pass.

**User-visible goal:** bounded, opt-in screenshot captures aligned to committed observed scene checkpoints and events, private integrity-verified files, and a single local HTML gallery showing frames and changes chronologically. Offline, static HTML is an inspection aid, not video or deterministic replay.

## RED/GREEN gates

1. Typed optional `TimelineFrameImage` metadata on `TimelineBatch` plus session opt-in flag. Archive `frames/<sample-index>.png` with PNG magic/header/size checks, owner-only permission, SHA-256, atomic publication *before* batch's commit marker. Offline reads validate file and batch metadata; reject tamper, traversal, invalid dimensions. Existing v1 JSON batches without image fields remain readable.
2. `RuntimeTimelineController` opt-in `captureScreenshots` (default false), hard pixel/PNG byte and frame-count limits. Capture Godot viewport image on main thread, resize if needed; hand immutable PNG bytes to worker, never native image or node. If headless or renderer cannot supply image, record explicit `unavailable` rather than falsify success. Add Godot sample probe and default-off tests.
3. Pure RuntimeHost offline `TimelineGalleryExporter`: render self-contained static private HTML with sample frame indices, screenshot previews (verified and bounded data URIs), names/counts of events and dropped fields. Safe text escaping and no executable script. CLI/MCP `godot_timeline_export_gallery` returning path and frame counts; no arbitrary output path and no screenshot bytes sent to MCP directly. Tests for HTML escaping, checksum mismatch, wrong-project isolation, no-camera sessions and Native AOT.
4. Sync addon, docs, full .NET 95+ assertions, original snapshot .tscn headless, timeline headless and native CLI gallery workflow, local AOT publish + ZIP and SHA checks; commit per gate.

## Limits and expectations

Opt-in and user privacy: screenshots can contain private content, remain on-disk under per-user restricted folders; gallery embeds images only after explicit export. Native screenshots are renderer-dependent, including black/unavailable images in headless. Do not claim deterministic replay or a reliable video framerate. Individual cap <=512 KiB, 1024 px maximum dimension, total cap <=32 MiB/session; last samples record skipped/unavailable status. Existing capture APIs and backward-compatibility preserved. Do not automatically push, merge, or create a GitHub Release.

## Evidence

- Task 1 commit `3017444`: 103 assertions, private frame byte/hash/PNG validation, tamper + traversal rejections.
- Task 2 commit `139717b`: optional Godot viewport sampling, explicit dummy-renderer fallback; end-to-end Headless 4 unavailable markers rather than fabricated images.
- Task 3 commit `3987c3c`: escaped CSP-protected static HTML timeline, local MCP/CLI gallery export, integrity verification and offline CLI integration, 112 .NET assertions.
- Task 4: documentation and final AOT and old `.tscn` regressions, no pushes or merges.

## Final validation

- `.NET`: 112/112 assertions including Native AOT SDK stdio and HTTP compatibility.
- Real Godot Headless integration: original `.tscn` export and strict failure diagnostics; timeline with screenshot opt-in and four explicit `renderer_unavailable` samples; two consecutive offline gallery E2E passes.
- PNG positive path: synthetic one-pixel PNG persisted, hashed, read and embedded in HTML; negative tamper, oversized frame, traversal, symlink defenses and HTML injection checks.
- Native macOS ARM64: `publish-release.sh v3.1.0 osx-arm64` succeeded, local ZIP integrity and SHA256SUMS validated; native CLI exported same gallery after game exit, 112/112 assertions.
- Limitation: graphical Viewport PNG image *quality and actual color* were not verified in GUI because the authorized test environment uses a dummy Headless renderer. Do not treat Headless black/unavailable frames as proof of GUI image fidelity.
- No git push, merge, or GitHub Release.
