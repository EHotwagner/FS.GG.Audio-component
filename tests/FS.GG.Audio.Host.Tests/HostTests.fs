module FS.GG.Audio.Host.Tests.HostTests

open Expecto
open FS.GG.Audio.Core
open FS.GG.Audio.Host

// Disambiguate the two `Audio` modules (Core's vocabulary vs the host's imperative drive).
module CoreAudio = FS.GG.Audio.Core.Audio
module HostAudio = FS.GG.Audio.Host.Audio

// Recording fake backend (DEC-002 / FR-008): asserts the effect->Play mapping and order without a
// real device. This is how the host is exercised headless.
type private RecordingBackend() =
    let calls = ResizeArray<AudioEffect>()
    member _.Calls = List.ofSeq calls
    interface IAudioBackend with
        member _.Play(e) = calls.Add e
        member _.Dispose() = ()

// A tiny valid mono/16-bit PCM WAV (2 sample frames) for the WAV-parser test.
let private sampleWav () : byte[] =
    use ms = new System.IO.MemoryStream()
    use w = new System.IO.BinaryWriter(ms)
    let data = [| 0uy; 0uy; 1uy; 0uy |]
    let channels = 1s
    let rate = 44100
    let bits = 16s
    let byteRate = rate * int channels * int bits / 8
    let blockAlign = int16 (int channels * int bits / 8)
    w.Write(System.Text.Encoding.ASCII.GetBytes "RIFF")
    w.Write(36 + data.Length)
    w.Write(System.Text.Encoding.ASCII.GetBytes "WAVE")
    w.Write(System.Text.Encoding.ASCII.GetBytes "fmt ")
    w.Write(16)
    w.Write(1s) // PCM
    w.Write(channels)
    w.Write(rate)
    w.Write(byteRate)
    w.Write(blockAlign)
    w.Write(bits)
    w.Write(System.Text.Encoding.ASCII.GetBytes "data")
    w.Write(data.Length)
    w.Write(data)
    w.Flush()
    ms.ToArray()

[<Tests>]
let tests =
    testList "FS.GG.Audio.Host" [

        test "Audio.play drives the backend once per effect in dispatch order (FR-006)" {
            let fake = new RecordingBackend()
            let effects =
                [ CoreAudio.playSfx (SoundId "a") 0.5
                  CoreAudio.playMusic (TrackId "m") true
                  CoreAudio.stopMusic ]
            HostAudio.play (fake :> IAudioBackend) effects
            Expect.equal fake.Calls effects "each effect drives Play once, oldest-first"
        }

        test "Null backend records evidence identical to Core.Audio.interpret (FR-002)" {
            let effects =
                [ CoreAudio.playSfx (SoundId "a") 9.0 // out of range -> normalized
                  CoreAudio.playMusic (TrackId "m") false
                  CoreAudio.setMasterVolume -1.0 ]
            let nb = NullBackend.create ()
            HostAudio.play (nb :> IAudioBackend) effects
            Expect.equal nb.Evidence (CoreAudio.interpret effects) "Null backend evidence == interpret"
        }

        test "Null backend opens no device and never throws (FR-002)" {
            let nb = NullBackend.create () :> IAudioBackend
            HostAudio.play nb [ CoreAudio.stopMusic; CoreAudio.playSfx (SoundId "x") 0.2 ]
            nb.Dispose()
            Expect.isTrue true "no exception thrown on a machine with no device"
        }

        test "OpenAlBackend.create degrades to a usable backend with no device (FR-004)" {
            // Headless/CI has no OpenAL device: create MUST NOT throw and MUST return a usable
            // IAudioBackend (the Null fallback); playing through it is a safe no-op.
            let resolver =
                { ResolveSound = (fun _ -> None)
                  ResolveTrack = (fun _ -> None) }
            let backend = OpenAlBackend.create resolver
            HostAudio.play backend [ CoreAudio.playSfx (SoundId "s") 0.5; CoreAudio.stopMusic ]
            backend.Dispose()
            Expect.isTrue true "create degraded without throwing and play was safe"
        }

        test "Wav.tryParse reads a minimal PCM WAV (FR-005)" {
            match Wav.tryParse (sampleWav ()) with
            | Some pcm ->
                Expect.equal pcm.Channels 1 "mono"
                Expect.equal pcm.BitsPerSample 16 "16-bit"
                Expect.equal pcm.SampleRate 44100 "44.1 kHz"
                Expect.equal pcm.Data.Length 4 "2 frames * 16-bit mono = 4 bytes"
            | None -> failtest "expected a parsed WAV"
        }

        test "Wav.tryParse returns None on malformed input, never throws (FR-005)" {
            Expect.isNone (Wav.tryParse [| 1uy; 2uy; 3uy |]) "too short -> None"
            Expect.isNone
                (Wav.tryParse (System.Text.Encoding.ASCII.GetBytes "NOTAWAVEFILE...."))
                "bad header -> None"
        }
    ]
