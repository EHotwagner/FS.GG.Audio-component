# FS.GG.Audio.Elmish

The Elmish authoring bridge for the [FS.GG.Audio](https://github.com/FS-GG/FS.GG.Audio) component.
A product's `update` returns ordinary `Elmish.Cmd` values; the Elmish runtime executes them,
playing the underlying `FS.GG.Audio.Core` effects.

The commands dispatch no message — audio is fire-and-forget — so `update` stays a pure
`Model -> Model * Cmd<'msg>`. Depends on [Elmish](https://elmish.github.io/) (MIT); never on
`FS.GG.UI`.

## Two paths: raw backend vs. the Engine

The difference is load-bearing — pick deliberately:

- **`Audio.Cmd.ofEffects` / `playSfx` / `playMusic` / `stopMusic` / `setMasterVolume`** play
  **straight at an [`FS.GG.Audio.Host`](https://www.nuget.org/packages/FS.GG.Audio.Host)
  `IAudioBackend`**. There is **no mixing**: `SetBusVolume`/`Duck` are backend no-ops and `PlaySfx3D`
  degrades to non-positional. Use these for simple fire-and-forget playback.
- **`Audio.Cmd.ofEngine engine dt effects`** routes the `AudioEffect` batch through
  [`FS.GG.Audio.Engine`](https://www.nuget.org/packages/FS.GG.Audio.Engine)`.step`, so **bus volume,
  master scaling, ducking, and 3D positioning apply**. Reach for it whenever those semantics matter.
  The caller owns the `Engine.T` and the per-frame `dt`, exactly as a direct `Engine.step` caller
  does — and because time-based envelopes (a `Duck`, or a fade you install with `Engine.fadeBus` /
  `Engine.crossFade`, which are engine methods rather than effects) advance only as the engine is
  stepped, drive `ofEngine` every frame for them to progress and restore.

## Install

```sh
dotnet add package FS.GG.Audio.Elmish
```

## Quick start

```fsharp
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Elmish

let backend = NullBackend.create () :> IAudioBackend

let update msg model =
    match msg with
    | Jumped -> model, Audio.Cmd.playSfx backend (SoundId "jump") 0.8
    | LevelStarted -> model, Audio.Cmd.playMusic backend (TrackId "bgm") true
    | LevelEnded -> model, Audio.Cmd.stopMusic backend
    | VolumeChanged v -> { model with Volume = v }, Audio.Cmd.setMasterVolume backend v
```

Batch several **raw** effects into one command with `Audio.Cmd.ofEffects` (no mixing — every effect
goes straight at the backend):

```fsharp
Audio.Cmd.ofEffects backend [ Audio.playSfx (SoundId "hit") 1.0
                              Audio.playSfx (SoundId "clink") 0.6 ]
```

When you need bus mixing, fades, ducking, or 3D, drive the **Engine** instead — the same batch, but
`Duck`/`SetBusVolume`/`PlaySfx3D` now carry their real semantics:

```fsharp
open FS.GG.Audio.Engine

let engine = Engine.create backend   // hold this in your model; create once

let update msg model =
    match msg with
    // dt is your frame delta; ofEffects would drop the duck, ofEngine honours it.
    | Hit dt -> model, Audio.Cmd.ofEngine engine dt [ Audio.duck Music 0.5 250.0
                                                       Audio.playSfx3D (SoundId "hit") 3.0 0.0 0.0 1.0 ]
```

## Surface

- `Audio.Cmd.ofEffects backend effects` — play a batch **straight through the backend** in dispatch
  order (no mixing).
- `Audio.Cmd.ofEngine engine dt effects` — advance `engine` by `dt` and realize the batch through
  `FS.GG.Audio.Engine.step` (bus mixing / fades / ducking / 3D apply).
- `Audio.Cmd.playSfx` / `playMusic` / `stopMusic` / `setMasterVolume` — one-effect raw-backend
  conveniences mirroring the `Core.Audio` smart constructors.

Every command dispatches no message.

## Determinism

Point the commands at `NullBackend` and the Elmish loop stays headless: no device is opened, and
the backend's recorded `AudioEvidence` is the assertion surface.

## License

MIT — see [LICENSE](https://github.com/FS-GG/FS.GG.Audio/blob/main/LICENSE).
