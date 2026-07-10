---
schemaVersion: 1
workId: 005-elmish-engine-cmd
title: Elmish Engine Cmd
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Elmish Engine Cmd Specification

Prose status: specified

## User Value
An Elmish product author can return `Audio.Cmd.ofEngine engine dt effects` from `update` and have
the effect batch realized **through `FS.GG.Audio.Engine`**, so bus mixing, fades, cross-fades,
ducking, and 3D positioning actually apply — instead of being silently dropped by the raw-backend
`ofEffects` path, where `SetBusVolume`/`Duck` are no-ops and `PlaySfx3D` degrades to non-positional.
The existing raw-backend constructors stay as a documented thin bridge, and the docs now say plainly
which surface mixes and which does not.

## Scope
- SB-001: Add `Audio.Cmd.ofEngine` producing an `Elmish.Cmd<'msg>` that, when the Elmish runtime
  executes it, advances a supplied `Engine.T` by `dt` and applies the effect batch via
  `Engine.step`, so the Engine's mixing semantics apply.
- SB-002: Document the raw-backend boundary on the existing `ofEffects` and single-effect
  constructors (`.fsi` + README): they play straight at the `IAudioBackend` with no bus
  mixing/fades/ducking/3D, and authors needing those semantics use `ofEngine`.
- SB-003: Add the `FS.GG.Audio.Engine` project reference to `FS.GG.Audio.Elmish` (required to type
  the new constructor); update the committed `.fsi` surface baseline.

## Non-Goals
- SB-004: No change to `FS.GG.Audio.Engine`, `FS.GG.Audio.Host`, or `FS.GG.Audio.Core` semantics —
  `ofEngine` is a thin Elmish adapter over the existing `Engine.step`.
- SB-005: No removal or behavior change of `ofEffects`/`playSfx`/`playMusic`/`stopMusic`/
  `setMasterVolume` — they remain the raw-backend bridge, now documented as such.
- SB-006: No new subscription (`Audio.Sub`) surface, and no `FS.GG.UI.*` dependency.
- SB-007: `ofEngine` does not own the frame clock — the caller supplies `dt` and the `Engine.T`,
  exactly as a direct `Engine.step` caller does. No timer, loop, or scheduling is introduced.

## User Stories
- US-001 (P1): As an Elmish product author, I return `Audio.Cmd.ofEngine engine dt effects` from
  `update`, and when the runtime executes it the effects are realized through the Engine so bus
  volume, fades, ducking, and 3D positioning apply.
- US-002 (P2): As a product author reading the API, the `ofEffects`/single-effect constructors state
  that they bypass the Engine (no mixing), so I know when I must reach for `ofEngine` instead.
- US-003 (P1): As a maintainer, the Engine-backed path stays render-independent (no `FS.GG.UI`) and
  headless-testable with no audio device.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `Audio.Cmd.ofEngine engine dt effects`, when the returned command's effect runs with a dispatch, then the batch is advanced through the engine by a single `Engine.step engine dt effects` call.
- AC-002 [US-001] [FR-002]: Given an engine and the batch `[ setBusVolume Sfx 0.5; playSfx s 1.0 ]` realized via `ofEngine` over a plain recording backend, when the command runs, then the backend records a single `PlaySfx(s, 0.5)` (bus gain folded in, `SetBusVolume` consumed by the engine) — whereas the same batch via `ofEffects` records `SetBusVolume(Sfx,0.5)` then `PlaySfx(s,1.0)` at raw gain.
- AC-003 [US-001] [FR-003]: Given `ofEngine`, when its effect runs with a recording dispatch, then no message is dispatched (fire-and-forget), consistent with the other `Audio.Cmd` constructors.
- AC-004 [US-002] [FR-004]: Given the committed `.fsi` and README, when the `ofEffects`/single-effect docs are read, then they state the surface plays straight at the backend with no bus mixing/fades/ducking/3D and direct authors to `ofEngine`/`Engine.step` for those semantics.
- AC-005 [US-003] [FR-005]: Given the built `FS.GG.Audio.Elmish` assembly, when its dependencies are inspected, then they are `Elmish` + `FS.GG.Audio.Engine` + `FS.GG.Audio.Host` + `FS.GG.Audio.Core` (+ FSharp.Core), with no `FS.GG.UI.*` and no `SkiaSharp`.
- AC-006 [US-003] [FR-006]: Given the test suite, when it runs headless, then it exercises `ofEngine` through an `Engine` over a recording backend with a no-op dispatch and asserts mixing is applied, with no audio device.
- AC-007 [US-003] [FR-007]: Given the committed `.fsi` surface baseline, when the public surface is compared after adding `ofEngine`, then the baseline is updated and matches the source signature with no drift.

## Functional Requirements
- FR-001: `Audio.Cmd.ofEngine` MUST produce an `Elmish.Cmd<'msg>` that, when its effect runs, advances the supplied engine by invoking `Engine.step engine dt effects` exactly once for the batch. (covers AC-001)
- FR-002: Effects realized via `ofEngine` MUST be subject to the Engine's mixing semantics (bus gain, fades, ducking, 3D positioning) as defined by `Engine.step`, rather than played at the raw backend. (covers AC-002)
- FR-003: The `ofEngine` command MUST dispatch no message (fire-and-forget), consistent with the existing `Audio.Cmd` surface. (covers AC-003)
- FR-004: The `.fsi` and README MUST document that `ofEffects` and the single-effect constructors play straight at the `IAudioBackend` with no bus mixing/fades/ducking/3D, and MUST direct authors needing those semantics to `ofEngine`/`Engine.step`. (covers AC-004)
- FR-005: `FS.GG.Audio.Elmish` MUST depend only on `Elmish` + `FS.GG.Audio.Engine` + `FS.GG.Audio.Host` + `FS.GG.Audio.Core` (+ FSharp.Core) and MUST NOT reference any `FS.GG.UI.*` package or `SkiaSharp`. (covers AC-005)
- FR-006: The test suite MUST run headless, exercising `ofEngine` through an `Engine` over a recording backend with a no-op dispatch and asserting mixing is applied, with no audio device. (covers AC-006)
- FR-007: The public `Audio.Cmd` surface MUST be declared by the committed `.fsi` and its surface baseline (`docs/api-surface/FS.GG.Audio.Elmish/Elmish.fsi`) updated to include `ofEngine` with no drift. (covers AC-007)

## Ambiguities
No material ambiguities recorded. `ofEngine` is fully determined by the shipped `Engine.step` surface
(`004-audio-engine`) and the existing `003-audio-elmish` Cmd shape: the caller owns the `Engine.T`
and the per-frame `dt`, exactly as a direct `Engine.step` caller does.

## Public Or Tool-Facing Impact
- Adds the public `Audio.Cmd.ofEngine` constructor to the `FS.GG.Audio.Elmish` package surface
  (declared by the committed `.fsi` baseline) — Tier 1. Adds a package/project dependency on
  `FS.GG.Audio.Engine`. Existing constructors are unchanged in behavior; their doc comments gain an
  explicit raw-backend-boundary note.

## Lifecycle Notes
- Realizes option 2 of `FS-GG/FS.GG.Audio#21` (Engine-backed Cmd path) plus option 1 (document the
  boundary). Next lifecycle action: `fsgg-sdd clarify --work 005-elmish-engine-cmd`.
