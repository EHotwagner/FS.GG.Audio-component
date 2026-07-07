---
schemaVersion: 1
workId: 002-audio-host
title: FS.GG.Audio.Host — pluggable playback host with Null + OpenAL (Silk.NET) backends
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# FS.GG.Audio.Host — pluggable playback host with Null + OpenAL (Silk.NET) backends Charter

## Identity
- Work id: `002-audio-host`
- Lifecycle stage: charter
- Status: chartered

`FS.GG.Audio.Host` is the host-side realization that turns the pure `AudioEffect` values
from `FS.GG.Audio.Core` into real sound, behind a narrow, pluggable `IAudioBackend` seam.
It ships two backends in this work item: a deterministic **Null/record backend** (the
default under test and headless — essentially `Core.Audio.interpret` behind the seam) and
a real **OpenAL device backend** built on `Silk.NET.OpenAL` (MIT bindings over OpenAL
Soft). It also exposes the Elmish `Cmd`/`Sub` authoring surface so a product drives audio
the same way it drives other effects. The audio edge is the analogue of the rendering
edge: **pure request → recorded evidence (contract, testable) → optional real projection
(host, degradable)**.

## Principles
- **Pure edge, effectful host** (constitution V). Game `update` keeps emitting
  `AudioEffect` values from `FS.GG.Audio.Core`; only this package opens a device. No change
  to `update`'s signature.
- **Degrade to zero.** No device, no assets, or a CI runner ⇒ the Null backend runs,
  records `AudioEvidence`, and the product is identical minus sound. A missing device is
  never a hard failure — it is an explicit, logged degrade.
- **Determinism under test** (constitution VI). The Null backend is byte-deterministic and
  hardware-free and is the default; the OpenAL backend is opt-in. Tests assert on
  `AudioEvidence` / recorded backend calls, never on audio samples.
- **Pluggable backend.** `IAudioBackend` is the narrowest possible device seam so a backend
  is swappable (Null ↔ OpenAL ↔ future miniaudio) without touching game code, and the
  component is never captive to one native dependency.
- **One-way dependencies.** `FS.GG.Audio.Host` depends only on `FS.GG.Audio.Core` +
  third-party audio libs (`Silk.NET.OpenAL`); nothing in FS-GG depends up into it. It does
  not touch Scene/Canvas/Skia — audio stays render-independent.
- **Permissive licensing only** (design-doc P5). Every mandatory dependency is MIT / MIT-0 /
  BSD / public-domain. The one LGPL component (OpenAL Soft native) is **dynamically linked
  and replaceable** — the LGPL-clean path for closed-source games. No GPL, no proprietary.
- **Public surface is declared** (constitution III). `IAudioBackend`, the backend
  constructors, and the `Audio.Cmd`/`Audio.Sub` surface are declared in `.fsi` with a
  committed surface baseline.

## Scope Boundaries
- **In:** the `IAudioBackend` seam; the Null/record backend (default, deterministic); a real
  OpenAL backend via `Silk.NET.OpenAL` mapping the four `Core.AudioEffect` cases
  (`PlaySfx`/`PlayMusic`/`StopMusic`/`SetMasterVolume`) to device calls; graceful
  degrade-to-Null when no device/library is present; the Elmish `Cmd`/`Sub` authoring
  surface; a minimal `SoundId`/`TrackId` → PCM `AssetResolver` seam sufficient to feed the
  OpenAL backend a buffer (WAV, built-in); headless tests over the Null backend + the
  effect→backend mapping via a recording fake.
- **Out (follow-up work items):**
  - `003` — the `AudioEngine` niceties: named buses (Master/Music/Sfx/Ui/Ambient), fades /
    cross-fades / ducking, 3D listener + emitters, the EFX effect graph.
  - decoders beyond built-in WAV (NVorbis OGG, optional NLayer MP3) and music streaming.
  - the additive `AudioEffect` variants deferred from `001-audio-core` (`PlaySfx3D`,
    `SetBusVolume`, `Duck`) — they land with the engine that implements their semantics.
  - the `miniaudio` alternate backend (kept as a documented option).
- **Out (coordination layer, not SDD):** publishing `FS.GG.Audio.Host` to the feed and any
  registry/roster/ADR change — handled on the cross-repo coordination track.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Honors constitution I (specify-before-implement), III (declared surface), V (pure edge /
  effect at the boundary), VI (test evidence; safe failure / explicit degrade), and VIII
  (observability — a missing device logs and degrades, never throws into game code).
- Tier 1: introduces a new public package surface (`FS.GG.Audio.Host`), so signatures,
  baseline, tests, and docs land together.

## Lifecycle Notes
- Consumes `FS.GG.Audio.Core` (work item `001-audio-core`, shipReady) as its request
  vocabulary; adds `Silk.NET.OpenAL` (MIT) as the only new mandatory package.
- Realizes the deferred host from the audio design doc
  (`FS-GG/FS.GG.Game/docs/reports/2026-07-05-game-audio-library-architecture.md`), backend
  selection = OpenAL primary (owner-confirmed 2026-07-07).
- Next lifecycle action: `fsgg-sdd specify --work 002-audio-host`.
