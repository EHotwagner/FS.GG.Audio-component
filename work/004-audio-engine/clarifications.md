---
schemaVersion: 1
workId: 004-audio-engine
stage: clarify
sourceSpec: work/004-audio-engine/spec.md
---

# Clarifications

## Source Specification
- work/004-audio-engine/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Which distance-attenuation model and default parameters should a 3D voice use?
- **CQ-002** (AMB-002): How does the engine drive the device — by extending the Host `IAudioBackend` seam, or by re-emitting pre-attenuated effects through the existing narrow `Play` seam?
- **CQ-003** (AMB-003): What are the default fade/cross-fade curves, and does `Duck` auto-restore or hold until released?

## Answers
- CQ-001 → OpenAL-style **inverse-distance, clamped**. Gain factor = `refDistance / (refDistance + rolloff × (max(d, refDistance) − refDistance))`, clamped to `[0,1]`, with defaults `refDistance = 1`, `rolloff = 1`, `maxDistance` uncapped unless set. `PlaySfx3D` with `z = 0` additionally pans by the emitter's x relative to the listener; attenuation uses planar distance. (resolves AMB-001)
- CQ-002 → The engine is a **pure deterministic mixing/state model** whose realized output is recorded, and it drives the device through a **minimal additive extension of the Host `IAudioBackend` seam** (per-voice gain, listener + emitter position, per-voice stop). The existing `Play(effect)` stays; the extension is additive so existing backends and games are unaffected. Null/record remains the deterministic default. (resolves AMB-002)
- CQ-003 → Cross-fades use an **equal-power** curve; a single-target bus fade uses a **linear** ramp. `Duck` is a self-contained timed envelope that **auto-restores** after the attack window (ramp down by `amount`, then ramp back); no explicit release call in this cut. (resolves AMB-003)

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-005]: 3D voices attenuate by an inverse-distance-clamped model with defaults `refDistance = 1`, `rolloff = 1`, `maxDistance` uncapped; `z = 0` pans by x. This is the total, device-free formula the record path asserts.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-007]: The engine is a pure state model realized through a minimal, additive extension of the Host `IAudioBackend` seam (per-voice gain, listener/emitter position, per-voice stop) via an optional `IMixingBackend`; the Null/record backend stays the default and existing implementations stay valid (also realizes FR-001 bus mixing). A contract-relevant additive change to the shipped `FS.GG.Audio.Host` surface.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-003]: Cross-fades are equal-power, single-target bus fades are linear (FR-003), and `Duck` auto-restores after its attack window as a self-contained timed envelope (FR-004).

## Accepted Deferrals
- **DEC-004** [FR-005] acceptedDeferral: Doppler (emitter velocity) and non-planar full-3D spatialization beyond distance attenuation + x-pan are deferred with the EFX/DSP follow-up — recorded, not dropped.

## Remaining Ambiguity
- None. AMB-001, AMB-002, and AMB-003 are resolved by DEC-001..DEC-003 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 004-audio-engine`.
