# Contract — `FS.GG.Audio.Host` public surface

Status: planned (Tier 1, contracted). Signatures-first declaration (constitution III) for work
item `002-audio-host`. The `.fs` implementation and the committed `docs/api-surface/**` baseline
MUST match this signature.

## Dependencies (FR-007)
`FS.GG.Audio.Core` (the `AudioEffect`/`AudioEvidence` vocabulary) + `Silk.NET.OpenAL`
(+ `Silk.NET.OpenAL.Soft.Native`, dynamically linked) + `FSharp.Core`. No `FS.GG.UI.*`, no
`SkiaSharp`, no Scene/Canvas.

## Declared signature (`src/FS.GG.Audio.Host/Host.fsi`)

```fsharp
namespace FS.GG.Audio.Host

open FS.GG.Audio.Core

/// Public contract type. Resolves a product-owned SoundId to PCM bytes (WAV) for a device
/// backend. `None` => unresolved: the host treats it as a recorded no-op, never a throw (FR-005).
/// The host does NOT own the id->asset mapping — the product supplies this function.
type AssetResolver = SoundId -> byte[] option

/// Public contract type. The narrow device seam (FR-001). Implementations: the Null/record
/// backend (default, deterministic) and the OpenAL backend (Silk.NET). Game-facing code never
/// names a concrete backend — it holds an IAudioBackend.
type IAudioBackend =
    inherit System.IDisposable
    /// Realize one requested effect. Volumes arrive already clamped by Core. Never throws; a
    /// backend that cannot act degrades to a no-op.
    abstract member Play: effect: AudioEffect -> unit

/// Public contract module. The imperative drive + backend constructors.
[<RequireQualifiedAccess>]
module Audio =

    /// Fold a per-frame batch of requests through the backend in dispatch order (FR-006).
    /// The product's `update` is unchanged: it emits AudioEffect values; this plays them.
    val play: backend: IAudioBackend -> effects: AudioEffect list -> unit

/// Public contract module. The deterministic, headless record-only backend — the default and
/// the test/CI backend (FR-002).
[<RequireQualifiedAccess>]
module NullBackend =

    /// A record-only backend. Opens no device, never throws.
    type T =
        interface IAudioBackend
        /// Accumulated evidence — equal to `FS.GG.Audio.Core.Audio.interpret` of the same batch.
        member Evidence: AudioEvidence

    /// Create a fresh Null backend.
    val create: unit -> T

/// Public contract module. The real OpenAL device backend (Silk.NET.OpenAL) (FR-003, FR-004).
[<RequireQualifiedAccess>]
module OpenAlBackend =

    /// Attempt to open an OpenAL device and return a backend that plays through it. If the device
    /// or the OpenAL Soft native library is unavailable, log the reason and return a Null backend
    /// instead (degrade-to-zero, FR-004) — the returned IAudioBackend is always usable, never null,
    /// never throwing into game code.
    val create: resolver: AssetResolver -> IAudioBackend
```

## Behavioral invariants
- **[FR-002]** `NullBackend`: no device opened; `Evidence` after playing a batch equals
  `Core.Audio.interpret` of that batch; total, never throws.
- **[FR-003]** `OpenAlBackend` maps `PlaySfx`→one-shot source, `PlayMusic(loop)`→looping source,
  `StopMusic`→source stop, `SetMasterVolume`→listener gain, via `Silk.NET.OpenAL`.
- **[FR-004]** `OpenAlBackend.create` never throws on a missing device/library — it degrades to
  `NullBackend` and logs.
- **[FR-005]** `AssetResolver` supplied by the caller; unresolved id → recorded no-op.
- **[FR-006]** `Audio.play` preserves dispatch order.
- **[FR-008]** Tests use a recording fake `IAudioBackend` + a degrade-path test; no device, no
  sample assertions.

## Packaging
`FS.GG.Audio.Host`, version `$(FsGgAudioVersion)` = `0.1.0-preview.1` (same axis as
`FS.GG.Audio.Core`). `net10.0`, house style (warnings-as-errors, deterministic, locked restore).
