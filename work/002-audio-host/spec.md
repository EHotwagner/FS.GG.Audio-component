---
schemaVersion: 1
workId: 002-audio-host
title: Audio Host
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Audio Host Specification

Prose status: specified

## User Value
A product's `update` keeps emitting pure `FS.GG.Audio.Core` `AudioEffect` values, and
`FS.GG.Audio.Host` turns them into real sound through a pluggable `IAudioBackend`: a real
OpenAL device backend (`Silk.NET.OpenAL`) when a device is available, and a deterministic
Null/record backend under test or when no device is present. The product's `update`
signature never changes, and a machine with no audio device still builds, tests, and runs —
identical minus sound.

## Scope
- SB-001: Define the narrow `IAudioBackend` seam (open/dispose device, load a PCM buffer,
  play/stop a source, set source gain, set master/listener gain).
- SB-002: Ship the Null/record backend (default; deterministic; headless-safe; records
  requests as `FS.GG.Audio.Core.AudioEvidence`).
- SB-003: Ship the OpenAL backend over `Silk.NET.OpenAL`, mapping the four `AudioEffect`
  cases to device calls.
- SB-004: Degrade-to-Null when the OpenAL device/library cannot be opened.
- SB-005: A minimal `AssetResolver` seam (`SoundId`/`TrackId` → PCM), with a built-in WAV
  reader, sufficient to feed the OpenAL backend.
- SB-006: Depend only on `FS.GG.Audio.Core` + `Silk.NET.OpenAL`; never on Scene/Canvas/Skia.

## Non-Goals
- SB-007: The `AudioEngine` niceties — named buses, fades/cross-fades/ducking, 3D
  listener/emitters, the EFX effect graph — are deferred to a follow-up work item.
- SB-008: Decoders beyond built-in WAV (NVorbis OGG, NLayer MP3) and music streaming.
- SB-009: The additive `AudioEffect` variants deferred from `001-audio-core`
  (`PlaySfx3D`/`SetBusVolume`/`Duck`) — they land with the engine that gives them meaning.
- SB-010: The `miniaudio` alternate backend.
- SB-011: Publishing / registry / roster / ADR changes (cross-repo coordination track).
- SB-012: The Elmish `Cmd`/`Sub` authoring surface — deferred to a separate
  `FS.GG.Audio.Elmish` package (its own work item), so `FS.GG.Audio.Host` never depends on
  `FS.GG.UI.*` (DEC, AMB-004). This item ships only the imperative drive over `IAudioBackend`.

## User Stories
- US-001 (P1): As a product author, I attach an `IAudioBackend` to my loop and the
  `AudioEffect` values my `update` already emits produce sound (OpenAL) or recorded evidence
  (Null), with no change to `update`.
- US-002 (P1): As a developer on a headless/CI machine with no device, the host degrades to
  the Null backend so my build and tests pass without throwing.
- US-003 (P2): As a maintainer, I swap the backend behind `IAudioBackend` (Null ↔ OpenAL)
  without touching game code.
- US-004 (P2): As a product author, I resolve my own `SoundId`/`TrackId` to PCM through the
  `AssetResolver` seam — the host does not own the id→asset mapping.

## Acceptance Scenarios
- AC-001 [US-003] [FR-001]: Given the `IAudioBackend` seam, when a caller folds a list of `AudioEffect` values through any backend, then it drives only the seam members (open, load, play/stop, set-gain, dispose) — no backend-specific type leaks into the calling surface.
- AC-002 [US-002] [FR-002]: Given the Null backend, when a batch of `AudioEffect` values is played on a machine with no device, then it opens no device, throws nothing, and records the requests as `AudioEvidence` byte-identical to `FS.GG.Audio.Core.Audio.interpret`.
- AC-003 [US-001] [FR-003]: Given the OpenAL backend on a machine with a device, when each of `PlaySfx`/`PlayMusic(loop)`/`StopMusic`/`SetMasterVolume` is played, then it issues the corresponding OpenAL call (source play one-shot / looping source / source stop / listener gain) with the volume already clamped by Core.
- AC-004 [US-002] [FR-004]: Given a machine where the OpenAL device or native library cannot be opened, when the host is constructed with the OpenAL backend requested, then it logs the reason and falls back to the Null backend, and the caller observes no exception and the same `AudioEffect` flow.
- AC-005 [US-004] [FR-005]: Given an `AssetResolver` mapping `SoundId "x"` to a WAV asset, when the OpenAL backend plays that sound, then the resolver is invoked and its PCM is loaded through the seam; an unresolved id degrades to a recorded no-op, not a throw.
- AC-006 [US-001] [FR-006]: Given a backend and a per-frame list of `AudioEffect` values, when `Audio.play` folds them through the backend, then each effect drives the backend seam in dispatch order and the product's `update` is unchanged.
- AC-007 [US-003] [FR-007]: Given the built `FS.GG.Audio.Host` assembly, when its dependencies are inspected, then they are exactly `FS.GG.Audio.Core` + `Silk.NET.OpenAL` (+ FSharp.Core); no `FS.GG.UI.*`, no `SkiaSharp`.
- AC-008 [US-002] [FR-008]: Given the test suite, when it runs on a headless/CI machine, then every test passes without a real device and asserts on recorded backend calls / `AudioEvidence`, never on audio samples.
- AC-009 [US-003] [FR-009]: Given the committed `FS.GG.Audio.Host` `.fsi` surface baseline, when the public surface is compared to it, then `IAudioBackend`, the backend constructors, the `AssetResolver` seam, and the `Audio.Cmd` surface match with no drift.

## Functional Requirements
- FR-001: The host MUST expose a narrow `IAudioBackend` seam (open/dispose device, load a PCM buffer, play/stop a source, set source gain, set master/listener gain) such that game-facing code never references a concrete backend type. (covers AC-001)
- FR-002: The Null backend MUST be the default, MUST open no device, MUST NOT throw, and MUST record requests as `AudioEvidence` equal to `FS.GG.Audio.Core.Audio.interpret` for the same input. (covers AC-002)
- FR-003: The OpenAL backend MUST map `PlaySfx`→a one-shot source, `PlayMusic(loop)`→a looping source, `StopMusic`→source stop, and `SetMasterVolume`→listener gain, using `Silk.NET.OpenAL`, with volumes as already clamped by Core. (covers AC-003)
- FR-004: When the OpenAL device or native library cannot be opened, the host MUST log the reason and fall back to the Null backend without surfacing an exception to game code. (covers AC-004)
- FR-005: The host MUST resolve `SoundId`/`TrackId` to PCM through a caller-supplied `AssetResolver` seam (built-in WAV reader), MUST NOT own the id→asset mapping, and MUST treat an unresolved id as a recorded no-op rather than a throw. (covers AC-005)
- FR-006: The host MUST expose an imperative drive `Audio.play : IAudioBackend -> AudioEffect list -> unit` that folds a per-frame batch of `Core.AudioEffect` values through the backend in dispatch order, with no change to a product's `update` and no `FS.GG.UI.*` dependency. (covers AC-006)
- FR-007: `FS.GG.Audio.Host` MUST depend only on `FS.GG.Audio.Core` + `Silk.NET.OpenAL` (+ FSharp.Core) and MUST NOT reference any `FS.GG.UI.*` package or `SkiaSharp`; the OpenAL Soft native MUST be dynamically linked. (covers AC-007)
- FR-008: The test suite MUST run headless with no real device, asserting on recorded backend calls / `AudioEvidence` and never on audio samples. (covers AC-008)
- FR-009: The public surface (`IAudioBackend`, backend constructors, `AssetResolver`, `Audio.Cmd`) MUST be declared by a committed `.fsi` with a surface baseline. (covers AC-009)

## Ambiguities
- AMB-001: `Silk.NET.OpenAL` version to pin, and whether the OpenAL Soft native ships as a
  `Silk.NET.OpenAL.Soft` runtime package or is assumed present on the host. Resolve in
  clarify.
- AMB-002: Headless test strategy for the OpenAL backend — a real device is unavailable in
  CI, so confirm the approach is a **recording fake** implementing `IAudioBackend` (assert
  the effect→call mapping) plus a **degrade-path test** (OpenAL requested, no device →
  Null), with the real OpenAL backend exercised only manually/opt-in. Resolve in clarify.
- AMB-003: `AssetResolver` shape — a pure `SoundId -> byte[] option` (WAV PCM) for this
  item vs. a richer streaming interface; and whether `TrackId` (music) is in scope now or
  waits for streaming. Resolve in clarify.
- AMB-004: `Audio.Cmd`/`Audio.Sub` — to honor render-independence (FR-007), the host cannot
  depend on `FS.GG.UI.Controls.Elmish`. Decision (owner, 2026-07-07): defer the Elmish
  surface to a separate `FS.GG.Audio.Elmish` package (its own work item); this item ships
  only the imperative `Audio.play` drive (FR-006). Recorded as an accepted deferral in
  clarify.

## Public Or Tool-Facing Impact
- Introduces the `FS.GG.Audio.Host` public package surface (`IAudioBackend`, Null + OpenAL
  backends, `AssetResolver`, `Audio.Cmd`) declared by a committed `.fsi` baseline — Tier 1.
- Adds one new mandatory third-party dependency (`Silk.NET.OpenAL`, MIT) and a
  dynamically-linked LGPL native (OpenAL Soft).

## Lifecycle Notes
- Consumes `FS.GG.Audio.Core` (`001-audio-core`, shipReady).
- Next lifecycle action: `fsgg-sdd clarify --work 002-audio-host`.
