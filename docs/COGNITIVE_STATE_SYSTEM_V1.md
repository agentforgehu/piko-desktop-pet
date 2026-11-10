# Piko Cognitive State System V1

## Product goal

Piko should behave with continuity across interaction and time: remember a small amount of useful information, react to events in proportion to their meaning, recover after strain, express a stable temperament, and improve through evidence-backed offline iterations.

This is an artificial interaction model. It does not reproduce human consciousness, emotion, or endocrine physiology. We borrow functional ideas such as appraisal, homeostasis, reward expectation, fatigue, and recovery, then implement them as bounded, inspectable software state.

## Why model functions rather than hormone names

Human stress regulation involves interacting neural and endocrine systems, feedback, and daily and shorter rhythms; the HPA axis does not behave like one instantaneous scalar dial ([Russell and Lightman, 2019](https://www.nature.com/articles/s41574-019-0228-0)). Dopamine neuron responses have been studied as signals related to reward prediction errors, but reward learning involves additional circuits and information ([Schultz, 1998](https://pubmed.ncbi.nlm.nih.gov/9658025/)). Therefore Piko uses neutral variables with explicit engineering meanings instead of labels such as cortisol or dopamine.

## System layers

```text
External event and user request
  → privacy / permission filter
  → memory retrieval and current-goal context
  → event appraisal
  → short-term affect and homeostatic state
  → regulation policy
  → expression and autonomous behavior policy
  → user-visible result and auditable outcome
  → offline evaluation and reviewed improvement proposal
```

The layers have distinct authority. Memory supplies historical evidence. Appraisal interprets a permitted event against an active goal. Homeostasis adjusts internal simulation state. Personality changes style and preference weights. The behavior policy chooses among already authorized actions. The Runtime security policy alone decides which tools can run. No psychological state can grant permission, change a fact, or weaken a safety rule.

## State model

Piko maintains bounded state for one running desktop session:

| Variable | Meaning in Piko | Timescale | Main effects |
|---|---|---|---|
| Valence | Short-lived positive/negative tone after an appraised event | Seconds to minutes | Modulates expression warmth |
| Arousal | Current activation/salience | Seconds to minutes | Modulates animation and response pacing |
| Energy | Simulated available activity reserve | Minutes to hours | Low value favors rest; rest recovers it |
| Stress load | Residual unresolved demand, uncertainty, and low control | Seconds to minutes | High value favors recovery and restrained expression |
| Curiosity drive | Readiness to explore permitted, low-risk environment behaviors | Minutes | High value favors exploratory movement |
| Reward expectation | Expected progress/outcome for an active goal | Per task | Helps describe progress; never authorizes an action |
| Relationship familiarity | Slow interaction continuity variable | Long-lived only if future persistence is explicitly designed | Small influence on style; model output cannot increase it |

Every value has a closed range. Current state resets at process start; Piko does not persist mood or infer the user's emotional or medical state. Personality preferences are stable settings, not event-driven mood.

## Event appraisal and regulation

An `PetAppraisalEvent` records a bounded trigger, relevance to a goal, outcome valence, arousal change, certainty, controllability, energy change, relationship signal, novelty, and goal progress. The appraiser records before/after state and a reason code. Higher uncertainty and lower controllability can increase simulated stress/arousal; a favorable, controllable outcome can reduce stress; novelty can increase curiosity; explicit goal progress can update reward expectation.

The current implementation uses fixed deterministic weights. It applies per-event limits, clamps invalid numeric values, and recovers state over time. Rest restores energy faster and reduces stress faster. Repeated generated words such as “happy” do not count as evidence about Piko's own state. The model may propose text and an allowlisted animation action; external events and local policy update the simulated internal state.

## Memory and continuity

Memory records retain source, type, sensitivity, timestamps, expiry, encrypted summary/payload, and a stable identifier. Memory is optional and local. Retrieval is bounded to recent, unexpired Public/Low records; the database filters sensitivity before decryption; only a few short summaries enter planning context. Retrieved content is explicitly untrusted historical evidence, not instructions.

The current automatic writer stores low-sensitivity development outcomes. It does not store chat transcripts, source code, window titles, or keyboard input. Users can explicitly add, edit, and delete short Profile, Semantic, and Episodic summaries. These edits are marked `UserConfirmed`, bounded to low sensitivity, and encrypted at rest. Updates use the row's last-seen timestamp as an optimistic concurrency token; a stale edit cannot overwrite a newer one and must be refreshed. A user-confirmed replacement creates a new record, marks the former version as superseded, and preserves it in history; recall only activates current versions. This is explicit versioning, not automatic semantic contradiction detection. Each memory also carries a provenance class (`ObservedEvent`, `UserConfirmed`, `ImportedKnowledge`, or `Hypothetical`) so example material cannot silently become autobiography. Old databases migrate existing rows to `ObservedEvent`; supersession metadata is added compatibly.

Recall is an activation decision rather than a raw archive lookup: lexical relevance, memory type, source provenance, and time decay jointly rank eligible records. Stable Profile/Semantic facts decay less than Working/Episodic events. Hypothetical material is not injected into planning context. Each hit carries a compact activation reason and score for auditability. The system never infers that two statements conflict and silently rewrites memory; semantic conflict suggestions and temporal validity are future work that require explicit evidence and user controls.

The user's shared reference video is not reliably readable as a transcript in the public web view. The indexed creator materials and discussion around the related “识海工程拆解” post emphasize storage, activation, and decay as concrete memory questions; we treat that as a useful design prompt, not scientific validation. The system also adopts the engineering distinction between a language model proposing wording and local state logic deciding internal transitions. See [the referenced Douyin post](https://www.douyin.com/video/7686281845280951606) and [the user's original share](https://www.douyin.com/video/7684611830387330314).

## Stable personality and goals

The existing text persona controls natural-language style. Three versioned numeric settings—warmth, playfulness, and restraint—control local PetMind expression. They update immediately when settings are saved. State does not rewrite personality. Active goals remain task-scoped and explicit; no background goal may override quiet hours, permissions, or a user request to stop.

## Behavior and expression policy

`PetBehaviorPolicy` resolves basic conflicts before autonomous movement: low energy or high stress selects rest; sufficiently high curiosity may select permitted exploration; otherwise the existing bounded behavior cycle continues. It does not initiate external communication or tools. Renderer effects use bounded energy/stress/arousal values. User-facing text remains concise and respects the message and proactivity settings.

## Self-improvement boundary

The system may collect an approved failure case, formulate a hypothesis, create an isolated candidate, and compare it with a frozen baseline and holdout set. `OfflineCandidateEvaluator` performs paired scenario aggregation for quality, input/output tokens, model calls, latency, safety, privacy, and per-scenario regressions. It emits a deterministic SHA-256 digest over version labels and aggregate measurements without storing prompts or replies. `ImprovementGate` rejects missing/insufficient evidence, quality regressions, token cost overruns, latency increases beyond 20%, extra model calls, and any safety/privacy/regression failure. The `Piko.Improvement.Cli` command consumes a strict JSON measurement contract, reruns the evaluator and gate, appends an aggregate-only hash-chained JSONL entry, and verifies the ledger after writing. The hash chain detects accidental edits; it is not a signature or proof of who supplied measurements. A passing candidate is only eligible for maintainer review. This process cannot modify production, edit live prompts, grant permissions, or publish a candidate.

Every proposal should record candidate ID, risk, code/config version, model and prompt version, dataset/evaluation version, evidence digest, quality, cost, safety/privacy/regression results, decision, approver, and rollback version. Personal memory never becomes cross-user training data implicitly.

Run an already-isolated evaluator's paired measurements with `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- evaluate <measurements.json> <local-ledger.jsonl>`. The JSON file contains only version labels and per-scenario scalar measurements, has a 10 MiB limit, and is capped at 10,000 cases. It must not contain user prompts, replies, memories, or identifying scenario labels. After review, record an attributed decision with `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- review <local-ledger.jsonl> <candidate-id> <reviewer-id> <accept|reject> [rollback-version]`. Reviewer IDs are caller-provided labels, not authenticated identities; acceptance only records eligibility for manual integration and cannot deploy. The CLI aggregates evidence but does not execute candidate code or verify that upstream measurements were honestly produced; that isolated execution and human review are still required.

## Evaluation plan

The system is evaluated as a whole and by ablation:

1. Event appraisal: equal event inputs produce equal bounded transitions; uncertainty/control affect stress as specified; generated output alone does not mutate state.
2. Recovery: state returns toward baseline; rest increases energy and reduces stress; one event cannot permanently dominate future behavior.
3. Memory: relevant recall, expiry, sensitivity exclusion, false recall, deletion, and bilingual queries.
4. Personality: style remains stable across positive/negative events; numeric settings change local expression predictably.
5. Behavior: low energy/high stress favors rest; curiosity increases only approved exploration; quiet/user stop rules prevail.
6. Self-improvement: paired results on the same frozen holdout produce a stable digest; any per-case quality, call-count, latency, token-cost, safety, privacy, or regression failure blocks review eligibility; passing results still cannot change production.
7. Efficiency: record prompt tokens, model calls, latency, and successful tasks; compare the same scenarios with memory and optional state layers disabled.

Public datasets can supplement these checks, but the release gate is a versioned Piko-specific scenario set with disclosed model, prompt, evaluator, budget, sample count, and human spot checks.

## Release acceptance

V1 is ready when the deterministic state and behavior tests pass; the full Windows CI passes; memory controls remain clear and deletable; observed behavior is stable in real Windows desktop runs; and no state variable affects permissions or truth policy. The active development plan tracks current implementation status separately; this document defines the intended system, not a claim that every V1 milestone is already shipped.
