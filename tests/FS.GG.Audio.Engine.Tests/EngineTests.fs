module FS.GG.Audio.Engine.Tests.EngineTests

open System.IO
open Expecto
open FS.GG.Audio.Core
open FS.GG.Audio.Host
open FS.GG.Audio.Engine

module CoreAudio = FS.GG.Audio.Core.Audio

// A recording backend that implements the optional mixing seam (so the engine spatializes and
// drives continuous bus gains). Captures every realized call for headless assertions.
type private RecordingMixingBackend() =
    let plays = ResizeArray<AudioEffect>()
    let playAts = ResizeArray<SoundId * float * float>()
    let busGains = ResizeArray<Bus * float>()
    let listeners = ResizeArray<float * float * float>()
    member _.Plays = List.ofSeq plays
    member _.PlayAts = List.ofSeq playAts
    member _.BusGains = List.ofSeq busGains
    member _.Listeners = List.ofSeq listeners
    interface IMixingBackend with
        member _.SetBusGain(bus, gain) = busGains.Add(bus, gain)
        member _.SetListener(x, y, z) = listeners.Add(x, y, z)
        member _.PlayAt(sound, gain, pan) = playAts.Add(sound, gain, pan)
    interface IAudioBackend with
        member _.Play(e) = plays.Add e
        member _.Dispose() = ()

// A backend that throws on every call — the engine must swallow it (FR-008).
type private ThrowingBackend() =
    interface IAudioBackend with
        member _.Play(_) = failwith "device on fire"
        member _.Dispose() = ()

let private mixing () = new RecordingMixingBackend()
let private acc = Accuracy.high

// Walk up from the test binary to the repo root (the directory holding the solution file).
let private repoRoot () =
    let mutable dir = DirectoryInfo(System.AppContext.BaseDirectory)
    while dir <> null && not (File.Exists(Path.Combine(dir.FullName, "FS.GG.Audio.slnx"))) do
        dir <- dir.Parent
    if isNull dir then failwith "repo root (FS.GG.Audio.slnx) not found" else dir.FullName

[<Tests>]
let tests =
    testList "FS.GG.Audio.Engine" [

        test "buses: effective gain = request x bus x master, each clamped (FR-001)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.step engine 0.0
                [ CoreAudio.setBusVolume Sfx 0.5
                  CoreAudio.setMasterVolume 0.5
                  CoreAudio.playSfx (SoundId "a") 0.8 ]
            Expect.floatClose acc (engine.BusGain Sfx) 0.5 "Sfx bus gain set"
            Expect.floatClose acc (engine.BusGain Master) 0.5 "Master bus gain set"
            let v = List.exactlyOne engine.LastVoices
            Expect.floatClose acc v.EffectiveGain (0.8 * 0.5 * 0.5) "effective = request x bus x master"
        }

        test "routing: sfx on Sfx bus, music on the single Music voice, StopMusic clears it (FR-002)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.step engine 0.0 [ CoreAudio.playSfx (SoundId "s") 1.0 ]
            Expect.equal (List.exactlyOne engine.LastVoices).Bus Sfx "sfx routes to the Sfx bus"
            Engine.step engine 0.0 [ CoreAudio.playMusic (TrackId "m") true ]
            Expect.floatClose acc engine.MusicGain 1.0 "music voice present at unity"
            Engine.step engine 0.0 [ CoreAudio.stopMusic ]
            Expect.floatClose acc engine.MusicGain 0.0 "StopMusic clears the music voice"
            Expect.contains backend.Plays (PlayMusic(TrackId "m", true)) "music start realized through the backend"
        }

        test "fadeBus reaches target exactly when deltas sum to the duration (FR-003)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.fadeBus engine Music 0.0 1.0            // fade Music 1.0 -> 0.0 over 1s (linear)
            Engine.step engine 0.5 []
            Expect.floatClose acc (engine.BusGain Music) 0.5 "half-way through a linear fade"
            Engine.step engine 0.5 []
            Expect.floatClose acc (engine.BusGain Music) 0.0 "target reached exactly at the duration"
        }

        test "crossFade is equal-power: constant summed power across the window (FR-003)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.crossFade engine Music Sfx 1.0
            Engine.step engine 0.5 []
            let m, s = engine.BusGain Music, engine.BusGain Sfx
            Expect.floatClose acc (m * m + s * s) 1.0 "equal-power: gains^2 sum to 1 mid-fade"
            Engine.step engine 0.5 []
            Expect.floatClose acc (engine.BusGain Music) 0.0 "from-bus ends silent"
            Expect.floatClose acc (engine.BusGain Sfx) 1.0 "to-bus ends at unity"
        }

        test "Duck attenuates by amount over the attack then auto-restores (FR-004)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.step engine 0.0 [ CoreAudio.duck Music 0.5 1000.0 ]   // 0.5 duck over 1000ms
            Engine.step engine 0.5 []
            Expect.floatClose acc (engine.BusGain Music) 0.5 "maximally ducked at the midpoint (1 - amount)"
            Engine.step engine 0.5 []
            Expect.floatClose acc (engine.BusGain Music) 1.0 "restored after the attack window"
        }

        test "3D: inverse-distance attenuation and x-pan on a mixing backend (FR-005)" {
            let backend = mixing ()
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.setListener engine 0.0 0.0 0.0
            Engine.step engine 0.0 [ CoreAudio.playSfx3D (SoundId "s") 3.0 0.0 0.0 1.0 ]
            let v = List.exactlyOne engine.LastVoices
            Expect.isTrue v.Positional "3D voice is positional on a mixing backend"
            Expect.floatClose acc v.EffectiveGain (1.0 / 3.0) "att = ref/(ref+rolloff*(d-ref)) = 1/3 at d=3"
            Expect.floatClose acc v.Pan 1.0 "pan clamps to +1 for a hard-right emitter"
        }

        test "3D degrades to a non-positional voice without a mixing backend (FR-005, FR-008)" {
            let backend = NullBackend.create ()          // IAudioBackend only, no mixing seam
            let engine = Engine.create (backend :> IAudioBackend)
            Engine.setListener engine 0.0 0.0 0.0
            Engine.step engine 0.0 [ CoreAudio.playSfx3D (SoundId "s") 9.0 0.0 0.0 1.0 ]
            let v = List.exactlyOne engine.LastVoices
            Expect.isFalse v.Positional "no mixing seam -> non-positional"
            Expect.floatClose acc v.EffectiveGain 1.0 "degraded voice plays at the bus-scaled gain (att = 1)"
        }

        test "Core threads the additive variants and clamps them (FR-006)" {
            let ev =
                CoreAudio.interpret
                    [ CoreAudio.playSfx3D (SoundId "s") 1.0 2.0 0.0 9.0   // volume 9.0 -> 1.0
                      CoreAudio.setBusVolume Sfx 2.0                       // level 2.0 -> 1.0
                      CoreAudio.duck Music 2.0 -5.0 ]                      // amount 2.0 -> 1.0, ms -5 -> 0
            Expect.equal
                ev.Requested
                [ PlaySfx3D(SoundId "s", 1.0, 2.0, 0.0, 1.0)
                  SetBusVolume(Sfx, 1.0)
                  Duck(Music, 1.0, 0.0) ]
                "additive cases recorded in order with clamped magnitudes"
            // The original four cases are unchanged (non-breaking).
            Expect.equal
                (CoreAudio.interpret [ CoreAudio.stopMusic ]).Requested
                [ StopMusic ]
                "original cases still record identically"
        }

        test "deterministic: identical inputs produce identical realized calls (FR-007)" {
            let run () =
                let backend = mixing ()
                let engine = Engine.create (backend :> IAudioBackend)
                Engine.setListener engine 1.0 0.0 0.0
                Engine.step engine 0.5
                    [ CoreAudio.setBusVolume Sfx 0.7
                      CoreAudio.playSfx3D (SoundId "s") 4.0 0.0 0.0 0.9
                      CoreAudio.playMusic (TrackId "m") true ]
                Engine.step engine 0.5 [ CoreAudio.duck Music 0.4 500.0 ]
                backend.PlayAts, backend.BusGains, backend.Plays, backend.Listeners
            Expect.equal (run ()) (run ()) "two independent runs realize identical calls"
        }

        test "a throwing backend is swallowed — step never throws (FR-008)" {
            let engine = Engine.create (new ThrowingBackend() :> IAudioBackend)
            Engine.step engine 0.016 [ CoreAudio.playSfx (SoundId "s") 1.0; CoreAudio.stopMusic ]
            Expect.isTrue true "step completed without an exception escaping into game code"
        }

        test "committed .fsi baselines match the sources, no drift (FR-009)" {
            let root = repoRoot ()
            let check pkg name =
                let src = Path.Combine(root, "src", pkg, name)
                let baseline = Path.Combine(root, "docs", "api-surface", pkg, name)
                Expect.isTrue (File.Exists baseline) (sprintf "%s baseline committed" pkg)
                Expect.equal (File.ReadAllText baseline) (File.ReadAllText src) (sprintf "%s surface matches baseline" pkg)
            check "FS.GG.Audio.Engine" "Engine.fsi"
            check "FS.GG.Audio.Core" "Audio.fsi"        // additive Core surface bump
        }
    ]
