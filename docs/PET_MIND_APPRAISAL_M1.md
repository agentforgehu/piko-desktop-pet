# PetMind Appraisal Milestone 1

## Goal

Give Piko's existing short-term mood variables an explainable event path. This is a bounded interaction simulation inspired by appraisal models, not a claim that Piko has subjective feelings.

## State update

`PetAppraisalEvent` carries a trigger, relevance to an active goal, expected outcome valence, arousal change, certainty, controllability, energy change, and relationship change. `PetMind.Appraise` clamps every input, uses lower certainty and controllability to increase arousal modestly, and records a `PetEmotionTransition` with before/after state and its trigger.

Existing stimulus and model response paths flow through the same appraisal method. Model-selected emotion/action labels remain allowlisted by `AgentPlanner`; a generated expression does not increase the relationship variable. Explicit local social interactions can change it only by a small bounded increment. Tool permissions remain controlled by the separate Runtime policy.

## Limits and next step

The first version uses fixed weights and per-event updates. It does not infer a user's psychological state, persist mood across sessions, or learn weights online. The next iteration should make decay/weights configurable, log transition reason codes without conversation text, and compare state/response consistency on labeled interaction scenarios before changing defaults.
