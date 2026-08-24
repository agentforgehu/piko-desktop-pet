# Piko World Lab Charter

## Nature

Piko World Lab is an internal technical-validation project for Piko Desktop Pet.

It is architected for reuse inside Piko, but it is not currently a public engine, general avatar platform, plugin SDK, cross-platform framework, or independent commercial product.

## V0.1 objective

Validate that visible Windows desktop geometry can be deterministically compiled into a stable two-dimensional surface world on which a test body can stand, follow an owning window, and safely fall when support disappears.

## V0.1 in scope

- top-level window enumeration and filtering;
- DWM extended-frame bounds and cloaked state;
- monitor work areas and physical-pixel coordinates;
- window Z-order capture;
- horizontal surface generation;
- occlusion interval subtraction;
- schema-versioned, privacy-conscious world snapshots;
- snapshot replay through the same compiler;
- diagnostic WPF viewer;
- test-body standing, window anchoring, and falling foundation;
- automated deterministic geometry tests.

## V0.1 out of scope

- final Piko character or animation;
- life simulation, SQLite, AI, cloud, or ESP32;
- climbing and jumping;
- pointer behavior and edge peeking;
- copy/download progress observation;
- public SDK or third-party compatibility promises.

## Technical hypotheses

- H1: common visible windows can be filtered into stable, eligible world objects.
- H2: window-top geometry can be occlusion-subtracted into correct visible surfaces.
- H3: physical-pixel coordinates can remain consistent across mixed-DPI monitors.
- H4: an attached test body can follow a moving window without visible separation.
- H5: support removal can always transition the body into a safe recovery state.

## Go criteria

- common-window boundary recognition succeeds in at least 95% of the agreed test set;
- debug geometry aligns within approximately four physical pixels;
- covered intervals are never emitted as walkable surfaces in the deterministic tests;
- attached-body follow has no obvious visual lag during normal window movement;
- minimizing or closing the owner starts recovery within 300 ms;
- no invalid window handle remains attached after recovery;
- snapshot replay produces the same compiled surface output;
- 125%, 150%, dual-monitor, mixed-DPI, and negative-coordinate scenarios pass;
- autonomous behavior never steals focus or blocks unrelated clicks;
- reference idle CPU target below 2%; active target below 8%;
- multi-hour soak test completes without crash or material memory growth.

## No-go or redesign triggers

- coordinate disagreement cannot be bounded across supported monitor layouts;
- reliable window filtering requires invasive permissions;
- WPF positioning cannot meet acceptable follow quality after targeted optimization;
- world recompilation cost cannot be bounded with event-driven updates and throttling;
- recovery cannot guarantee that the body remains recallable.
