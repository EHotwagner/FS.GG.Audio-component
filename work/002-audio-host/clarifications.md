---
schemaVersion: 1
workId: 002-audio-host
stage: clarify
sourceSpec: work/002-audio-host/spec.md
---

# Clarifications

## Source Specification
- work/002-audio-host/spec.md

## Clarification Questions
- **CQ-001** (AMB-001): Which `Silk.NET.OpenAL` package(s) and version, and how does the OpenAL Soft native ship?
- **CQ-002** (AMB-002): How is the OpenAL backend tested when CI has no audio device?
- **CQ-003** (AMB-003): What shape is the `AssetResolver`, and is music (`TrackId`) streaming in scope now?
- **CQ-004** (AMB-004): Where does the Elmish `Cmd`/`Sub` surface live, given the host cannot depend on `FS.GG.UI`?

## Answers
- CQ-001 → Pin the latest stable `Silk.NET.OpenAL` plus the `Silk.NET.OpenAL.Soft.Native` runtime package, so OpenAL Soft (LGPL) ships dynamically-linked and user-replaceable with no host preinstall (design-doc P5). Resolves AMB-001.
- CQ-002 → A **recording fake** implementing `IAudioBackend` asserts the `AudioEffect`→seam-call mapping (FR-003 shape), and a **degrade-path test** asserts OpenAL-requested-with-no-device falls back to Null (FR-004). The real OpenAL backend is exercised only manually/opt-in; no test opens a device or asserts on samples (FR-008). Resolves AMB-002.
- CQ-003 → `AssetResolver` is a pure `SoundId -> byte[] option` (PCM) with a built-in WAV reader; `TrackId`/music is played as a (possibly looping) buffer with no streaming this item — streaming + non-WAV decoders are deferred (SB-008). An unresolved id is a recorded no-op, not a throw (FR-005). Resolves AMB-003.
- CQ-004 → Defer the Elmish surface to a separate `FS.GG.Audio.Elmish` package (its own work item); `FS.GG.Audio.Host` ships only the imperative `Audio.play` drive (FR-006) and never references `FS.GG.UI.*` (FR-007). Resolves AMB-004.

## Decisions
- **DEC-001** [CQ-001] [AMB:AMB-001] [FR-007]: Depend on `Silk.NET.OpenAL` (latest stable) + `Silk.NET.OpenAL.Soft.Native`; OpenAL Soft is dynamically linked and replaceable.
- **DEC-002** [CQ-002] [AMB:AMB-002] [FR-008]: Test via a recording fake `IAudioBackend` (mapping assertions) + a degrade-path test (no device → Null); no device access, no sample assertions in the suite.
- **DEC-003** [CQ-003] [AMB:AMB-003] [FR-005]: `AssetResolver = SoundId -> byte[] option` (WAV PCM), music as a looping buffer, no streaming; unresolved id → recorded no-op.

## Accepted Deferrals
- **DEC-004** [CQ-004] [AMB:AMB-004] [FR-006]: The Elmish `Cmd`/`Sub` authoring surface is deferred to a separate `FS.GG.Audio.Elmish` package (its own future work item). This item ships only the imperative `Audio.play : IAudioBackend -> AudioEffect list -> unit` drive; `FS.GG.Audio.Host` stays free of any `FS.GG.UI.*` dependency.

## Remaining Ambiguity
- None. AMB-001, AMB-002, AMB-003, and AMB-004 are all resolved above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 002-audio-host`.
