# Contract — `FS.GG.Audio.Engine` surface + additive `Core`/`Host` bumps

Status: planned (Tier 1, contracted). Signatures-first declaration (constitution III) for work
item `004-audio-engine`. The `.fs` implementations and the committed `docs/api-surface/**`
baselines MUST match these signatures. Resolves DEC-001..DEC-003; defers DEC-004 (Doppler/full 3D).

## Layering & dependencies
- `FS.GG.Audio.Engine` depends on `FS.GG.Audio.Host` + `FS.GG.Audio.Core` + `FSharp.Core` only.
  No `FS.GG.UI.*`, no `SkiaSharp`, no `Elmish` (the Elmish bridge for engine ops is a later item).
- The engine is a **pure deterministic mixing/state model** (DEC-002). It exposes its computed
  state for headless assertions and realizes through the `Host` backend: one-shots via the
  existing `IAudioBackend.Play`; continuous bus/listener control via the optional
  `IMixingBackend` when the backend implements it, else it **degrades** (FR-008).

---

## Additive `FS.GG.Audio.Core` surface bump (FR-006)

New public `Bus` type and three additive `AudioEffect` cases; existing cases and `AudioEvidence`
are unchanged (a v1 game that never emits the new cases records identical evidence).

```fsharp
namespace FS.GG.Audio.Core

/// Named mixer bus (004). Master scales everything; SetMasterVolume maps to Master.
type Bus =
    | Master
    | Music
    | Sfx
    | Ui
    | Ambient

type AudioEffect =
    | PlaySfx of sound: SoundId * volume: float
    | PlayMusic of track: TrackId * loop: bool
    | StopMusic
    | SetMasterVolume of level: float
    // --- additive (004-audio-engine); existing cases above are untouched ---
    | PlaySfx3D of sound: SoundId * x: float * y: float * z: float * volume: float
    | SetBusVolume of bus: Bus * level: float
    | Duck of bus: Bus * amount: float * milliseconds: float

[<RequireQualifiedAccess>]
module Audio =
    // ... existing values/functions unchanged ...

    /// Smart constructor for a positional sfx request; clamps the carried volume. `z = 0` is the
    /// 2D stereo-pan-by-x convenience.
    val playSfx3D: sound: SoundId -> x: float -> y: float -> z: float -> volume: float -> AudioEffect
    /// Smart constructor for a per-bus volume request; clamps level to [0,1].
    val setBusVolume: bus: Bus -> level: float -> AudioEffect
    /// Smart constructor for a timed duck; clamps amount to [0,1] and milliseconds to >= 0.
    val duck: bus: Bus -> amount: float -> milliseconds: float -> AudioEffect
    // `record`/`interpret` thread the three new cases into AudioEvidence.Requested in order.
```

## Additive `FS.GG.Audio.Host` surface bump (DEC-002)

The shipped `IAudioBackend` is **unchanged**. A new *optional* capability interface is added; a
backend MAY implement it to realize continuous mixing/spatial control. The engine feature-detects
it and degrades to plain `Play` when it is absent — so existing backends stay valid.

```fsharp
namespace FS.GG.Audio.Host

open FS.GG.Audio.Core

/// Optional mixing/spatial control a backend MAY implement alongside IAudioBackend (004). The
/// engine feature-detects it (`:? IMixingBackend`); a backend without it degrades to Play, with
/// bus/fade/duck folded into one-shot gains and 3D collapsed to non-positional voices (FR-008).
type IMixingBackend =
    inherit IAudioBackend
    /// Set a bus's realized gain (already clamped). Called as fades/ducks advance.
    abstract member SetBusGain: bus: Bus * gain: float -> unit
    /// Set the listener position (metres). Called when the listener moves.
    abstract member SetListener: x: float * y: float * z: float -> unit
    /// Play a positional one-shot with a pre-resolved effective gain and pan in [-1, 1].
    abstract member PlayAt: sound: SoundId * gain: float * pan: float -> unit
```

---

## New `FS.GG.Audio.Engine` surface (FR-001..FR-005, FR-007..FR-009)

```fsharp
namespace FS.GG.Audio.Engine

open FS.GG.Audio.Core
open FS.GG.Audio.Host

/// Distance-attenuation configuration (DEC-001). Defaults: refDistance 1, rolloff 1, uncapped.
type SpatialConfig =
    { RefDistance: float
      Rolloff: float
      MaxDistance: float option }

/// A one-shot voice realized on a step, exposed for headless assertions. `Pan` in [-1, 1];
/// `Positional` is false when 3D was unavailable / the voice is non-spatial.
type Voice =
    { Sound: SoundId
      Bus: Bus
      RequestGain: float
      EffectiveGain: float   // requestGain * busGain * masterGain * distanceAttenuation
      Pan: float
      Positional: bool }

/// The mixing/voice engine. Holds bus gains, active fade/duck envelopes, the listener, and the
/// single music voice. Advanced once per frame by `step`.
[<Sealed>]
type T =
    /// Realized gain of a bus in [0,1] (Master folded in for non-Master buses on request).
    member BusGain: bus: Bus -> float
    /// Realized gain of the current music voice (0 when none).
    member MusicGain: float
    /// Current listener position.
    member Listener: float * float * float
    /// The one-shot voices realized on the most recent `step`, in dispatch order.
    member LastVoices: Voice list

[<RequireQualifiedAccess>]
module Engine =
    val defaultSpatial: SpatialConfig

    /// Create an engine over a backend (SpatialConfig = defaultSpatial). Opens no device itself.
    val create: backend: IAudioBackend -> T
    val createWith: config: SpatialConfig -> backend: IAudioBackend -> T

    /// Advance the engine by `dt` seconds and apply a per-frame batch of effects, in order:
    /// resolves buses (FR-001/-002), advances fade/duck envelopes (FR-003/-004), resolves 3D
    /// (FR-005), and realizes through the backend. Pure/total; never throws (FR-007/-008).
    val step: engine: T -> dt: float -> effects: AudioEffect list -> unit

    /// Install a timed bus fade to `target` over `seconds` (linear ramp; DEC-003). Realized as
    /// `step` advances.
    val fadeBus: engine: T -> bus: Bus -> target: float -> seconds: float -> unit
    /// Install an equal-power cross-fade: `fromBus` down and `toBus` up over `seconds` (DEC-003).
    val crossFade: engine: T -> fromBus: Bus -> toBus: Bus -> seconds: float -> unit
```

## Realization & determinism notes
- **Effective gain** = `requestGain × busGain × masterGain × distanceAttenuation`, each factor
  clamped so the product stays in `[0,1]`. Distance attenuation (DEC-001):
  `att = refDistance / (refDistance + rolloff × (max(d, refDistance) − refDistance))`, `d` the
  planar (x,z) distance to the listener; `att = 1` for non-positional voices; capped at
  `maxDistance` when set. `pan = clamp((emitterX − listenerX) / refDistance, -1, 1)`.
- **Fades/ducks** are envelopes advanced by `dt`; `fadeBus` linear, `crossFade` equal-power,
  `Duck` auto-restores after its attack window (DEC-003). Summing `dt`s to the duration reaches
  the target exactly (FR-003).
- **Degrade (FR-008):** no `IMixingBackend` ⇒ bus/fade/duck gains are folded into subsequently
  played one-shot `EffectiveGain` (no continuous control) and 3D voices collapse to
  `Positional = false` at bus-scaled gain; a throwing backend is caught and treated as silence.
- **Baselines (FR-009):** `docs/api-surface/FS.GG.Audio.Engine/Engine.fsi` and the updated
  `docs/api-surface/FS.GG.Audio.Core/Audio.fsi` are committed and asserted byte-identical to the
  sources by a drift test in each package's suite.
