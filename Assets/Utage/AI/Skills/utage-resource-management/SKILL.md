---
name: utage-resource-management
description: "Use this skill whenever the user wants to package UTAGE resources into AssetBundles for external/server-hosted delivery, or convert a folder of full character-pose images into UTAGE's 'dicing' format to save texture memory — e.g. 'build asset bundles for utage', 'package my resources for download', 'convert these character images to dicing format', 'reduce texture memory for my sprite poses'. Covers the Resource Converter (AssetBundle build) and Dicing Converter windows. This is an advanced/optional distribution and optimization step, not something every project needs. Do NOT use for writing or rebuilding scenario content (see utage-write-scenario), initial project setup (see utage-setup-and-overview), or C# extension (see utage-extend-with-code). When in doubt whether a request is about AssetBundle packaging or dicing conversion specifically (not general resource setup), use this skill."
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

# Convert and Package UTAGE Resources

Covers two distinct, optional Editor tools UTAGE ships for resource-side work: building
**AssetBundles** for external/server-hosted resource delivery (Resource Converter), and converting
full character-pose image sets into UTAGE's **dicing** format to reduce texture memory (Dicing
Converter). Neither is part of everyday scenario writing — most projects that just play scenario
content directly from `Resources` never need this skill.

## When to use this skill

- The user wants to build AssetBundles of UTAGE resources (e.g. to host scenario/media assets on a
  server instead of shipping them in the app)
- The user wants to convert a folder of full character-pose images into UTAGE's dicing format
- Not for: writing or rebuilding scenario content (`utage-write-scenario`), initial project
  setup (`utage-setup-and-overview`), or C# extension (`utage-extend-with-code`)

## Prerequisites

- A UTAGE project already set up, with a scene containing an `AssetFileManager` component open —
  the Resource Converter requires one and logs `"FileManager is not found in current scene"` and
  aborts if none exists
- For AssetBundle building: know the target platform(s); UTAGE's converter defaults to building
  for the Editor's platform only
- For dicing conversion: the source character-pose images already exist in a folder

**Programmatic check** — confirm the types `Utage.AdvResourcesConverter` and `Utage.DicingConverter`
exist, and that an `Utage.AssetFileManager` component exists in the currently open scene before
attempting an AssetBundle build.

## Quick start (AssetBundle build via Resource Converter)

1. Make sure a UTAGE scene with an `AssetFileManager` (e.g. the scene created by
   `utage-setup-and-overview`) is currently open.
2. Open `Tools > Utage > Resource Converter`.
3. Set either "Resources Directory" (a folder of resources) or "Project Setting" (an
   `AdvScenarioDataProject` asset) as the source — the Convert button stays disabled until one of
   these plus an output path is set.
4. Set the output path (a directory on disk, via the path picker).
5. Review the AssetBundle options (build mode: none / editor-only / all platforms; rename type;
   target platform flags; build options) — defaults to editor-only, Windows target.
6. Click **Convert**.

Expected result: AssetBundles are built to the chosen output path. Errors are logged via
`Debug.LogException`, not silently swallowed — check the Console.

## Workflows

### Workflow: Build AssetBundles for a specific project

**Goal:** Package a UTAGE project's resources as AssetBundles instead of shipping them directly in
the app.

**Steps:** Run Quick start steps 1–6, using "Project Setting" pointed at the project's
`<ProjectName>.project.asset` as the source instead of a raw folder.

**Expected result:** AssetBundles for that project's resources exist at the chosen output path,
with no Console exceptions logged during the build.

### Workflow: Convert character-pose images to dicing format

**Goal:** Reduce texture memory for a character with many pose/expression variations by converting
a folder of full images into UTAGE's dicing format.

**Steps:** Open `Tools > Utage > Dicing Converter`. For each character/image set, assign the input
folder and two output folders (one for the generated dicing data, one for the resulting textures).

**Expected result:** Dicing data and texture assets are generated in the specified output folders,
replacing the need to keep every full pose image as a separate uncompressed texture.

## Verification

- After an AssetBundle build: the output path contains the built bundles, and the Console shows no
  logged exceptions from the Convert step
- After a dicing conversion: both output folders contain generated assets, and no errors appear in
  the Console

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `Tools > Utage > Resource Converter` | Editor menu item | Builds AssetBundles from a resource folder or an `AdvScenarioDataProject`; requires an `AssetFileManager` in the open scene |
| `Tools > Utage > Dicing Converter` | Editor menu item | Converts a folder of full character-pose images into UTAGE's dicing data + texture format |

## Common issues

- **"FileManager is not found in current scene" logged, nothing happens** — Cause: no
  `AssetFileManager` component exists in the currently open scene. Fix: open a UTAGE scene that
  has one (e.g. the scene from `utage-setup-and-overview`'s New Project wizard).
- **Convert button stays disabled** — Cause: neither Resources Directory nor Project Setting is
  assigned, or the output path is empty. Fix: set one source and a non-empty output path.
- **Exception logged during Convert** — The tool catches and logs (`Debug.LogException`) rather
  than failing silently; report the exact exception to the user rather than retrying blindly.

## Boundaries

- This skill is about AssetBundle packaging and dicing conversion specifically — it is not the
  general "how do I add character images/sounds to my project" workflow (that's just placing files
  under the setting sheets described in `utage-write-scenario`'s reference, chapter 07).
- Does not cover scenario content or rebuilding scenario data — see `utage-write-scenario`.
- Japanese-speaking users may prefer the Japanese version of this skill set under
  `Assets/Utage/AI/Skills-ja/`.
