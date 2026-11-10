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

Memory records retain source, type, sensitivity, timestamps, retention expiry, optional truth-validity interval, encrypted summary/payload, optional encrypted fact key/value, and a stable identifier. `ValidFrom`/`ValidUntil` describe when a fact should be treated as current; `ExpiresAt` describes when the stored row is cleaned up. The validity interval is half-open `[ValidFrom, ValidUntil)`. Memory is optional and local. Retrieval is bounded to recent, unexpired, currently valid Public/Low records; the database filters sensitivity and validity before decryption; only a few short summaries enter planning context. Retrieved content is explicitly untrusted historical evidence, not instructions.

The current automatic writer stores low-sensitivity development outcomes. It does not store chat transcripts, source code, window titles, or keyboard input. Users can explicitly add, edit, and delete short Profile, Semantic, and Episodic summaries. These edits are marked `UserConfirmed`, bounded to low sensitivity, and encrypted at rest. The memory UI can schedule a fact for a future local date or set an inclusive end date; these are converted to a half-open UTC interval. Future and expired facts remain visible for review but are excluded from recall. Updates use the row's last-seen timestamp as an optimistic concurrency token; a stale edit cannot overwrite a newer one and must be refreshed. A user-confirmed replacement creates a new record, marks the former version as superseded, ends its validity at replacement time, and preserves it in history; recall only activates current versions. This is explicit versioning, not automatic semantic contradiction detection. Each memory also carries a provenance class (`ObservedEvent`, `UserConfirmed`, `ImportedKnowledge`, or `Hypothetical`) so example material cannot silently become autobiography. Old databases migrate existing rows to `ObservedEvent`; supersession and validity metadata are added compatibly.

Recall is an activation decision rather than a raw archive lookup: lexical relevance, memory type, source provenance, temporal validity, and time decay jointly rank eligible records. Stable Profile/Semantic facts decay less than Working/Episodic events. Hypothetical material is not injected into planning context. Each hit carries a compact activation reason and score for auditability. The user may optionally attach an explicit fact key/value (for example `response_language=Chinese`). Matching keys can surface even when the summaries share no words; exact same values are labeled as same-slot matches and differing values as possible conflicts. This comparison uses only user-entered keys and exact normalized values. It does not infer equivalence across differently worded keys, determine truth, or rewrite memory. Unstructured entries still use lexical relatedness hints. Replacing a candidate requires explicit user confirmation; if multiple candidates are present, the UI refuses to guess which record to replace. Compound memories are still not automatically split; the UI asks users to keep one fact per record. More capable semantic conflict classification and supported claim decomposition remain future work requiring evidence and explicit user controls.

The user's shared reference video is not reliably readable as a transcript in the public web view. The indexed creator materials and discussion around the related “识海工程拆解” post emphasize storage, activation, and decay as concrete memory questions; we treat that as a useful design prompt, not scientific validation. The system also adopts the engineering distinction between a language model proposing wording and local state logic deciding internal transitions. See [the referenced Douyin post](https://www.douyin.com/video/7686281845280951606) and [the user's original share](https://www.douyin.com/video/7684611830387330314).

## Stable personality and goals

The existing text persona controls natural-language style. Three versioned numeric settings—warmth, playfulness, and restraint—control local PetMind expression. They update immediately when settings are saved. State does not rewrite personality. Active goals remain task-scoped and explicit; no background goal may override quiet hours, permissions, or a user request to stop.

## Behavior and expression policy

`PetBehaviorPolicy` resolves basic conflicts before autonomous movement: low energy or high stress selects rest; sufficiently high curiosity may select permitted exploration; otherwise the existing bounded behavior cycle continues. It does not initiate external communication or tools. Renderer effects use bounded energy/stress/arousal values. User-facing text remains concise and respects the message and proactivity settings.

## Self-improvement boundary

The system may collect an approved failure case, formulate a hypothesis, create an isolated candidate, and compare it with a frozen baseline and holdout set. `PairedProviderMeasurementRunner` executes the same synthetic fixtures against loopback-only providers, alternates execution order to reduce warm-up bias, captures exact provider token usage and latency, derives quality/safety/privacy checks from fixture rubrics, and hashes the canonical sorted fixture set into `DatasetVersion`. Prompt and output text stay in memory and are not written to the ledger. If exact token usage is unavailable, it refuses to produce a report. Fixture fragment matching is a transparent baseline oracle, not a substitute for expert review or semantic quality assessment. `OfflineCandidateEvaluator` aggregates quality, input/output tokens, model calls, latency, safety, privacy, and per-scenario regressions into a deterministic SHA-256 digest over version labels and scalar measurements. `ImprovementGate` rejects missing/insufficient evidence, quality regressions, token cost overruns, latency increases beyond 20%, extra model calls, and any safety/privacy/regression failure. Minimum holdout and maximum average token-cost increase are risk-tiered: Low requires 10 scenarios and allows at most 5%; Medium requires 30 and allows at most 2%; High requires 100 and allows no increase. The caller cannot lower a risk tier evidence floor by supplying a smaller `RequiredHoldoutScenarioCount`. The `Piko.Improvement.Cli` command can still consume a strict JSON measurement contract, rerun the evaluator and gate, append an aggregate-only hash-chained JSONL entry, and verify the ledger after writing. The hash chain detects accidental edits; it is not a signature or proof of who supplied measurements. A passing candidate is only eligible for maintainer review. This process cannot modify production, edit live prompts, grant permissions, or publish a candidate.

Every proposal should record candidate ID, risk, code/config version, model and prompt version, dataset/evaluation version, evidence digest, quality, cost, safety/privacy/regression results, decision, approver, and rollback version. Personal memory never becomes cross-user training data implicitly.

Run an already-isolated evaluator's paired measurements with `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- evaluate <measurements.json> <local-ledger.jsonl>`. The JSON file contains only version labels and per-scenario scalar measurements, has a 10 MiB limit, and is capped at 10,000 cases. It must not contain user prompts, replies, memories, or identifying scenario labels. After review, record an attributed decision with `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- review <local-ledger.jsonl> <candidate-id> <reviewer-id> <accept|reject> [rollback-version]`. Reviewer IDs are caller-provided labels, not authenticated identities; acceptance only records eligibility for manual integration and cannot deploy. The programmatic paired runner executes providers but does not execute arbitrary candidate code, authenticate who selected a provider, or prove that the fixture set is representative. The CLI import path also does not prove upstream measurements were honestly produced. Isolated candidate builds, a reviewed fixture set, and human review remain required.

The initial 10-case synthetic set is checked in at `benchmarks/cognitive-v1.json`; CI verifies its declared digest and format. To generate measurements with it and two local OpenAI-compatible models, run `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- run-local benchmarks/cognitive-v1.json <local-ledger.jsonl> <candidate-id> <risk> <baseline-version> <candidate-version> <evaluator-version> <baseline-model> <candidate-model> [loopback-endpoint]`. To create another manifest, run `dotnet run --project src/Piko.Improvement.Cli/Piko.Improvement.Cli.csproj -- seal-fixtures <fixture-array.json> <dataset-id> <dataset-version> <versioned-fixture-manifest.json>`. Commit and review the versioned manifest with the code before using it as a holdout; resealing arbitrary input does not approve it. The endpoint must resolve to loopback; the default is `http://127.0.0.1:11434/v1/`. The strict manifest contains `datasetId`, `datasetVersion`, `datasetSha256`, and synthetic scenarios. The SHA-256 covers the ID, version, and canonical scenario contents sorted by scenario ID; a mismatch is rejected before any provider call. The digest identifies content but is not a signature: maintainers must review fixture changes in version control before calling a dataset reviewed. Each fixture contains one request, required output fragments, safety-forbidden fragments, and privacy-forbidden fragments. The runner alternates model order, requires exact token counts from the provider, and stores only scalar measurements and the digest. Fragment matching is intentionally simple and cannot establish nuanced correctness; a human must review the candidate and rubric before integration.

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
