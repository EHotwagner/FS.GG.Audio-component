# SDD + Governance Workflow — Feedback Report

- **Date:** 2026-07-07 13:19 CEST (2026-07-07T11:19:05Z)
- **Author:** Claude (Opus 4.8), pair-driving with @EHotwagner
- **Scope:** Hands-on assessment of the FS.GG spec-driven-development (SDD) lifecycle
  and its Governance boundary, based on driving work item **`003-audio-elmish`**
  (`FS.GG.Audio.Elmish`) from `charter` through `ship`.
- **Environment:** `fsgg-sdd` generator **0.8.0**, report schema **1.1.0**,
  artifact `schemaVersion: 1`, .NET SDK **10.0.301**, repo `FS.GG.Audio-component`.
- **Evidence base:** one complete lifecycle run (10/10 stages), plus the
  implementation + test hardening of the package under review.

---

## 1. Executive summary

The SDD lifecycle delivered its core promise: a chartered idea reached a
**`shipReady`** verdict through a sequence of small, individually-checkable stages,
each pointing at the next, with a traceable line from functional requirement →
acceptance criterion → task → evidence obligation. The **evidence satisfaction rule**
(`pass ∧ ¬synthetic`) is the strongest part of the design — it makes "looks done"
and "is proven done" different states that the tooling can tell apart.

The friction is concentrated in three places: (a) a handful of **tooling defects**
that inject spurious diffs or stale prose into authored artifacts, (b) a **scaffold
posture that defaults to `pass`**, which quietly pushes against the "classify
honestly" doctrine the skills preach, and (c) **Governance being un-exercisable**
here (`notEvaluated` throughout), so its side of the contract could only be assessed
on paper.

Overall: the *skeleton* is sound and worth keeping. The *ergonomics* need a pass to
stop the tool from occasionally undermining the honesty guarantees it exists to
protect.

---

## 2. What worked

### 2.1 Stage sequencing with explicit next-actions
Every stage report ends with `nextAction` and a `lifecycle: N/10 <stage>` line. At no
point did I have to guess the next command — `clarify → checklist → plan → tasks →
analyze → evidence → verify → ship` was self-navigating. For an agent driving the
process this is the single most valuable affordance.

### 2.2 The evidence satisfaction rule is honest-by-construction
`result: pass ∧ synthetic: false` being the *only* satisfying disposition means a
synthetic stand-in or a deferral cannot masquerade as proof. Deferrals are
first-class (require `rationale`/`owner`/`scope`/`laterLifecycleVisibility`), so
"not done yet, and here's why" is a legal, non-failing outcome. This is a genuinely
good governance primitive.

### 2.3 End-to-end traceability
`FR-004 → AC-004 → T004/T010 → EV004/EV010` held together across the whole run. The
`verify` stage cross-checked 32 obligations and 6 skills without manual bookkeeping.
When I *added* three tests, the corresponding evidence obligations were already there
to point them at.

### 2.4 Deferral flow across work items
`DEC-004` in `002-audio-host` ("defer the Elmish surface") surfaced as the *charter
input* for `003-audio-elmish` and was closed here. The lifecycle carried a decision
across a package boundary without it being lost — exactly what the deferral machinery
is for.

### 2.5 Multiple output projections
`--json` (deterministic, parseable), `--text` (portable summary), `--rich`
(Spectre.Console). Having a stable machine projection *and* a human one from the same
command is the right design.

### 2.6 Governance is optional, not load-bearing
The entire lifecycle ran with **no Governance runtime installed** — the
`.fsgg/policy.yml` / `capabilities.yml` / `tooling.yml` reported as
`notEvaluated` compatibility facts and nothing blocked. "SDD reports readiness;
Governance enforces" is a clean separation, and being able to run `init → ship`
without Governance is a real adoption win.

### 2.7 Determinism + snapshot digests
`evidence.yml` records `sha256` digests of its upstream sources; re-running `plan`
correctly reported `noChange`. Staleness is detectable rather than silent.

---

## 3. What didn't work / friction

Ordered by how much each one erodes trust in the artifacts.

### 3.1 `evidence` re-serialization rewrites `null` → `"null"`  — **defect, high**
Re-running `fsgg-sdd evidence` rewrote every nullable field
(`rationale`/`owner`/`scope`/`laterLifecycleVisibility`) from a proper YAML `null`
to the **quoted string `"null"`**, producing ~128 lines of spurious diff on a file
whose semantic content hadn't changed. Beyond the noise, `"null"` (string) is not
`null` (absent) — it's a latent data-quality bug. I had to `git checkout` the file
and re-apply only the three intentional edits by hand to keep the commit reviewable.

### 3.2 Auto-generated evidence notes assert facts that go stale — **defect, medium**
The scaffold wrote specific claims like `"… 3/3 headless"` into `notes`. As soon as I
added three tests, those notes were **wrong** (the suite was 6/6) — but nothing flags
it, because prose isn't validated. Auto-generated text that asserts countable facts
will drift into dishonesty without the author noticing. (I caught and fixed three of
these; a less careful pass would have shipped false evidence notes.)

### 3.3 Evidence scaffold defaults obligations to `result: pass` — **posture, medium**
A fresh `evidence` scaffold marked all 16 obligations `pass` / `synthetic: false`
*before any human judged them*. The skill text explicitly warns "don't blanket-`pass`
… classify honestly, one obligation at a time" — but the tool's own default sets up
exactly the blanket-pass state it warns against. The safe default would be a
non-satisfying placeholder (`missing`/`pending`) that forces an affirmative
classification.

### 3.4 `plan` and `tasks` are mechanical restatements, not design — **design gap, medium**
`plan.md`'s "decisions" were `PD-001 … PD-006: "Plan requirement FR-00X through the
plan command contract."` — one per FR, carrying no actual technical design (module
shape, the `Elmish.Cmd<'msg>` approach, the `.fsi` surface). The real design lived in
the *charter* and had to be re-derived from code. For a stage whose doctrine is
"signatures before implementation," the generated plan doesn't capture signatures.
It reads as ceremony rather than the load-bearing design record it's meant to be.

### 3.5 Stage-state cache reports stages "done" before they run — **defect, medium**
Before I ran `verify`/`ship`, the `stages:` footer already showed
`verify=done ship=done` (projected from a stale work-model). This is actively
misleading about actual progress — a reader can't trust the progress line to reflect
what has really been executed vs. what the model *expects* to happen.

### 3.6 `--rich` is unrecoverable from a capturing harness — **ergonomics, low/medium**
`--rich` detects a non-TTY stdout and degrades to plain text. Under an agent harness
(pipe-captured stdout) the Spectre rendering is only reachable via a PTY shim
(`script`/`unbuffer`), and even then only the *raw escape codes* survive capture —
never rendered colour, because capture and render are separate stages. There's no
`FORCE_COLOR`/force-ANSI escape hatch. Net: the intended human report is
second-class for any automated or logged context.

### 3.7 Readiness verdicts are gitignored — **audit gap, low/medium**
`readiness/**` (incl. `verify.json`, `ship.json`, `governance-handoff.json`) is
`.gitignore`d, so the **`shipReady` verdict at a given commit is not in history**.
"Was `003` ship-ready when it merged?" is answerable only by re-running the tool, not
by reading git. For a merge-boundary artifact this is surprising — at least a compact,
committed ship-summary would preserve the audit trail.

### 3.8 API-surface baseline drift is not enforced — **gap, medium**
The convention is a committed copy under `docs/api-surface/<pkg>/<name>.fsi`, but
**nothing checks it** — no build target, no `fsgg-sdd` subcommand. FR-006 ("committed
`.fsi` baseline, no drift") only became real because I hand-wrote a test that diffs
the two files. A framework that declares a baseline convention should ship the drift
check, not leave it per-package DIY.

### 3.9 Spec projection confusion — **ergonomics, low**
On first read `spec.md` showed a single boilerplate `FR-001` ("As a maintainer, I can
specify … after chartering"); moments later it contained the real six FRs. Whatever
regenerated/thickened it between reads, the transient thin state is confusing and
looked for a moment like the spec had lost content.

### 3.10 Governance could not be exercised — **coverage gap**
Every Governance signal was `notEvaluated` (no runtime installed). The *contract
shape* (ship → `governance-handoff.json` → protected-boundary handoff) is clear and
sensible, but I could form no opinion on the gate's actual behavior, evidence-freshness
policy, routing, or audit. See §4.

---

## 4. Governance-specific observations

- **The boundary is well-drawn.** SDD emitting a `governance-handoff.json` and
  *pointing at* an external, optional gate — rather than embedding enforcement — keeps
  the two systems independently adoptable. Running the full lifecycle with the gov
  config files absent/`notEvaluated` and still succeeding is the correct default.
- **But "optional" also meant "untestable here."** With no runtime, I cannot say
  whether the handoff carries *enough* for a real gate (evidence freshness windows,
  profile/routing, audit identity). A **`--dry-run` / simulated-gate mode** that
  evaluates `ship.json` against a sample policy and prints the verdict — without
  requiring the full Governance install — would let teams (and reviewers) preview gate
  behavior and would make this side of the workflow assessable.
- **The compatibility table is good.** Reporting `.fsgg/*.yml` as
  `optionalGovernance* : notEvaluated` (even when absent or malformed) is exactly the
  right "won't block you, will tell you" posture.

---

## 5. Possible improvements (prioritized)

**P0 — correctness of authored artifacts**
1. **Fix `null` → `"null"` serialization** in `evidence` (and audit other stages for
   the same round-trip bug). Round-tripping an unchanged file must be a no-op diff.
2. **Stop auto-writing countable claims** (`"3/3 headless"`) into evidence `notes`, or
   compute them at report time from a real source. Don't persist prose that silently
   goes stale.

**P1 — honesty posture**
3. **Scaffold obligations to a non-satisfying default** (`missing`/`pending`), so
   `pass` is always an affirmative human act. Aligns the tool's default with the
   skill's "classify honestly" doctrine.
4. **Fix the stage-state projection** so `stages:` reflects executed-vs-expected
   truthfully (e.g. distinguish `done` from `projected`).

**P1 — enforce the conventions the framework declares**
5. **Ship an API-surface drift check** (`fsgg-sdd surface --check`, or an MSBuild
   target) so `.fsi` baselines are enforced, not hand-tested per package.
6. **Commit a compact ship verdict** (or un-ignore a small subset of `readiness/`) so
   the merge-readiness decision is in git history, not only re-derivable.

**P2 — design capture & ergonomics**
7. **Make `plan`/`tasks` capture real design** (signatures, module layout, contract
   deltas) instead of one boilerplate decision per FR — or clearly mark the generated
   lines as placeholders to be authored.
8. **Give `--rich` a `--force-color`/`FORCE_COLOR` escape hatch**, and/or add a
   Markdown "report card" projection that's both human-readable and capture-safe.
9. **Smooth the spec projection** so a partially-generated `spec.md` is never
   observable in a thin/boilerplate intermediate state.

**P2 — Governance**
10. **Add a simulated/dry-run gate** over `ship.json` so the Governance handoff can be
    previewed and assessed without the full runtime.

---

## 6. Bottom line

The SDD lifecycle is a **keeper**: the staged flow, the FR→evidence traceability, and
the `pass ∧ ¬synthetic` satisfaction rule are the right bones, and the Governance
boundary is cleanly separated and optional. The work to do is mostly **defensive
polish** — stop the tooling from injecting spurious diffs (§3.1), stale prose (§3.2),
and optimistic defaults (§3.3, §3.5) that quietly work against the very honesty the
model is built to guarantee. Land those P0/P1 fixes and this becomes a workflow I'd
trust to gate a merge without a human double-checking the tool's own artifacts.

---

*Generated from a single end-to-end run; findings are grounded in `003-audio-elmish`
artifacts (`work/003-audio-elmish/`, `readiness/003-audio-elmish/`) and the
`FS.GG.Audio.Elmish` implementation committed in `fd4e1a0`/`324bf81`.*
