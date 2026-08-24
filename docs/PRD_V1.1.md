# Piko Desktop Pet PRD V1.1

## 1. Document scope

This is the product-requirements document version, not the product release number.

- Product: Piko Desktop Pet
- Platform: Windows 10/11 x64 first
- Product stage: MVP validation
- Product category: intelligent desktop pet; AI Companion capabilities are later releases
- Long-term form: Windows pet plus an optional physical ESP32 body

## 2. Product definition

Piko is a digital pet that lives in the user's Windows desktop space. It has persistent state, temperament, autonomous behavior, and a growing relationship with its owner. Windows, the taskbar, screen edges, the pointer, and selected system activities form its environment.

Piko is not a chat assistant with a mascot skin. Its primary emotional value comes from ambient behavior and spatial presence. AI conversation and agent execution are optional later capabilities.

## 3. Product principles

1. Piko behaves like a small life, not a button-triggered animation player.
2. The user is not a moment-to-moment remote controller, but retains full product control.
3. Ambient behavior is more important than proactive chat.
4. Absence does not punish the user or create guilt.
5. Sensitive sensing is off by default and individually permissioned.
6. Product actions use semantic IDs so PC art, a future ESP32 face, audio, and future motion can project the same intent differently.

## 4. First public MVP scope

### 4.1 Foundation

- transparent, borderless, always-on-top pet window;
- tray controls, show/hide, global recall, safe quit;
- position persistence and multi-monitor recovery;
- sprite animation system with interrupt points and anchors;
- local state persistence;
- quiet hours and behavior-frequency controls.

### 4.2 Desktop spatial behavior

The following six signature capabilities remain committed to the first public MVP:

1. climb eligible window sides;
2. jump between safe visible window surfaces;
3. hide at an outer screen edge while leaving a recallable peek region;
4. treat window tops, sides, and corners as furniture;
5. observe file-copy/download activity with explicit confidence levels;
6. briefly approach and pause near an idle pointer without blocking it.

These are delivered through sequential engineering gates. Their absence from World Lab V0.1 is not removal from the MVP.

### 4.3 Life and relationship

Internal state:

- energy;
- mood;
- curiosity;
- comfort;
- bond.

Long absence causes independent activity, rest, or exploration rather than ongoing negative-state accumulation. Bond may grow more slowly during absence but does not decay as punishment.

### 4.4 Direct interaction contract

- click: poke/greet;
- press on an eligible body region: petting reaction;
- hold and move beyond the drag threshold: pick up and drag;
- double-click: open pet status panel;
- right-click: quick menu;
- fast repeated clicks: bounded special reaction;
- fast drag release: optional throw gesture.

Exact time and distance thresholds are settings owned by the interaction specification, not animation code.

## 5. Deferred scope

- LLM conversation;
- voice input/output;
- autonomous agent execution;
- account and cloud sync;
- marketplace and monetization;
- multiplayer/social systems;
- complex disease/death mechanics;
- public engine SDK;
- non-Windows platforms;
- production ESP32 firmware.

## 6. Privacy and user control

Default operation is local and offline. The user can independently disable:

- window exploration;
- pointer awareness;
- file-activity awareness;
- proactive behavior;
- autostart;
- future AI/network access.

World diagnostics do not record window titles by default. File observation does not read file contents. Sensitive data cannot enter long-term memory without explicit policy and permission.

## 7. Release gates

| Gate | Outcome |
|---|---|
| World Lab V0.1 | Window discovery, surfaces, stand/follow/fall foundation |
| World Lab V0.2 | Occlusion, multi-monitor, mixed-DPI stability |
| World Lab V0.3 | Climb, corner transition, jump, recovery |
| World Lab V0.4 | Peek, pointer pause, window-furniture behaviors |
| World Lab V0.5 | File-copy/download observation experiment |
| Piko MVP | Original character, life state, settings, persistence, all six signature behaviors |

## 8. MVP quality targets

- pet visible within three seconds of normal launch target;
- no focus theft during autonomous behavior;
- global recall always available;
- support 100%-200% display scaling target;
- safe handling of negative-coordinate monitor layouts;
- idle CPU target below 2% on the reference machine;
- active CPU target below 8% on the reference machine;
- no material memory growth during an eight-hour soak test;
- default network traffic: none.

## 9. Product success

The MVP succeeds when users describe Piko as something that lives on their desktop, not as a decorative widget. Quantitative measures include interaction acceptance, mute/hide rate, weekly active days, crash-free sessions, and the frequency with which users voluntarily recall or approach Piko.
