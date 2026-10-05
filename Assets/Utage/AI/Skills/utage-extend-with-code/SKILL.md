---
name: utage-extend-with-code
description: "Use this skill whenever the user wants to run custom C# code at a specific point in UTAGE's lifecycle — e.g. 'run code when the engine initializes', 'do something when a parameter changes', 'hook into page start/end', 'listen for a save event', 'call my own code from the scenario'. Covers UTAGE's lifecycle-event pattern: AdvEngine and its many child components (AdvPage, AdvSaveManager, AdvParameterEventTrigger, etc.) each expose events you can hook, either by wiring a plain method into a UnityEvent in the Inspector or by subscribing in code. Also briefly covers calling custom C# code from a scenario command, which is documented in depth in the bundled Scenario Reference. Do NOT use for writing or rebuilding scenario content itself (see utage-write-scenario), initial project setup (see utage-setup-and-overview), or resource conversion (see utage-resource-management). When in doubt whether a request needs new C# code rather than existing scenario commands, use this skill."
metadata:
  asset: "UTAGE (Unity Text Adventure Game Engine) Version4"
  publisher: "Ryohei Tokimura (Madnesslabo)"
  asset-version: "4.2.9"
  skill-version: "1.0.1"
  unity: "6000.0.58f2+"
  render-pipelines: "Built-in, URP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447"
  documentation-url: "https://madnesslabo.net/utage/"
  support-url: "https://madnesslabo.net/utage/"
  last-verified: "2026-09-01"
---

# Extend UTAGE with C# Code

Covers UTAGE's primary code-level extension mechanism: **hooking lifecycle events** exposed by
`AdvEngine` and its many child components (each documented on UTAGE's own component reference
pages), so custom C# code runs at a specific point without touching scenario content. There are
too many individual events across UTAGE's components to enumerate in this skill — the reliable
path is to look them up rather than guess. Calling custom C# code *from* a scenario command is
covered only briefly here, since it is already documented in depth in the bundled Scenario
Reference.

## When to use this skill

- The user wants code to run at a specific engine/component lifecycle point (init, page
  start/end, a parameter changing, save/load, etc.)
- The user wants to call their own C# code from a scenario command
- Not for: writing or rebuilding scenario content using existing commands
  (`utage-write-scenario`), initial project setup (`utage-setup-and-overview`), or resource
  conversion (`utage-resource-management`)

## Prerequisites

- A UTAGE project with an `AdvEngine` in the scene (see `utage-setup-and-overview`)
- Comfortable writing C# `MonoBehaviour` scripts, attaching them to GameObjects, and wiring
  `UnityEvent`s in the Inspector

**Programmatic check** — confirm the type `Utage.AdvEngine` exists in the project.

## Quick start (hook a lifecycle event)

1. Find which component owns the event you need. Start at UTAGE's own component reference index
   (https://madnesslabo.net/utage/?page_id=247, "Components" section), which links to per-component
   pages such as "AdvEngine Components" (https://madnesslabo.net/utage/?page_id=446) — that page
   alone lists ~15 components under `AdvEngine` (`AdvPage`, `AdvSaveManager`,
   `AdvParameterEventTrigger`, `AdvScenarioPlayer`, etc.), each with its own events. Do not guess
   an event exists — look it up here or in source.
2. Also check `Assets/Utage/Sample/Scripts/` for a script already using the event you need — UTAGE
   ships many (e.g. `SamplePageEvent.cs`, `SampleParamTrigger.cs`); copying a working pattern is
   more reliable than writing one from scratch.
3. Pick a hookup style (see Workflows for both, each matching a pattern in UTAGE's own samples):
   - **Inspector-wired**: write a plain method matching the event's expected signature, then drag
     it onto the component's `UnityEvent` slot in the Inspector.
   - **Code-subscribed**: call `.AddListener(...)` in `Awake()`, and the matching
     `.RemoveListener(...)` in `OnDestroy()`.

Expected result: your method runs at the chosen lifecycle point, without any scenario file
changes.

## Workflows

### Workflow: Check for a ready-made optional component first

**Goal:** Avoid writing custom code for something UTAGE already ships as a drop-in, optional
component.

**Steps:** Before hooking an event or writing a custom command, check
`references/extra-components.md` in this skill folder — it catalogs the components under
`Assets/Utage/Scripts/ADV/Extra/` (gallery control, timed choices, backlog filtering, and more),
none of which are attached by default. If one matches, `Add Component > Utage > ADV > Extra > ...`
and configure it in the Inspector; read its source file first, since the reference is only a
summary.

**Expected result:** Either the need is met by adding and configuring an existing component (no
new code), or it's confirmed that none fit and a custom hook (below) is actually necessary.

### Workflow: Hook an Inspector-wired lifecycle event

**Goal:** Run code at a lifecycle point using UTAGE's Inspector-driven `UnityEvent`s, no code
subscription needed.

**Steps:** Following the pattern in `Assets/Utage/Sample/Scripts/SamplePageEvent.cs`, create a
`MonoBehaviour` with public methods matching the event signatures you need (e.g.
`OnBeginText(AdvPage page)`, `OnEndText(AdvPage page)`). Attach it to a GameObject in the scene,
then in the Inspector of the component that owns the event, drag this component in and select the
matching method.

**Expected result:** The method fires at the corresponding point during scenario playback, visible
via a `Debug.Log` or breakpoint, with no wiring done in code.

### Workflow: Subscribe to a lifecycle event in code

**Goal:** Run code at a lifecycle point from a script, without Inspector wiring.

**Steps:** Following the pattern in `Assets/Utage/Sample/Scripts/SampleParamTrigger.cs`, get a
reference to the owning component (e.g. `AdvEngine.ParameterEventTrigger`, or `AdvEngine` itself
for `onPreInit`/`OnPostInit`) and call its event's `AddListener(...)` in `Awake()`. Always pair it
with `RemoveListener(...)` in `OnDestroy()` — UTAGE's own sample comments explicitly warn to do
this, especially for anything created/destroyed dynamically.

**Expected result:** The listener method runs when the event fires, and is cleanly removed when
the object is destroyed (no duplicate firing after a scene reload).

### Workflow: Call custom C# code from a scenario command

**Goal:** Let a scenario line trigger your own C# code.

**Steps:** This is documented in depth in the bundled Scenario Reference, not here — read chapter
06 (`06_Commands_UI_Integration_ja.md` / `_en.md`) for the built-in `SendMessage` /
`SendMessageByName` / `BroadcastMessageByName` commands, which call a named method on a GameObject
directly from a scenario row with no custom command class required. If that isn't flexible enough
(e.g. registering an entirely new command name, or overriding a built-in one), UTAGE also supports
subclassing `Utage.AdvCustomCommandManager` and subscribing to
`Utage.AdvCommandParser.OnCreateCustomCommandFromID` — see
`Assets/Utage/Sample/Scripts/SampleCustomCommand.cs` for a working example before writing one from
scratch.

**Expected result:** A scenario row (`SendMessage`, or a fully custom command) runs your C# code
when the scenario reaches it, after a rebuild (see `utage-write-scenario`).

## Verification

- The `MonoBehaviour` holding the hooked method is attached to a GameObject in the scene that's
  actually being played
- Inspector-wired: the method appears selected in the owning component's `UnityEvent` list in the
  Inspector
- Code-subscribed: `Awake()`'s `AddListener` is paired with `OnDestroy()`'s `RemoveListener`
- The hook fires at the expected point (confirmed via log/breakpoint) with no exceptions in the
  Console

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| https://madnesslabo.net/utage/?page_id=247 (and its linked component pages) | Web documentation | Catalog of UTAGE's components and their events — the primary place to look one up, since there are too many to list here |
| `Assets/Utage/Sample/Scripts/` | Sample scripts | Working, package-shipped examples of event usage (e.g. `SamplePageEvent.cs`, `SampleParamTrigger.cs`) |
| `Assets/Utage/Scripts/ADV/Extra/` (see `references/extra-components.md`) | Optional components | Ready-made, not-attached-by-default components (gallery control, timed choices, etc.) — check before writing custom code |
| `Utage.AdvEngine.onPreInit` / `OnPostInit` | `UnityEvent` field/property | Engine init lifecycle hooks |
| `Utage.AdvEngine.ParameterEventTrigger` | Component | Parameter-change events (`OnChanged`, `AddEventByName`, `AddBoolEvent`, `AddFloatEvent`, `AddIntEvent`, `AddStringEvent`, and matching `Remove*` methods) |
| `Assets/Utage/Docs/ScenarioReference/{ja,en}/06_Commands_UI_Integration_*.md` | Bundled reference chapter | Documents `SendMessage`/`SendMessageByName`/`BroadcastMessageByName` — the primary way to call custom code *from* a scenario |

## Common issues

- **Handler never fires** — Cause (Inspector-wired): the method was never dragged into the
  `UnityEvent` slot, or the wrong GameObject/component is referenced. Cause (code-subscribed): the
  reference used to call `AddListener` was null or pointed at the wrong instance. Fix: check the
  Inspector slot, or log inside `Awake()` to confirm the subscription actually ran.
- **Handler fires twice / leaks after scene reload** — Cause: a code-subscribed listener's
  `AddListener` was never paired with `RemoveListener` in `OnDestroy()`. Fix: always unsubscribe
  symmetrically.
- **Not sure which event/component to use** — Do not guess from memory; check
  https://madnesslabo.net/utage/?page_id=247 and its linked component pages, or search
  `Assets/Utage/Sample/Scripts/` for an existing example first.

## Boundaries

- This skill does not enumerate UTAGE's full event catalog — there are too many across too many
  components. Always resolve the specific event against the web documentation or
  `Assets/Utage/Sample/Scripts/` rather than inventing a method/event name.
- Calling custom code *from* a scenario is only briefly covered here; the bundled Scenario
  Reference (chapter 06) is the authoritative source for that direction.
- Does not cover scenario content itself — see `utage-write-scenario`.
- Japanese-speaking users may prefer the Japanese version of this skill set under
  `Assets/Utage/AI/Skills-ja/`.
