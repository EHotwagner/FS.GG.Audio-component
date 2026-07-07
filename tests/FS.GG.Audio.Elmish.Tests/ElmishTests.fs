module FS.GG.Audio.Elmish.Tests.ElmishTests

open System.IO
open Expecto
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Elmish

// Disambiguate the three `Audio` modules in scope (Core vocabulary, Host drive, this Cmd surface).
module CoreAudio = FS.GG.Audio.Core.Audio
module AudioCmd = FS.GG.Audio.Elmish.Audio.Cmd

// Walk up from the test binary to the repo root (the directory holding the solution file).
let private repoRoot () =
    let mutable dir = DirectoryInfo(System.AppContext.BaseDirectory)
    while dir <> null && not (File.Exists(Path.Combine(dir.FullName, "FS.GG.Audio.slnx"))) do
        dir <- dir.Parent
    if isNull dir then failwith "repo root (FS.GG.Audio.slnx) not found" else dir.FullName

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

        test "the package references only Elmish + Host + Core (+ FSharp.Core), never FS.GG.UI/SkiaSharp (FR-004)" {
            // A produced effect is a lambda compiled into the package's own assembly, so its runtime
            // type reliably identifies FS.GG.Audio.Elmish (no dependence on load order).
            let fake = new RecordingBackend()
            let cmd: Elmish.Cmd<int> = AudioCmd.stopMusic (fake :> IAudioBackend)
            let effect = List.head cmd
            let refs =
                effect.GetType().Assembly.GetReferencedAssemblies()
                |> Array.map (fun n -> n.Name)
            Expect.isFalse
                (refs |> Array.exists (fun n -> not (isNull n) && n.StartsWith "FS.GG.UI"))
                "no FS.GG.UI dependency"
            Expect.isFalse (refs |> Array.contains "SkiaSharp") "no SkiaSharp dependency"
            Expect.isTrue (refs |> Array.contains "Elmish") "references Elmish"
            Expect.isTrue (refs |> Array.contains "FS.GG.Audio.Host") "references FS.GG.Audio.Host"
            Expect.isTrue (refs |> Array.contains "FS.GG.Audio.Core") "references FS.GG.Audio.Core"
        }

        test "commands run headless against a recording backend with a no-op dispatch, no device (FR-005)" {
            let fake = new RecordingBackend()
            let cmd = AudioCmd.playMusic (fake :> IAudioBackend) (TrackId "bgm") true
            // A genuine no-op dispatch, and no audio device is opened anywhere.
            for effect in cmd do
                effect ignore
            Expect.equal
                fake.Calls
                [ CoreAudio.playMusic (TrackId "bgm") true ]
                "recorded the requested effect with no device and a no-op dispatch"
        }

        test "the committed .fsi surface baseline matches the source signature, no drift (FR-006)" {
            let root = repoRoot ()
            let source = Path.Combine(root, "src", "FS.GG.Audio.Elmish", "Elmish.fsi")
            let baseline = Path.Combine(root, "docs", "api-surface", "FS.GG.Audio.Elmish", "Elmish.fsi")
            Expect.isTrue (File.Exists baseline) "committed api-surface baseline exists"
            Expect.equal
                (File.ReadAllText baseline)
                (File.ReadAllText source)
                "public surface matches the committed .fsi baseline"
        }
    ]
