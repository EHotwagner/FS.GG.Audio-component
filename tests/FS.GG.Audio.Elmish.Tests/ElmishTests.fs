module FS.GG.Audio.Elmish.Tests.ElmishTests

open Expecto
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Elmish

// Disambiguate the three `Audio` modules in scope (Core vocabulary, Host drive, this Cmd surface).
module CoreAudio = FS.GG.Audio.Core.Audio
module AudioCmd = FS.GG.Audio.Elmish.Audio.Cmd

type private RecordingBackend() =
    let calls = ResizeArray<AudioEffect>()
    member _.Calls = List.ofSeq calls
    interface IAudioBackend with
        member _.Play(e) = calls.Add e
        member _.Dispose() = ()

// Execute an Elmish command's effects with a recording dispatch; return the dispatched messages.
let private run (cmd: Elmish.Cmd<int>) : int list =
    let dispatched = ResizeArray<int>()
    for effect in cmd do
        effect (fun m -> dispatched.Add m)
    List.ofSeq dispatched

[<Tests>]
let tests =
    testList "FS.GG.Audio.Elmish" [

        test "Audio.Cmd.ofEffects plays effects in order and dispatches nothing (FR-001, FR-003)" {
            let fake = new RecordingBackend()
            let effects =
                [ CoreAudio.playSfx (SoundId "a") 0.5
                  CoreAudio.playMusic (TrackId "m") true
                  CoreAudio.stopMusic ]
            let cmd: Elmish.Cmd<int> = AudioCmd.ofEffects (fake :> IAudioBackend) effects
            let dispatched = run cmd
            Expect.equal fake.Calls effects "each effect played through the backend, in order"
            Expect.isEmpty dispatched "fire-and-forget: no message dispatched"
        }

        test "single-effect constructors play exactly the matching Core.Audio effect (FR-002)" {
            let fake = new RecordingBackend()
            let b = fake :> IAudioBackend
            run (AudioCmd.playSfx b (SoundId "x") 2.0) |> ignore
            run (AudioCmd.playMusic b (TrackId "m") true) |> ignore
            run (AudioCmd.stopMusic b) |> ignore
            run (AudioCmd.setMasterVolume b 0.3) |> ignore
            Expect.equal
                fake.Calls
                [ CoreAudio.playSfx (SoundId "x") 2.0
                  CoreAudio.playMusic (TrackId "m") true
                  CoreAudio.stopMusic
                  CoreAudio.setMasterVolume 0.3 ]
                "each Cmd constructor plays its matching Core effect (volumes clamped by Core)"
        }

        test "an empty batch plays nothing and dispatches nothing (FR-003)" {
            let fake = new RecordingBackend()
            let dispatched = run (AudioCmd.ofEffects (fake :> IAudioBackend) [])
            Expect.isEmpty fake.Calls "no effects played"
            Expect.isEmpty dispatched "no message dispatched"
        }
    ]
