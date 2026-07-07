---
schemaVersion: 1
workId: 003-audio-elmish
title: Audio Elmish
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Audio Elmish Specification

Prose status: specified

## User Value
A product's Elmish `update` can return `Audio.Cmd.playSfx` / `playMusic` / `stopMusic` /
`setMasterVolume` commands that the Elmish runtime executes to play through an
`IAudioBackend` — keeping `update` a pure `Model -> Model * Cmd<'msg>` with fire-and-forget
audio, and without the audio component ever depending on `FS.GG.UI`.

## Scope
- SB-001: A thin `Audio.Cmd` module producing `Elmish.Cmd<'msg>` values over a supplied
  `IAudioBackend`, mirroring the `Core.Audio` vocabulary one-for-one.
- SB-002: Depends on `Elmish` (MIT) + `FS.GG.Audio.Host` + `FS.GG.Audio.Core` only.

## Non-Goals
- SB-003: An `Audio.Sub` playback-event subscription surface (needs backend callbacks the
  Host seam does not yet expose) — deferred until the Host grows events.
- SB-004: Any `FS.GG.UI.*` integration.

## User Stories
- US-001 (P1): As a product author, I return `Audio.Cmd.playSfx …` from `update` and the
  sound plays when the Elmish runtime executes the command, with no change to `update`'s
  pure shape.
- US-002 (P1): As a maintainer, the audio Elmish bridge keeps the component
  render-independent — it does not drag in `FS.GG.UI`.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given `Audio.Cmd.ofEffects backend effects`, when the returned command's effects are run with a dispatch, then each `AudioEffect` is played through the backend in dispatch order.
- AC-002 [US-001] [FR-002]: Given `Audio.Cmd.playSfx backend sound vol` (and the other three single-effect constructors), when the command runs, then it plays exactly the equivalent `Core.Audio` effect.
- AC-003 [US-001] [FR-003]: Given any `Audio.Cmd.*` command, when its effects run with a recording dispatch, then no message is dispatched (fire-and-forget) and the recording backend records exactly the requested effects.
- AC-004 [US-002] [FR-004]: Given the built `FS.GG.Audio.Elmish` assembly, when its dependencies are inspected, then they are `Elmish` + `FS.GG.Audio.Host` + `FS.GG.Audio.Core` (+ FSharp.Core) with no `FS.GG.UI.*` and no `SkiaSharp`.
- AC-005 [US-001] [FR-005]: Given the test suite, when it runs headless, then it executes produced commands against a recording backend with a no-op dispatch, asserting recorded effects — with no audio device.
- AC-006 [US-002] [FR-006]: Given the committed `FS.GG.Audio.Elmish` `.fsi` baseline, when the public surface is compared, then the `Audio.Cmd` surface matches with no drift.

## Functional Requirements
- FR-001: `Audio.Cmd.ofEffects` MUST produce an `Elmish.Cmd<'msg>` that, when its effects run, plays each `AudioEffect` through the supplied `IAudioBackend` in dispatch order. (covers AC-001)
- FR-002: `Audio.Cmd.playSfx`/`playMusic`/`stopMusic`/`setMasterVolume` MUST each produce the command equivalent to `ofEffects` of the matching single `Core.Audio` effect. (covers AC-002)
- FR-003: Every `Audio.Cmd.*` command MUST dispatch no message (fire-and-forget) and MUST play exactly the requested effects, no more. (covers AC-003)
- FR-004: `FS.GG.Audio.Elmish` MUST depend only on `Elmish` + `FS.GG.Audio.Host` + `FS.GG.Audio.Core` (+ FSharp.Core) and MUST NOT reference any `FS.GG.UI.*` package or `SkiaSharp`. (covers AC-004)
- FR-005: The test suite MUST run headless, executing produced commands against a recording backend with a no-op dispatch and asserting recorded effects, with no audio device. (covers AC-005)
- FR-006: The public `Audio.Cmd` surface MUST be declared by a committed `.fsi` with a surface baseline. (covers AC-006)

## Ambiguities
No material ambiguities recorded. The design is fully determined by DEC-004 of `002-audio-host`
(separate Elmish package) and the shipped Host/Core surfaces.

## Public Or Tool-Facing Impact
- Introduces the `FS.GG.Audio.Elmish` public package surface (`Audio.Cmd`) declared by a
  committed `.fsi` baseline — Tier 1. Adds a dependency on the `Elmish` MIT library.

## Lifecycle Notes
- Realizes DEC-004 of `002-audio-host`. Next lifecycle action: `fsgg-sdd clarify --work 003-audio-elmish`.
