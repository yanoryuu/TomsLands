---
name: utage-write-scenario
description: "Use this skill whenever the user wants to write or edit UTAGE scenario script content, rebuild it, or fix an import error — e.g. 'write a scene where the character confesses', 'add a choice branch here', 'add background music to this scene', 'rebuild the scenario', 'why is my scenario not updating in the game', 'I'm getting an import error', 'the character name isn't recognized', 'check what page the scenario is on'. Covers writing/editing the Excel/CSV scenario data (by pointing at the package's own bundled Scenario Reference rather than re-deriving syntax here), rebuilding it via the Scenario Data Builder, reading Console import errors, and using the Scenario Viewer to inspect playback state. Do NOT use for initial project setup (see utage-setup-and-overview), AssetBundle/dicing resource conversion (see utage-resource-management), or C# extension (see utage-extend-with-code). When in doubt whether a request is about UTAGE scenario content — writing it or getting it working — use this skill."
metadata:
  asset: "UTAGE (Unity Text Adventure Game Engine) Version4"
  publisher: "Ryohei Tokimura (Madnesslabo)"
  asset-version: "4.2.9"
  skill-version: "1.0.0"
  unity: "6000.0.58f2+"
  render-pipelines: "Built-in, URP"
  category: "tools/game-toolkits"
  asset-store-url: "https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447"
  documentation-url: "https://madnesslabo.net/utage/"
  support-url: "https://madnesslabo.net/utage/"
  last-verified: "2026-09-01"
---

# Write, Rebuild, and Debug UTAGE Scenario Content

Covers the full loop of working on a UTAGE scenario: writing/editing its Excel/CSV content,
rebuilding that content into UTAGE's runtime format, and diagnosing what goes wrong. This skill
deliberately does not re-teach the scenario syntax — UTAGE ships a complete, authoritative syntax
and command reference inside the package itself, and this skill's job is to point an agent at it,
enforce the "only use names that are actually defined" discipline, and cover the rebuild/debug
tools UTAGE ships alongside it.

## When to use this skill

- The user wants to add or edit dialogue, character expressions, choices, branching, or any
  in-scene command in an existing or new UTAGE scenario
- The user asks how UTAGE scenario syntax works
- After editing a scenario file, to make the changes take effect
- An import error appears in the Console after editing
- The game isn't showing the expected scenario content and the cause is unclear
- Not for: initial project setup (see `utage-setup-and-overview`), AssetBundle/dicing resource
  conversion (see `utage-resource-management`), or C# extension (see `utage-extend-with-code`)

## Prerequisites

- A UTAGE project already set up (see `utage-setup-and-overview` if not)
- A scenario file (Excel `.xls`/`.xlsx` or CSV) under `Assets/<ProjectName>/Scenarios/` — the New
  Project wizard creates one automatically (`Scenarios/<ProjectName>.xls`), along with an
  `AdvScenarioDataProject` asset at `Assets/<ProjectName>/<ProjectName>.project.asset`
- The package's bundled Scenario Reference, which ships inside `Assets/Utage/Docs/ScenarioReference/`:
  - Japanese (canonical): `Assets/Utage/Docs/ScenarioReference/ja/README.md`
  - English (generated from the Japanese original): `Assets/Utage/Docs/ScenarioReference/en/README.md`

**Programmatic check** — confirm `Assets/Utage/Docs/ScenarioReference/ja/README.md` (or the `en`
counterpart) exists, and that the type `Utage.AdvScenarioDataProject` exists, before proceeding. If
either is missing, the UTAGE import may be incomplete; tell the user to re-import the package.

## Quick start

**Writing content:**

1. Open the bundled reference README in the language you're working in (`ja/README.md` or
   `en/README.md`).
2. Follow its own "How to use" steps in order — do not skip ahead:
   - Read chapter 01 first for column layout, labels, `PageCtrl`, and text tags
   - Check chapters 02–06 for the argument spec of whichever commands you need
   - Use **only** character names, texture labels, sound labels, and variables already defined in
     chapter 07's setting sheets — an undefined name causes an import error
3. If the project already has scenario files, read them first and stay consistent with the
   character names and labels already in use there. Do not invent new names.
4. Write or edit the scenario content in the Excel/CSV file, following the syntax and argument
   specs from the reference.

**Rebuilding so the edit takes effect:**

5. Open `Tools > Utage > Scenario Data Builder`.
6. In the "Project" field at the top, make sure the project's `<ProjectName>.project.asset` is
   assigned (it is auto-assigned right after New Project creates it; if this window shows a
   different or empty project, assign the right one).
7. Below "Import Scenario Files", click **Import** (disabled if no scenario file is registered —
   see Common issues).
8. Check the Console. No errors means the rebuild succeeded and the edited content is live.
9. If there are errors, do not guess a fix — read chapter 08 of the bundled reference
   (`08_CommonErrors_ja.md` / `08_CommonErrors_en.md`), which documents how to read UTAGE's import
   error messages and their typical causes, then fix the scenario file and re-import.

Expected result: scenario content that follows UTAGE's documented syntax, only references names
that exist in the project's setting sheets, and imports with no Console errors.

## Workflows

### Workflow: Add new scenario content to an existing file

**Goal:** Extend an existing scenario (add dialogue, a new branch, new commands) without breaking
what already works.

**Steps:** Read the target scenario file and the setting sheets (chapter 07) first to learn the
character names, image/sound labels, and existing label structure (`*label` markers, `PageCtrl`
usage) actually in use. Add new rows following the same conventions, using chapters 02–06 for the
exact syntax of each command used. Then rebuild (Quick start steps 5–9).

**Expected result:** New rows that use only already-defined names, with `Jump`/label targets that
exist, and a rebuild that completes with no Console errors.

### Workflow: Start a scenario from the template created by New Project

**Goal:** Turn the near-empty scenario template created by `utage-setup-and-overview`'s New
Project wizard into real content.

**Steps:** Open `Scenarios/<ProjectName>.xls`, read chapter 01 for the required column structure,
then write dialogue/commands per chapters 02–06 — reusing whatever placeholder characters/labels
already exist in the template's chapter-07 setting sheets, or asking the user for real ones. Then
rebuild (Quick start steps 5–9).

**Expected result:** A scenario sheet with real content that follows the documented column
structure, only references defined names, and imports cleanly.

### Workflow: Diagnose an import error

**Goal:** Turn a Console import error into a specific fix in the scenario file.

**Steps:** Read the full error message and its context (which file/row it names). Cross-reference
it against chapter 08 of the bundled reference for the matching symptom. Common root causes are an
undefined character/texture/sound/variable name (see chapter 07), a malformed row (wrong column
count/blank required cell), or a broken `Jump`/label target.

**Expected result:** The specific cause identified and fixed in the scenario file, followed by a
re-import (Quick start) that completes with no errors.

### Workflow: Inspect current playback state while debugging

**Goal:** See what scenario/page/label the engine is actually on, while iterating.

**Steps:** Ensure a scene with an `AdvEngine` is open (and typically in Play Mode, actually running
the scenario). Open `Tools > Utage > Viewer > Scenario Viewer`. If it reports "Not found
AdvEngine", the wrong scene is open or no `AdvEngine` exists in it — fix that first (see
`utage-setup-and-overview`).

**Expected result:** The viewer shows the current scenario/page data, useful for confirming
whether playback reached the point the user expects.

## Verification

- Every character name, texture label, sound label, and variable used is defined in the project's
  setting sheets (chapter 07 of the reference)
- Every `Jump`/label target referenced actually exists in the scenario
- The syntax used (columns, `PageCtrl`, tags) matches chapter 01 and the relevant command chapter
  (02–06)
- The Console shows no errors after clicking Import — this, not the written syntax alone, is the
  authoritative pass/fail signal
- The Scenario Viewer (with the scene open and an `AdvEngine` present) reflects the expected
  current scenario state
- If the user reported unexpected behavior, the specific row/command responsible has been located
  and traced to a concrete cause, not just "it works now"

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Assets/Utage/Docs/ScenarioReference/ja/README.md` / `en/README.md` | Bundled Markdown reference | The authoritative, package-shipped syntax and command reference this skill defers to |
| `Assets/<ProjectName>/Scenarios/<ProjectName>.xls` | Excel scenario data | The scenario content file itself, created by the New Project wizard |
| `Tools > Utage > Scenario Data Builder` | Editor menu item | Opens the window whose Import button rebuilds scenario data from the Excel/CSV source |
| `Utage.AdvScenarioDataProject.ImportAll()` | Method | The underlying call the Import button triggers; rebuilds all registered scenario files for the project |
| `Tools > Utage > Viewer > Scenario Viewer` | Editor menu item | Inspects the current scenario/page state of an `AdvEngine` in the open scene |
| `Tools > Utage > Tools > Scenario Character Validator` | Editor menu item | Validates that scenario text only uses expected characters (e.g. to catch glyphs your font/localization doesn't cover) — a text-content check, not a check of character *names* |

## Common issues

- **Import error after editing** — do not guess at a fix; read chapter 08 (`08_CommonErrors_ja.md`
  / `08_CommonErrors_en.md`) of the bundled reference, which documents how to read the error and
  the typical causes.
- **Import error referencing an undefined name** — the most common cause; a character/texture/
  sound/variable name used in the scenario isn't defined in the setting sheets (reference chapter
  07). Fix: add the definition, or correct the name to match an existing one.
- **Import button stays disabled** — Cause: no scenario file is registered on the project asset
  (`IsEnableImport` is false, which requires `GetAllScenarioFiles()` to return at least one
  non-null path). Fix: confirm the project has at least one scenario file, or that the right
  `.project.asset` is assigned in the Project field.
- **Scenario Viewer says "Not found AdvEngine"** — Cause: the currently open scene has no
  `AdvEngine`, or no scene is open. Fix: open the correct UTAGE scene (see
  `utage-setup-and-overview`).
- **Edits don't appear to take effect** — Cause: the scenario was edited but never re-imported.
  Fix: run the Import step in Quick start; UTAGE does not treat the source Excel/CSV as live at
  runtime.

## Boundaries

- This skill intentionally does not restate UTAGE's scenario syntax — it always defers to the
  bundled reference so the two never drift out of sync. If the bundled reference is missing or
  looks incomplete, say so rather than inventing syntax from memory.
- Does not cover AssetBundle building or "dicing" resource conversion, which are separate tools
  (`Tools > Utage > Resource Converter`, `Tools > Utage > Dicing Converter`) — see
  `utage-resource-management`.
- The Scenario Character Validator checks the *text characters* used (e.g. font/glyph coverage),
  not whether character *names* are defined — don't conflate the two when debugging.
- Does not cover code-level extension — see `utage-extend-with-code`.
- Japanese-speaking users may prefer the Japanese version of this skill set under
  `Assets/Utage/AI/Skills-ja/`.
