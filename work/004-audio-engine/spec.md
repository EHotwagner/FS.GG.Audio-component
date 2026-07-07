---
schemaVersion: 1
workId: 004-audio-engine
title: Audio Engine
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Audio Engine Specification

Prose status: specified

## User Value
A product feeds a per-frame batch of `AudioEffect` values to `FS.GG.Audio.Engine` and gets named-bus mixing, time-based fades/cross-fades and side-chain ducking, and 3D listener/emitter positioning — all realized through the `FS.GG.Audio.Host` backend, and all reproducible on a deterministic, device-free record path so the audio can be tested headless.

## Scope
- SB-001: A new `FS.GG.Audio.Engine` package layering on `FS.GG.Audio.Host` (`IAudioBackend`) and `FS.GG.Audio.Core`, providing named buses, fades/cross-fades, ducking, and 3D listener/emitter positioning.
- SB-002: Additive `AudioEffect` variants `PlaySfx3D`, `SetBusVolume`, `Duck` in `FS.GG.Audio.Core`, threaded through `Core.Audio.interpret`/record, plus any minimal additive extension of the Host `IAudioBackend` seam the engine needs to realize per-voice/bus/listener control.
- SB-003: The native EFX effect graph and the managed DSP micro-layer are out — a dedicated follow-up.
- SB-004: The `miniaudio` alternate backend, decoders beyond built-in WAV, music streaming, and an `Audio.Sub` playback-events surface are out.

## Non-Goals
- SB-005: Do not implement real-device numeric/audio verification in the CI assertion path; the device backend stays behind an opt-in manual lane.
- SB-006: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a product author, I mix game audio through named buses with fades, ducking, and 3D positioning by feeding `AudioEffect` batches to the engine once per frame, and it degrades safely when a capability or device is absent.
- US-002 (P1): As a maintainer, the engine and the additive Core surface stay deterministic and headless-testable, with committed `.fsi` baselines and non-breaking Core evidence.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given the buses Master/Music/Sfx/Ui/Ambient, when a voice plays on a bus, then its effective gain equals request-gain × its bus gain × Master gain, each clamped to [0,1].
- AC-002 [US-001] [FR-002]: Given a running engine, when PlaySfx/PlaySfx3D and PlayMusic are stepped, then the sfx voice is acquired on the Sfx bus and the music voice on the Music bus, a new PlayMusic replaces the current music voice, and StopMusic clears it.
- AC-003 [US-001] [FR-003]: Given a fade of a bus from its current gain to a target over duration D, when the engine is stepped by frame deltas summing to D, then the bus gain reaches the target exactly, and a cross-fade ramps one source down while another ramps up over the same window.
- AC-004 [US-001] [FR-004]: Given a Duck(bus, amount, ms) request, when the engine is stepped across the attack and release, then the bus gain is attenuated by `amount` over the attack and restored afterward as a deterministic envelope.
- AC-005 [US-001] [FR-005]: Given a listener and an emitter position, when a PlaySfx3D voice is stepped, then its gain is the distance-attenuation model applied to (request-gain × bus gain); with z=0 it pans by x; and when 3D is unavailable it degrades to a non-positional voice at the bus-scaled gain.
- AC-006 [US-002] [FR-006]: Given the extended `FS.GG.Audio.Core` `AudioEffect`, when PlaySfx3D/SetBusVolume/Duck are interpreted, then they appear in `AudioEvidence` in order and the recorded evidence for the original four cases is unchanged.
- AC-007 [US-002] [FR-007]: Given the same inputs, when the engine is stepped twice against a Null/record backend, then it opens no device, never throws, and produces identical realized calls and evidence.
- AC-008 [US-001] [FR-008]: Given an unavailable or failing backend, or a missing 3D capability, when the engine is stepped, then it degrades to the Null path / non-positional voices and no exception crosses into game code.
- AC-009 [US-002] [FR-009]: Given the committed `FS.GG.Audio.Engine` and `FS.GG.Audio.Core` `.fsi` baselines, when the public surfaces are compared, then the Engine surface and the additive Core surface match with no drift.

## Functional Requirements
- FR-001: The engine MUST maintain the buses Master, Music, Sfx, Ui, Ambient, each with an independent gain clamped to [0,1]; `SetMasterVolume` MUST set Master and `SetBusVolume(bus, v)` MUST set the named bus; a voice's effective gain MUST equal request-gain × its bus gain × Master gain. (covers AC-001)
- FR-002: The engine MUST route PlaySfx/PlaySfx3D to the Sfx bus and PlayMusic to the Music bus, acquiring the sfx voice per one-shot and replacing the single music voice on a new PlayMusic, with StopMusic clearing it. (covers AC-002)
- FR-003: The engine MUST support a timed fade of a bus (or the music voice) from its current gain to a target over a duration, advanced by the per-frame delta and reaching the target at/after the duration, and a cross-fade that ramps one source down while another ramps up over the same window. (covers AC-003)
- FR-004: A `Duck(bus, amount, ms)` request MUST attenuate the bus gain by `amount` over the attack window and then restore it, as a deterministic envelope advanced by frame delta. (covers AC-004)
- FR-005: Given a listener and an emitter position, the engine MUST attenuate a 3D voice by a distance-attenuation model applied to (request-gain × bus gain), MUST provide a 2D stereo-pan-by-x convenience via `PlaySfx3D` with z=0, and MUST degrade a 3D voice to a non-positional voice at the bus-scaled gain when 3D is unavailable. (covers AC-005)
- FR-006: `FS.GG.Audio.Core`'s `AudioEffect` MUST gain the additive cases `PlaySfx3D`, `SetBusVolume`, `Duck` without altering the existing cases, and `Core.Audio.interpret`/record MUST thread them into `AudioEvidence` while leaving evidence for the original cases unchanged. (covers AC-006)
- FR-007: The engine MUST advance as a pure, total step over its state against a fixed frame delta and a Null/record backend, opening no device and never throwing, such that identical inputs produce identical realized calls and evidence. (covers AC-007)
- FR-008: An unavailable or failing backend MUST degrade to the Null path and a missing 3D capability MUST degrade to non-positional voices, with no exception crossing into game code. (covers AC-008)
- FR-009: The public `FS.GG.Audio.Engine` surface and the additive `FS.GG.Audio.Core` surface MUST be declared by committed `.fsi` baselines that match the shipped surfaces with no drift. (covers AC-009)

## Ambiguities
- AMB-001: The distance-attenuation model and its default parameters are unspecified — inverse vs linear vs exponential clamped, and the `refDistance`/`maxDistance`/`rolloff` defaults.
- AMB-002: How the engine drives the device is unspecified — by extending the Host `IAudioBackend` seam with per-voice/bus/listener control ops, or by pre-attenuating and re-emitting effects through the existing narrow `Play` seam.
- AMB-003: The default fade/cross-fade curve (linear vs equal-power) and whether `Duck` auto-restores after the attack or holds until explicitly released are unspecified.

## Public Or Tool-Facing Impact
- This specification introduces a new public package surface (`FS.GG.Audio.Engine`) and an additive change to the `FS.GG.Audio.Core` public surface — a contract-relevant `fs-gg-audio` capability change.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 004-audio-engine`.
