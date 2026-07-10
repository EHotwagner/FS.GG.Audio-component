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

        // The bundled OpenAL backend must be an IMixingBackend, because that is the only interface
        // FS.GG.Audio.Engine spatializes through: a backend that implements IAudioBackend alone
        // silently drops every pan. Vacuous on headless CI, where create degrades to Null (and Null
        // must stay a plain IAudioBackend, so the Engine's degrade path is what runs there).
        test "the OpenAL backend implements IMixingBackend when a device is present (#11)" {
            let resolver =
                { ResolveSound = (fun _ -> None)
                  ResolveTrack = (fun _ -> None) }
            let backend = OpenAlBackend.create resolver
            match backend with
            | :? NullBackend.T -> Expect.isFalse (backend :? IMixingBackend) "the Null fallback stays non-mixing"
            | _ -> Expect.isTrue (backend :? IMixingBackend) "a real device backend spatializes"
            backend.Dispose()
        }

        test "Spatial.panToPosition puts a centred voice in front of the listener (#11)" {
            let (x, y, z) = Spatial.panToPosition 0.0
            Expect.floatClose Accuracy.high x 0.0 "centred: no lateral offset"
            Expect.floatClose Accuracy.high y 0.0 "planar: no vertical offset"
            Expect.floatClose Accuracy.high z -1.0 "dead ahead, not inside the listener's head"
        }

        test "Spatial.panToPosition separates hard left from hard right (#11)" {
            let (lx, _, lz) = Spatial.panToPosition -1.0
            let (rx, _, rz) = Spatial.panToPosition 1.0
            Expect.floatClose Accuracy.high lx -1.0 "pan=-1 sits on the listener's left"
            Expect.floatClose Accuracy.high rx 1.0 "pan=+1 sits on the listener's right"
            Expect.floatClose Accuracy.high lz 0.0 "hard pan is fully lateral"
            Expect.floatClose Accuracy.high rz 0.0 "hard pan is fully lateral"
            Expect.isLessThan lx rx "left and right are distinguishable, not collapsed"
        }

        test "Spatial.panToPosition is unit-length for every pan, so gain is never re-attenuated (#11)" {
            // Engine already folded distance attenuation into the voice's gain. The source must land
            // on the unit circle: at OpenAL's default reference distance the distance model is a
            // no-op, so the gain we pass is the gain that plays.
            for i in -20 .. 20 do
                let pan = float i / 10.0 // spans [-2, 2]: past the ends too, to cover the clamp
                let (x, y, z) = Spatial.panToPosition pan
                Expect.floatClose Accuracy.high (sqrt (x * x + y * y + z * z)) 1.0 $"unit length at pan={pan}"
        }

        test "Spatial.panToPosition clamps out-of-range pan and centres nan (#11)" {
            Expect.equal (Spatial.panToPosition -5.0) (Spatial.panToPosition -1.0) "below -1 clamps to hard left"
            Expect.equal (Spatial.panToPosition 5.0) (Spatial.panToPosition 1.0) "above +1 clamps to hard right"
            // Total on bad input (Principle VI): nan is a defined centre, not a nan position that
            // would silently mute the source on the device.
            Expect.equal (Spatial.panToPosition nan) (Spatial.panToPosition 0.0) "nan -> centred"
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
