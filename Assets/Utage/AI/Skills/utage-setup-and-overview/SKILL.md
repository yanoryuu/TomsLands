---
name: utage-setup-and-overview
description: "Use this skill whenever the user wants to set up, install, verify, or get oriented with UTAGE (Utage) in a Unity project — e.g. 'set up utage', 'create a new utage project', 'how do I start a visual novel with utage', 'add a visual novel scene to my game', 'is utage installed correctly', 'where are the utage scenario files'. Covers first-time project setup via the New Project window (creating a new ADV scene, adding UTAGE to an existing scene, or creating a scenario-only project), verifying the asset is installed, and locating key files (scenario data, scripts, samples, docs). Do NOT use for writing, rebuilding, or debugging scenario content (see utage-write-scenario), resource conversion (see utage-resource-management), or code-level extension via custom commands (see utage-extend-with-code). When in doubt whether a Unity visual-novel/ADV request could involve UTAGE, use this skill — Prerequisites shows how to confirm the asset is installed."
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

# Set Up and Get Oriented with UTAGE

Helps an agent verify that UTAGE is installed in the current Unity project, scaffold a new
visual-novel (ADV) project or scene through UTAGE's own New Project wizard, and find the
key files and documentation a user will need next.

## When to use this skill

- The user asks to "set up", "install", "add", or "start" UTAGE / a visual novel / an ADV scene
- The user asks whether UTAGE is installed correctly, or where its files/docs live
- The user wants a brand-new UTAGE project, or wants UTAGE added to an existing scene
- Not for: writing, rebuilding, or debugging scenario content (see `utage-write-scenario`),
  converting image/audio resources (see `utage-resource-management`), or extending UTAGE with C#
  code (see `utage-extend-with-code`)

## Prerequisites

- Unity 6000.0.58f2 or later
- Built-in Render Pipeline or Universal Render Pipeline (URP) — HDRP is not confirmed supported
- UTAGE (version 4.2.9 as of this writing) imported under `Assets/Utage/`

**Programmatic install check** — confirm either of the following before proceeding:
- The type `Utage.AdvEngine` exists in the project (defined in `Assets/Utage/Scripts/ADV/AdvEngine.cs`)
- The menu item `Tools > Utage > New Project` is present in the Unity Editor

If the check fails, UTAGE is not imported (or only partially imported). Tell the user to import
the package from the Asset Store page:
https://assetstore.unity.com/packages/tools/game-toolkits/utage4-for-unity-text-adventure-game-engine-version4-266447

## Quick start

The fastest way to get a working UTAGE scene is UTAGE's own New Project wizard — do not hand-build
a scene from scratch.

1. In the Unity Editor, open `Tools > Utage > New Project`.
2. In "Input New Project Name", type a project name (must be non-empty, and
   `Assets/<name>/` must not already exist, or the Create button stays disabled).
3. In "Select Create Type", choose **Create New Adv Scene** (see Workflows below for the other
   two types).
4. Leave "Template Settings" at its default unless the user asked for a specific template
   (default is "New Scene Default Settings ... New Scene Default TMP").
5. Under "Create Settings" (fields shown depend on the selected Template Settings — a
   TMP/URP-based template shows all of the below):
   - **Secret Key** — defaults to the literal placeholder text `InputOriginalKey`. This is used
     to encrypt save data and scenario files (`FileIOManager.SetCryptKey`). **Replace it with a
     real, project-specific key** — leaving the placeholder means the "encryption" key is public
     knowledge. Must be non-empty or Create stays disabled.
   - **Game Screen Width / Game Screen Height** — default `1280` / `720`. Sets the game's base
     resolution (applied to `LetterBoxCamera` and `ScreenResolution`). Must both be > 0.
   - **Auto Clear Urp Volumes** — checkbox, default checked, only shown when the project uses
     URP. When checked, clears the Volumes already set on the default
     `UniversalRenderPipelineAsset` at project-creation time.
   - **Font Language** — dropdown, only shown for TextMeshPro-based templates. Defaults to the
     Editor's system language (e.g. "Japanese"). Must be non-empty; pick the language the
     scenario text will be written in so the correct TMP font is applied.
6. Click **Create**.

Expected result: a new folder `Assets/<ProjectName>/` is created, containing:
- `<ProjectName>.unity` — the new scene, already open in the Editor, with a root `AdvEngine`
  GameObject (and its `UI/MessageWindowManager/...` children), a `FileIOManager` (using the
  Secret Key entered above), and `LetterBoxCamera`/`ScreenResolution` objects sized to the Game
  Screen Width/Height entered above
- `Scenarios/<ProjectName>.xls` — the scenario data template (Excel), plus its imported
  `<ProjectName>.book.asset` and `<ProjectName>.scenarios.asset`
- `Fonts & Materials/` — TextMeshPro font assets generated per supported language (NotoSans,
  NotoSansJP, NotoSansKR, NotoSansSC, NotoSansTC) plus matching materials
- `Audio/<ProjectName> AudioMixer.mixer` and a `Resources/<ProjectName>/` folder

No errors should appear in the Console (only informational `RebuildAssets...` / `...End
RebuildAssets` log lines are expected while it runs).

## Workflows

### Workflow: Create a New ADV Scene Project

**Goal:** Start a brand-new scene dedicated to a visual novel built with UTAGE.

**Steps:** Same as Quick start above, with Create Type set to **Create New Adv Scene**.

**Expected result:** A new scene file under `Assets/<ProjectName>/` containing an initialized
UTAGE engine, plus copied template scenario-data assets, with no Console errors.

### Workflow: Add UTAGE to an Existing Scene

**Goal:** Bring UTAGE into a scene the user already has (e.g. an existing gameplay scene), instead
of creating a dedicated new scene.

**Steps:** Open the current scene first, then run the Quick start steps but set Create Type to
**Add To Current Scene** in step 3.

**Expected result:** UTAGE's engine and template scenario-data assets are added into the project
and wired into the currently open scene, rather than into a new scene file.

### Workflow: Create a Scenario-Only Project

**Goal:** Prepare only the scenario data assets (Excel/CSV-based) without creating or touching any
scene — useful when a scene will be set up separately, or scenario writing needs to start before
scene work.

**Steps:** Run the Quick start steps but set Create Type to **Create Scenario Asset Only** in
step 3.

**Expected result:** A new folder `Assets/<ProjectName>/` with copied template scenario-data
assets, and no scene is created or modified.

## Verification

- `Assets/<ProjectName>/` exists and contains a copy of UTAGE's template assets
- For "Create New Adv Scene": a new `.unity` scene file exists under that folder, and it is the
  currently open scene, with a root `AdvEngine` GameObject present in its hierarchy
- `Scenarios/<ProjectName>.xls` exists (the scenario data template)
- The Unity Console shows no errors after the New Project window's Create button was clicked
  (a "Failed save scene" error indicates the scene could not be saved and should be reported to
  the user)
- The Secret Key entered was not left as the literal default `InputOriginalKey` (see Common
  issues)

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools > Utage > New Project` | Editor menu item | Opens the wizard that scaffolds a new ADV scene, adds UTAGE to the current scene, or creates a scenario-only project |
| `Utage.AdvEngine` | Class (`Assets/Utage/Scripts/ADV/AdvEngine.cs`) | The runtime engine that drives ADV scenario playback; its presence in a scene/project is the install/init check used above |
| `IAdvProjectCreatorSecurity.SecretKey` | Interface property | The Create Settings "Secret Key" field; the value set here becomes the encryption key for save data and scenario files |
| `IAdvProjectCreatorGameScreenSize.GameScreenWidth/Height` | Interface property | The Create Settings "Game Screen Width/Height" fields; applied to `LetterBoxCamera` and `ScreenResolution` on project creation |

## Common issues

- **Create button stays disabled** — Cause: the project name field is empty, a folder already
  exists at `Assets/<name>/`, Secret Key is empty, Game Screen Width/Height is 0 or less, or (for
  TMP templates) Font Language is empty. Fix: fill in all Create Settings fields; choose a
  non-empty, not-yet-used project name.
- **"TemplateSettings is invalid" / "Failed Create settings" logged to Console** — Cause: the
  selected Template Settings asset is missing or misconfigured. Fix: leave Template Settings at
  its default, or ask the user which custom template they intended.
- **"Failed save scene" logged to Console** — Cause: the new scene could not be saved to disk
  (e.g. permissions, disk space). Fix: report the exact Console error to the user; this is not
  something to silently retry.
- **Secret Key left as the literal default `InputOriginalKey`** — Not a Console error, but a real
  pitfall: the field defaults to that literal placeholder string, not a random key. If the user
  ships a project with it unchanged, their save/scenario encryption key is effectively public.
  Always prompt the user to set their own key before finishing setup.

## Skill index

- `utage-setup-and-overview` (this skill) — install check, New Project wizard, key file locations
- `utage-write-scenario` — writing/editing scenario dialogue, choices, and commands, rebuilding
  edited scenario data, reading import errors, and inspecting playback state
- `utage-resource-management` — AssetBundle packaging and dicing conversion (advanced/optional)
- `utage-extend-with-code` — custom scenario commands and engine lifecycle hooks in C#

## Boundaries

- This skill only covers getting a project started via the official New Project wizard. It does
  not teach scenario script syntax, resource conversion, or C# extension — see the skill index
  above.
- If, after importing UTAGE, the project shows errors related to the render pipeline (e.g. URP) or
  a Unity version mismatch, UTAGE has its own internal compatibility mechanism —
  `Tools > Utage > Extension Package Manager` — that imports additional assets needed for the
  current pipeline/Unity version. This is a troubleshooting fallback, not a general package
  manager for third-party add-ons.
- Japanese-speaking users may prefer the Japanese version of this skill set under
  `Assets/Utage/AI/Skills-ja/`, which mirrors the same structure with Japanese-language content.
- HDRP support is not confirmed; do not assume UTAGE works correctly under HDRP without asking
  the user to verify.
