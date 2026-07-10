namespace FS.GG.Audio.Elmish

open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Engine

// Reach Core's Audio module without colliding with this package's own `Audio` module below.
module CoreAudio = FS.GG.Audio.Core.Audio

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
            Elmish.Cmd.ofEffect (fun _dispatch ->
                for effect in effects do
                    backend.Play effect)

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
