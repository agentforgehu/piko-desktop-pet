# PetMind Appraisal Milestone 1

## Goal

Give Piko's existing short-term mood variables an explainable event path. This is a bounded interaction simulation inspired by appraisal models, not a claim that Piko has subjective feelings.

## State update

`PetAppraisalEvent` carries a trigger, relevance to an active goal, expected outcome valence, arousal change, certainty, controllability, energy change, and relationship change. `PetMind.Appraise` clamps every input, uses lower certainty and controllability to increase arousal modestly, and records a `PetEmotionTransition` with before/after state and its trigger.

Explicit local stimulus paths flow through the appraisal method. Model-selected emotion/action labels remain allowlisted for presentation; generated text and labels do not mutate internal state or relationship. Only evaluated local events can update those values, with bounded increments. Tool permissions remain controlled by the separate Runtime policy.

## Limits and next step

The first version uses fixed weights and per-event updates. It does not infer a user's psychological state, persist mood across sessions, or learn weights online. The next iteration should make decay/weights configurable, log transition reason codes without conversation text, and compare state/response consistency on labeled interaction scenarios before changing defaults.
