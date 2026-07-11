namespace FS.GG.Audio.Elmish

open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Engine

// Reach Core's Audio module without colliding with this package's own `Audio` module below.
module CoreAudio = FS.GG.Audio.Core.Audio

// Likewise Host's `Audio` module: `ofEffects` DELEGATES to its `play` rather than re-driving the
// backend itself (#29), so the raw path has one implementation, not two.
module HostAudio = FS.GG.Audio.Host.Audio

[<RequireQualifiedAccess>]
module Audio =

    [<RequireQualifiedAccess>]
    module Cmd =

        let ofEffects (backend: IAudioBackend) (effects: AudioEffect list) : Elmish.Cmd<'msg> =
            // A Cmd is a description; the effect (playing through the backend) runs only when the
            // Elmish runtime executes it. It ignores dispatch, so no message is produced.
            // This is the RAW-BACKEND bridge: effects go straight at IAudioBackend with no bus
            // mixing/fades/ducking/3D — SetBusVolume/Duck are backend no-ops and PlaySfx3D degrades
            // to non-positional. Use `ofEngine` when those semantics matter.
            //
            // `HostAudio.play`, NOT an inline `for` over `backend.Play` (#29). The loop was a second
            // implementation of "fold a batch through the backend in dispatch order", and the two had
            // to stay in agreement by hand — only Host's has the FR-006 dispatch-order test behind it.
            // Worse, it bypassed the #27 diagnostic: the SetBusVolume/Duck drop above is SILENT, and
            // an Elmish product with a dead volume slider got a dropped effect, no error, and nothing
            // on any channel. Delegating makes this path inherit the warning for free.
            Elmish.Cmd.ofEffect (fun _dispatch -> HostAudio.play backend effects)

        let ofEngine (engine: T) (dt: float) (effects: AudioEffect list) : Elmish.Cmd<'msg> =
            // Route the batch THROUGH the Engine rather than straight at the backend, so bus volume,
            // master scaling, ducking, and 3D positioning apply (fades/cross-fades are separate
            // Engine methods, not AudioEffects). `Engine.step` advances the engine by `dt` and
            // realizes the batch through the backend it was built over; time-based envelopes progress
            // only as it is stepped, so the caller drives this each frame. The caller owns the
            // `Engine.T` and the per-frame `dt`, exactly as a direct `step` caller does. It dispatches
            // no message, so `update` stays pure and the audio is fire-and-forget like the rest of
            // this surface.
            Elmish.Cmd.ofEffect (fun _dispatch -> Engine.step engine dt effects)

        let playSfx (backend: IAudioBackend) (sound: SoundId) (volume: float) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.playSfx sound volume ]

        let playMusic (backend: IAudioBackend) (track: TrackId) (loop: bool) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.playMusic track loop ]

        let stopMusic (backend: IAudioBackend) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.stopMusic ]

        let setMasterVolume (backend: IAudioBackend) (level: float) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.setMasterVolume level ]
