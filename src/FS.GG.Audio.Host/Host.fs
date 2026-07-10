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
module Spatial =

    // FS.GG.Audio.Engine owns the spatial model: by the time a voice reaches IMixingBackend.PlayAt
    // its distance attenuation is already folded into `gain`, and its direction survives only as a
    // stereo pan in [-1, 1]. A device backend therefore has to place the source where the device's
    // own distance model cannot attenuate it a second time: on the unit circle around the listener,
    // in the listener's frame. Pan drives the lateral axis; the remainder goes to -z (straight
    // ahead, the OpenAL listener's default facing), so a centred voice sits in front of the
    // listener rather than inside their head.
    let panToPosition (pan: float) : float * float * float =
        let p =
            if Double.IsNaN pan then 0.0
            elif pan < -1.0 then -1.0
            elif pan > 1.0 then 1.0
            else pan
        // p^2 <= 1 by the clamp above, so the sqrt is total and the result is always unit-length.
        // At a hard pan the depth is `-sqrt 0.0` = negative zero; fold it to +0.0 so a printed or
        // compared position reads as the plain 0.0 it is.
        let z = -sqrt (1.0 - p * p)
        (p, 0.0, (if z = 0.0 then 0.0 else z))

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

        // Realized bus gains, as pushed by IMixingBackend.SetBusGain. Engine folds Sfx*Master into
        // each one-shot's gain before PlayAt, but it forwards PlayMusic to `Play` un-scaled — so the
        // music voice is the one thing the backend must scale itself, and it is the only reader of
        // this table. Master must NOT reach the listener gain here: that would apply it a second
        // time to voices whose gain already carries it.
        let busGains = Collections.Generic.Dictionary<Bus, float>()
        do for b in [ Master; Music; Sfx; Ui; Ambient ] do busGains.[b] <- 1.0

        let musicGain () = float32 (CoreAudio.clampVolume (busGains.[Music] * busGains.[Master]))

        // Last gain written to the music source. The Engine re-pushes every bus on every frame, so
        // without this a steady mix still costs two native writes per frame. A frame that does move
        // the gain writes twice (Master is pushed before Music, so the first write carries the
        // previous frame's Music), which is inaudible: both land before the Engine returns and the
        // device renders. A negative sentinel cannot equal a clamped gain, so the first write always
        // lands.
        let mutable appliedMusicGain = -1.0f

        // Push the current Music*Master gain at the playing music source, if it has moved. Nothing
        // else writes that source's gain, so the memo cannot go stale.
        let applyMusicGain () =
            let gain = musicGain ()
            match musicSource with
            | Some src when gain <> appliedMusicGain ->
                al.SetSourceProperty(src, SourceFloat.Gain, gain)
                appliedMusicGain <- gain
            | _ -> ()

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

        // Every source is listener-relative with the distance model switched off, so its position is
        // read as a pure direction and the gain we pass is the gain that plays. `position` is None
        // for a non-positional voice, which then sits exactly at the listener (dead centre, and
        // unattenuated no matter where the listener has moved to).
        // OpenAL spatializes mono buffers only — a stereo asset plays centred whatever we set here.
        let startSource (buf: uint) (loop: bool) (gain: float32) (position: (float * float * float) option) : uint =
            let src = al.GenSource()
            al.SetSourceProperty(src, SourceInteger.Buffer, int buf)
            al.SetSourceProperty(src, SourceBoolean.Looping, loop)
            al.SetSourceProperty(src, SourceFloat.Gain, gain)
            al.SetSourceProperty(src, SourceBoolean.SourceRelative, true)
            al.SetSourceProperty(src, SourceFloat.RolloffFactor, 0.0f)
            let (px, py, pz) = defaultArg position (0.0, 0.0, 0.0)
            al.SetSourceProperty(src, SourceVector3.Position, float32 px, float32 py, float32 pz)
            al.SourcePlay src
            sources.Add src
            src

        // Play a resolved one-shot; `position` None => non-positional.
        let playOneShot (sound: SoundId) (gain: float32) (position: (float * float * float) option) =
            match resolver.ResolveSound sound with
            | Some bytes ->
                match loadBuffer bytes with
                | Some buf -> startSource buf false gain position |> ignore
                | None -> ()
            | None -> ()

        interface IAudioBackend with
            member _.Play(effect: AudioEffect) =
                try
                    match effect with
                    // A PlaySfx3D that arrives here was dispatched straight at the backend rather
                    // than through FS.GG.Audio.Engine, so nothing has spatialized it: the position
                    // is in the product's world frame and the backend has no listener to relate it
                    // to. Degrade to a non-positional one-shot at the carried gain (004). Driven by
                    // the Engine, 3D voices arrive pre-spatialized through PlayAt below.
                    | PlaySfx(sound, volume)
                    | PlaySfx3D(sound, _, _, _, volume) -> playOneShot sound (float32 volume) None
                    // Bus mixing / ducking are envelopes over time: the raw backend has no clock, so
                    // FS.GG.Audio.Engine advances them and pushes the realized gains through
                    // IMixingBackend.SetBusGain below.
                    | SetBusVolume _
                    | Duck _ -> ()
                    | PlayMusic(track, loop) ->
                        match resolver.ResolveTrack track with
                        | Some bytes ->
                            match loadBuffer bytes with
                            | Some buf ->
                                musicSource |> Option.iter al.SourceStop
                                let gain = musicGain ()
                                musicSource <- Some(startSource buf loop gain None)
                                appliedMusicGain <- gain
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

        interface IMixingBackend with
            member _.SetBusGain(bus: Bus, gain: float) =
                try
                    busGains.[bus] <- gain
                    // Only the music voice is left for the backend to scale, and it is long-lived:
                    // a fade or a duck has to reach the source that is already playing. The one-shot
                    // buses (Sfx, Ui, Ambient) are folded into each voice's gain before PlayAt.
                    if bus = Music || bus = Master then applyMusicGain ()
                with _ -> ()

            member _.SetListener(x: float, y: float, z: float) =
                try
                    // Mirrored into the device for truthfulness, though today's sources are all
                    // listener-relative and so read positions as directions, ignoring it. The
                    // Engine's listener remains the single source of truth for attenuation and pan.
                    al.SetListenerProperty(ListenerVector3.Position, float32 x, float32 y, float32 z)
                with _ -> ()

            member _.PlayAt(sound: SoundId, gain: float, pan: float) =
                try playOneShot sound (float32 gain) (Some(Spatial.panToPosition pan)) with _ -> ()

[<RequireQualifiedAccess>]
module OpenAlBackend =

    let create (resolver: AssetResolver) : IAudioBackend =
        try
            new OpenAl.Backend(resolver) :> IAudioBackend
        with ex ->
            // Degrade-to-zero (FR-004): no device / no native library -> Null backend, logged.
            eprintfn "FS.GG.Audio.Host: OpenAL unavailable (%s); using the Null backend." ex.Message
            NullBackend.create () :> IAudioBackend
