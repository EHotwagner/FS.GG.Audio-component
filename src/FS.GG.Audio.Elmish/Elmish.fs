namespace FS.GG.Audio.Elmish

open FS.GG.Audio.Core
open FS.GG.Audio.Host

// Reach Core's Audio module without colliding with this package's own `Audio` module below.
module CoreAudio = FS.GG.Audio.Core.Audio

[<RequireQualifiedAccess>]
module Audio =

    [<RequireQualifiedAccess>]
    module Cmd =

        let ofEffects (backend: IAudioBackend) (effects: AudioEffect list) : Elmish.Cmd<'msg> =
            // A Cmd is a description; the effect (playing through the backend) runs only when the
            // Elmish runtime executes it. It ignores dispatch, so no message is produced.
            Elmish.Cmd.ofEffect (fun _dispatch ->
                for effect in effects do
                    backend.Play effect)

        let playSfx (backend: IAudioBackend) (sound: SoundId) (volume: float) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.playSfx sound volume ]

        let playMusic (backend: IAudioBackend) (track: TrackId) (loop: bool) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.playMusic track loop ]

        let stopMusic (backend: IAudioBackend) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.stopMusic ]

        let setMasterVolume (backend: IAudioBackend) (level: float) : Elmish.Cmd<'msg> =
            ofEffects backend [ CoreAudio.setMasterVolume level ]
