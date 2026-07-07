---
schemaVersion: 1
workId: 003-audio-elmish
title: FS.GG.Audio.Elmish — Elmish Cmd authoring surface over FS.GG.Audio.Host
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# FS.GG.Audio.Elmish — Elmish Cmd authoring surface over FS.GG.Audio.Host Charter

## Identity
- Work id: `003-audio-elmish`
- Lifecycle stage: charter
- Status: chartered

`FS.GG.Audio.Elmish` is the thin Elmish authoring bridge deferred from `002-audio-host`
(DEC-004). It lets a product's `update` return `Audio.Cmd.playSfx …` etc. — `Elmish.Cmd`
values that, when the Elmish runtime executes them, play through an `IAudioBackend`. It
depends on the standard MIT `Elmish` library plus `FS.GG.Audio.Host`/`.Core`, and
crucially **not** on `FS.GG.UI.*`, so it introduces the Elmish convenience without
contaminating the render-independent audio component.

## Principles
- **No FS.GG.UI dependency.** Depends on `Elmish` (MIT), `FS.GG.Audio.Host`, and
  `FS.GG.Audio.Core` only — the reason this is a separate package rather than part of Host.
- **Pure edge, effectful host.** A `Cmd` is a description; the effect (playing through the
  backend) runs only when the Elmish runtime executes it. The commands dispatch no message
  (fire-and-forget audio), so `update` stays a pure `Model -> Model * Cmd`.
- **Same vocabulary as Core.** The `Cmd` constructors mirror `Core.Audio` one-for-one; no
  new effect semantics are invented here.
- **Deterministic and headless.** A produced `Cmd` is a list of effects runnable with a
  no-op dispatch against a recording/Null backend — testable with no device.
- **Public surface declared** (constitution III): the `Audio.Cmd` surface is a `.fsi` with a
  committed baseline.

## Scope Boundaries
- **In:** `Audio.Cmd.ofEffects` + `playSfx`/`playMusic`/`stopMusic`/`setMasterVolume`, each
  producing an `Elmish.Cmd<'msg>` that plays a batch/effect through a supplied
  `IAudioBackend`; headless tests that execute the `Cmd` against a recording backend.
- **Out:** an `Audio.Sub` subscription surface for playback-completion events (needs backend
  callbacks not in the Host's current seam) — a follow-up when the Host grows events; any
  `FS.GG.UI` integration; the engine niceties.

## Policy Pointers
- SDD policy from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Honors constitution I, III, V (Cmd is a pure description; effect at runtime), VI.
- Tier 1: new public package surface (`FS.GG.Audio.Elmish`).

## Lifecycle Notes
- Consumes `FS.GG.Audio.Host` (`002-audio-host`, shipReady) + `Elmish` 5.x (MIT).
- Realizes the deferral DEC-004 from `002-audio-host`.
- Next lifecycle action: `fsgg-sdd specify --work 003-audio-elmish`.
