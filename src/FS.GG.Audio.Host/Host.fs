namespace FS.GG.Audio.Host

#nowarn "9" // native-interop pointers (OpenAL device/context handles) are guarded and disposed below.

open System
open FS.GG.Audio.Core

type AssetResolver =
    { ResolveSound: SoundId -> byte[] option
      ResolveTrack: TrackId -> byte[] option }

type IAudioBackend =
    inherit IDisposable
    abstract member Play: effect: AudioEffect -> unit

// Optional mixing/spatial control (004-audio-engine). Additive: existing IAudioBackend-only
// backends stay valid; FS.GG.Audio.Engine feature-detects this and degrades when it is absent.
type IMixingBackend =
    inherit IAudioBackend
    abstract member SetBusGain: bus: Bus * gain: float -> unit
    abstract member SetListener: x: float * y: float * z: float -> unit
    abstract member PlayAt: sound: SoundId * gain: float * pan: float -> unit

// Reach Core's Audio module without shadowing the host's own `Audio` module below.
module CoreAudio = FS.GG.Audio.Core.Audio

[<RequireQualifiedAccess>]
module Wav =

    type PcmData =
        { Channels: int
          BitsPerSample: int
          SampleRate: int
          Data: byte[] }

    // Minimal RIFF/WAVE PCM reader: walk chunks, pull fmt (channels/rate/bits) + data. Total —
    // returns None on anything malformed or unrecognized rather than throwing.
    let tryParse (bytes: byte[]) : PcmData option =
        try
            let ascii off len = Text.Encoding.ASCII.GetString(bytes, off, len)
            if bytes.Length < 44 || ascii 0 4 <> "RIFF" || ascii 8 4 <> "WAVE" then
                None
            else
                let mutable pos = 12
                let mutable channels = 0
                let mutable sampleRate = 0
                let mutable bits = 0
                let mutable dataOff = -1
                let mutable dataLen = 0
                while pos + 8 <= bytes.Length do
                    let id = ascii pos 4
                    let sz = BitConverter.ToInt32(bytes, pos + 4)
                    let body = pos + 8
                    if id = "fmt " && body + 16 <= bytes.Length then
                        channels <- int (BitConverter.ToInt16(bytes, body + 2))
                        sampleRate <- BitConverter.ToInt32(bytes, body + 4)
                        bits <- int (BitConverter.ToInt16(bytes, body + 14))
                    elif id = "data" then
                        dataOff <- body
                        dataLen <- sz
                    // chunks are word-aligned: skip the body plus any pad byte.
                    pos <- body + sz + (sz &&& 1)
                if dataOff < 0 || channels = 0 || bits = 0 then
                    None
                else
                    let len = max 0 (min dataLen (bytes.Length - dataOff))
                    Some
                        { Channels = channels
                          BitsPerSample = bits
                          SampleRate = sampleRate
                          Data = Array.sub bytes dataOff len }
        with _ -> None

[<RequireQualifiedAccess>]
module Audio =

    let play (backend: IAudioBackend) (effects: AudioEffect list) : unit =
        for effect in effects do
            backend.Play effect

[<RequireQualifiedAccess>]
module NullBackend =

    [<Sealed>]
    type T() =
        let mutable evidence = CoreAudio.emptyEvidence
        member _.Evidence = evidence
        interface IAudioBackend with
            member _.Play(effect: AudioEffect) =
                // Record-only: identical folding to Core.Audio.interpret, one effect at a time.
                evidence <- CoreAudio.record effect evidence
            member _.Dispose() = ()

    let create () = new T()

// The real OpenAL device backend. All device interaction is isolated here and fully guarded, so a
// missing device/native library never escapes as an exception into game code (FR-004).
module private OpenAl =

    open Silk.NET.OpenAL
    open Microsoft.FSharp.NativeInterop

    let private bufferFormat channels bits =
        match channels, bits with
        | 1, 8 -> Some BufferFormat.Mono8
        | 1, 16 -> Some BufferFormat.Mono16
        | 2, 8 -> Some BufferFormat.Stereo8
        | 2, 16 -> Some BufferFormat.Stereo16
        | _ -> None

    // A real device backend. Construction opens the device/context; failure throws and the caller
    // (create, below) degrades to Null. Per-effect Play is guarded so a runtime device error is a
    // no-op, never a throw.
    type Backend(resolver: AssetResolver) =
        let al = AL.GetApi(false)
        let alc = ALContext.GetApi(false)
        let device = alc.OpenDevice("")
        do if NativePtr.toNativeInt device = 0n then failwith "OpenAL: no output device"
        let context = alc.CreateContext(device, NativePtr.nullPtr)
        do
            if NativePtr.toNativeInt context = 0n then
                alc.CloseDevice device |> ignore
                failwith "OpenAL: could not create context"
        do alc.MakeContextCurrent context |> ignore

        let buffers = Collections.Generic.List<uint>()
        let sources = Collections.Generic.List<uint>()
        let mutable musicSource : uint option = None

        let loadBuffer (bytes: byte[]) : uint option =
            match Wav.tryParse bytes with
            | None -> None
            | Some pcm ->
                match bufferFormat pcm.Channels pcm.BitsPerSample with
                | None -> None
                | Some fmt ->
                    let buf = al.GenBuffer()
                    al.BufferData(buf, fmt, pcm.Data, pcm.SampleRate)
                    buffers.Add buf
                    Some buf

        let startSource (buf: uint) (loop: bool) (gain: float32) : uint =
            let src = al.GenSource()
            al.SetSourceProperty(src, SourceInteger.Buffer, int buf)
            al.SetSourceProperty(src, SourceBoolean.Looping, loop)
            al.SetSourceProperty(src, SourceFloat.Gain, gain)
            al.SourcePlay src
            sources.Add src
            src

        interface IAudioBackend with
            member _.Play(effect: AudioEffect) =
                try
                    match effect with
                    // PlaySfx3D on the raw backend degrades to a non-positional one-shot at the
                    // carried gain (004); the Engine spatializes via IMixingBackend.PlayAt.
                    | PlaySfx(sound, volume)
                    | PlaySfx3D(sound, _, _, _, volume) ->
                        match resolver.ResolveSound sound with
                        | Some bytes ->
                            match loadBuffer bytes with
                            | Some buf -> startSource buf false (float32 volume) |> ignore
                            | None -> ()
                        | None -> ()
                    // Bus mixing / ducking are realized by FS.GG.Audio.Engine, not the raw backend.
                    | SetBusVolume _
                    | Duck _ -> ()
                    | PlayMusic(track, loop) ->
                        match resolver.ResolveTrack track with
                        | Some bytes ->
                            match loadBuffer bytes with
                            | Some buf ->
                                musicSource |> Option.iter al.SourceStop
                                musicSource <- Some(startSource buf loop 1.0f)
                            | None -> ()
                        | None -> ()
                    | StopMusic ->
                        musicSource |> Option.iter al.SourceStop
                        musicSource <- None
                    | SetMasterVolume level ->
                        al.SetListenerProperty(ListenerFloat.Gain, float32 level)
                with _ ->
                    // Safe failure (Principle VIII): a device hiccup degrades to silence, not a crash.
                    ()

            member _.Dispose() =
                try
                    for src in sources do al.SourceStop src
                    for src in sources do al.DeleteSource src
                    for buf in buffers do al.DeleteBuffer buf
                    alc.DestroyContext context
                    alc.CloseDevice device |> ignore
                    al.Dispose()
                    alc.Dispose()
                with _ -> ()

[<RequireQualifiedAccess>]
module OpenAlBackend =

    let create (resolver: AssetResolver) : IAudioBackend =
        try
            new OpenAl.Backend(resolver) :> IAudioBackend
        with ex ->
            // Degrade-to-zero (FR-004): no device / no native library -> Null backend, logged.
            eprintfn "FS.GG.Audio.Host: OpenAL unavailable (%s); using the Null backend." ex.Message
            NullBackend.create () :> IAudioBackend
