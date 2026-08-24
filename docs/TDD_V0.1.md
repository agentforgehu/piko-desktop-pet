# Piko World Lab Technical Design V0.1

## 1. Architecture

```text
Windows OS
   ↓
WindowsSnapshotProvider (Win32 + DWM + Monitor/DPI)
   ↓
DesktopSnapshot
   ↓
DesktopWorldCompiler (pure deterministic transformation)
   ↓
DesktopWorld (surfaces and diagnostics)
   ↓
World Lab viewer / later physics consumer
```

UI Automation is not part of the V0.1 geometry path. It will be an optional semantic provider for later file-progress and application-specific integrations.

## 2. Assemblies

```text
Piko.World
  Platform-independent records, geometry, compiler, serialization.

Piko.World.Windows
  Win32/DWM observation and physical-coordinate capture.

Piko.WorldLab
  WPF diagnostic viewer and manual capture/export workflow.

Piko.World.Tests
  Deterministic interval, occlusion, compilation, and replay tests.
```

Physics and navigation remain namespaces inside `Piko.World` until their interfaces stabilize.

## 3. Coordinate contract

- All core geometry is physical desktop pixels.
- Rectangles use half-open intervals: `[left,right)` and `[top,bottom)`.
- Monitor and window positions may be negative.
- UI rendering performs an explicit transform from world pixels into viewer DIPs.
- No WPF `Point` or `Rect` type crosses into `Piko.World`.

## 4. Snapshot schema

`DesktopSnapshot` contains:

- `schemaVersion`;
- timestamp;
- coordinate-space identifier;
- virtual-desktop bounds;
- monitor bounds, work areas, DPI, and primary flag;
- normalized window geometry, Z-order, eligibility, and diagnostic exclusion reason;
- cursor position.

Window titles are excluded by default. HWND values are diagnostic transient IDs and cannot be used as replay identity.

## 5. Observation strategy

V0.1 initially provides synchronous capture. The subsequent attached-body milestone adds:

- WinEvent hooks for location, foreground, minimize, and destroy signals;
- high-rate updates only for an attached or actively moving window;
- debounced whole-world recompilation capped around 10 Hz;
- low-frequency integrity polling while idle;
- a fixed-step 60 Hz physics loop independent of observation frequency.

## 6. World compiler

The compiler is pure: equal snapshots and options produce equal worlds.

For each eligible window:

1. create a candidate interval from its top edge;
2. find eligible windows above it in Z-order;
3. select occluders whose vertical range covers the candidate Y;
4. intersect their horizontal bounds with the candidate interval;
5. subtract and normalize all covered intervals;
6. discard segments below minimum surface width;
7. emit stable surface IDs derived from replay-stable window snapshot IDs and segment order.

Each monitor work-area bottom emits a fallback floor surface.

## 7. Viewer

The viewer renders:

- monitor bounds in neutral gray;
- eligible windows in green;
- excluded windows in translucent red;
- compiled horizontal surfaces in blue;
- future climb edges in yellow;
- diagnostics and snapshot metadata in a side panel.

Capture and export are explicit user actions. The viewer does not continuously persist desktop state.

## 8. Test-body milestone

The first body states are `Falling`, `Standing`, `Walking`, and `Dragging`.

An attachment stores the owner transient window ID, surface ID, and local X anchor. When support disappears, the body detaches, clears all invalid owner references, and enters `Falling` before seeking the nearest valid surface below.

## 9. Test strategy

Automated tests cover:

- interval subtraction and normalization;
- no occlusion, partial occlusion, split, and full occlusion;
- overlapping occluders;
- negative coordinates;
- minimum surface width;
- taskbar/work-area floor generation;
- snapshot JSON round trip;
- deterministic replay output.

Manual scenarios cover real applications, mixed DPI, window snapping, rapid resize, maximize/minimize, owner destruction, full screen, and long-running stability.

## 10. Performance budget

- no continuous 60 Hz enumeration of all windows;
- compiled-world updates are debounced and measurable;
- observation, compilation, physics, and rendering expose separate timings;
- diagnostic overlay can be disabled;
- release decisions use measured reference-machine data, not estimates alone.
