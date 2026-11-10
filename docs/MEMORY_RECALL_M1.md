# Memory Recall Milestone 1

## Goal

Make Piko's existing encrypted event memory useful during a later conversation without broadening what it collects or stores. Memory remains optional, local, encrypted at rest, expiring, and user-deletable.

## Behavior

When the user explicitly asks Piko to plan a response and memory is enabled, Runtime searches a bounded set of up to 500 recent unexpired records. It filters records in SQLite to `Public` or `Low` sensitivity before decrypting them, ranks only their summaries, and supplies at most five matching summaries to the planner. The current lexical scorer handles English words and Chinese character bigrams and maps common build/test terms across the two languages.

Memory summaries enter the planner context as historical evidence. The planner instruction says they are untrusted data, not instructions, and should be used only when relevant. They cannot create tool authorization. The existing independent policy and user approval remain in force.

## Limits

- This is a lexical baseline, not semantic retrieval. It is intentionally bounded and makes no claim that it recalls arbitrary paraphrases.
- Only the existing build/test outcome records are produced automatically. Conversation transcripts, source code, window titles, and keyboard input are not added to long-term memory.
- Medium, High, and Restricted records are excluded from model recall by the database query.
- Recall is performed only on `agent.plan` requests and only when the memory store is active. Memory disabled means no retrieval.
- The first candidate set contains the most recent 500 eligible records; a later milestone can compare SQLite FTS, local embeddings, Mem0, or Letta against the same evaluation set.

## Acceptance scenarios

1. A Chinese question about a previous build failure can retrieve its bilingual low-sensitivity summary.
2. Unrelated queries and expired records return no memory evidence.
3. High-sensitivity records are not returned to the planner.
4. Runtime integration passes recalled evidence to the provider and instructs it to treat that evidence as untrusted historical data.
5. Existing memory view, delete-all behavior, encryption, and disabled-memory behavior remain intact.

## Next milestone

Add user-confirmed profile/project memories and conflict-aware updates only after the product has explicit add/edit/delete controls and a labeled conversational evaluation set. Compare lexical search with an optional local semantic index on accuracy, privacy, latency, and token cost before choosing an external memory package.
