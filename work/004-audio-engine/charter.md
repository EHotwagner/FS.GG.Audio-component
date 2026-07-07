---
schemaVersion: 1
workId: 004-audio-engine
title: FS.GG.Audio.Engine — buses, fades/ducking, and 3D over FS.GG.Audio.Host
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

# FS.GG.Audio.Engine — buses, fades/ducking, and 3D over FS.GG.Audio.Host Charter

## Identity
- Work id: `004-audio-engine`
- Lifecycle stage: charter
- Status: chartered

`FS.GG.Audio.Engine` is the mixing/voice layer that sits between the pure
`AudioEffect` vocabulary and the device seam: a product feeds it a per-frame batch of
effects and it resolves them into **named buses** (Master/Music/Sfx/Ui/Ambient) with
independent gain, time-based **fades / cross-fades** and side-chain **ducking**, and a
3D **listener + per-voice emitters** (with a 2D stereo-pan-by-x convenience). It
realizes this through the existing `FS.GG.Audio.Host` `IAudioBackend` seam and keeps a
deterministic record path so the whole engine is testable with no device. It also
lands the three **additive `AudioEffect` variants** deferred from `001-audio-core`
(`PlaySfx3D`, `SetBusVolume`, `Duck`), a backward-compatible bump to the
`FS.GG.Audio.Core` surface. It realizes the "AudioEngine" layer designated as the next
item in the `002-audio-host` charter and the L2 layer of the audio architecture design.

## Principles
- **Determinism is the invariant that must not regress.** The engine advances as a
  pure, total step over its own state (bus gains, active voices, fade/duck envelopes,
  listener/emitter positions) against a fixed frame-time delta. Under test the default
  backend is Null/record: no device is opened and the recorded state/evidence *is* the
  proof (constitution V/VI, design §8).
- **Pure edge, effectful realization.** A product's `update` still only emits
  `AudioEffect` values; the engine turns accumulated state into backend calls at the
  boundary. No case of `AudioEffect` carries a device handle or closure.
- **Additive, non-breaking Core surface.** `PlaySfx3D` / `SetBusVolume` / `Duck` extend
  the `AudioEffect` DU without altering existing cases — a v1 game that never emits them
  is unaffected. Any `AudioEffect` addition is a contract-relevant change and travels
  with signatures + baseline + tests together (constitution III).
- **Degrade to zero, never throw.** Missing 3D support collapses emitters to
  non-positional voices at the requested gain; an unavailable backend degrades to the
  Null path. A device hiccup is silence, never an exception into game code
  (constitution VIII).
- **Public surface declared.** The `Engine`/bus/fade/3D surface and the Core additions
  are declared in `.fsi` with committed baselines.

## Scope Boundaries
- **In:**
  - A new `FS.GG.Audio.Engine` package layering on `FS.GG.Audio.Host` (`IAudioBackend`)
    and `FS.GG.Audio.Core`.
  - Named buses `Master/Music/Sfx/Ui/Ambient` with independent, clamped gain; the
    `SetMasterVolume`/`Sfx` semantics map onto `Master`/`Sfx`.
  - Time-based fades and cross-fades (linear and equal-power) and timed side-chain
    ducking (drop a bus under a stinger, then restore).
  - A 3D listener + per-voice emitter positions with a distance-attenuation model, plus
    a 2D `stereo-pan-by-x` convenience (`PlaySfx3D` with `z = 0`).
  - The additive `FS.GG.Audio.Core` `AudioEffect` variants `PlaySfx3D`, `SetBusVolume`,
    `Duck`, threaded through `Core.Audio.interpret`/record so recorded evidence covers
    them.
  - Any minimal, additive extension of the Host `IAudioBackend` seam the engine needs to
    realize per-voice gain / listener / position over time, keeping the Null/record
    backend the deterministic default.
  - Headless tests: the engine stepped against a recording backend, asserting bus gains,
    fade/duck envelopes, and emitter resolution over frames — no audio device.
  - `.fsi` surfaces + committed baselines for the Engine package and the Core additions.
- **Out (follow-up work items):**
  - The **native EFX effect graph** (reverb/echo/filters via OpenAL auxiliary sends) and
    the managed **`Dsp` micro-layer** (biquads / delay line / soft-clip / eq-power fades
    as pure `Span<float32>` kernels) — the heaviest, most device-coupled, least
    deterministic parts; a dedicated follow-up (`EffectSpec` DU + realization).
  - The `miniaudio` alternate backend (documented option).
  - Decoders beyond built-in WAV (NVorbis OGG / NLayer MP3) and music streaming.
  - An `Audio.Sub` playback-events subscription (`PlaybackFinished`/`LoopWrapped`) —
    still gated on a Host events seam; carried from `003-audio-elmish`.
  - Real-device numeric/audio verification — an opt-in manual/soak lane, never in the CI
    assertion path.
- **Out (coordination layer, not SDD):** publishing the packages and any
  registry/roster/ADR change — handled on the cross-repo coordination track. Note the
  `AudioEffect` additions are a contract-relevant `fs-gg-audio` capability change that
  must go through the Rendering surface-bump + registry reconcile.

## Policy Pointers
- SDD policy from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Honors constitution I (specify-before-implement), III (declared surface + baselines),
  V (pure step; effect at the boundary), VI (headless test evidence), VIII (safe
  failure / explicit degrade).
- Tier 1: a new public package surface (`FS.GG.Audio.Engine`) plus an additive public
  surface change to `FS.GG.Audio.Core` — signatures, baselines, tests, and docs land
  together.

## Lifecycle Notes
- Consumes `FS.GG.Audio.Host` (`002-audio-host`, shipReady) and `FS.GG.Audio.Core`
  (`001-audio-core`, shipReady); realizes the additive variants deferred there (001
  SB-004) and the AudioEngine designated by 002's charter.
- Realizes the L2 `AudioEngine` layer of the audio architecture design
  (`FS.GG.Game/docs/reports/2026-07-05-game-audio-library-architecture.md`, §5.1/§5.2/§7).
- Next lifecycle action: `fsgg-sdd specify --work 004-audio-engine`.
