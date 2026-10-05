# UTAGE's optional "Extra" components

Read this when the user's request sounds like it might already be covered by one of UTAGE's
ready-made optional components, before writing a custom lifecycle-event hook from scratch (see the
main `SKILL.md`'s Workflows). These live under `Assets/Utage/Scripts/ADV/Extra/` and are **not**
attached to the `AdvEngine` hierarchy by default — add the one you need via
`Add Component > Utage > ADV > Extra > ...`, then configure its Inspector fields.

Each component is source-documented (a summary comment above the class) — read the actual file
before recommending it, since this table only summarizes what was true as of `last-verified` in
the main `SKILL.md`.

| Component | What it does |
|---|---|
| `AdvAgingTest` | Aging/soak-test helper: auto-selects choices to let a scenario play through unattended, for automated testing. |
| `AdvBackLogFilter` | Controls whether specific lines are kept in or excluded from the backlog. |
| `AdvCharacterGrayOutController` | Grays out non-speaking characters. Per its own source comment, it must be registered as a listener on `AdvEngine`'s `OnPageTextChange` event (its own same-named method) to take effect — a concrete real example of the Inspector/code event-hook pattern described in the main `SKILL.md`. |
| `AdvGalleryController` | Tracks/controls whether scene recollection (gallery) playback is currently active. |
| `AdvOpenGallery` / `AdvCloseGallery` | Force-unlock or force-relock all CG/scene gallery entries — intended for debugging, per their own source comments. |
| `AdvDisableDuringSaveDataLoad` | Disables the GameObject it's attached to while save data is loading, to avoid UI elements flashing on screen mid-load. |
| `AdvInterruptScenario` | Forcibly interrupts the currently running scenario and jumps to a specified label. **UTAGE's own source comment notes that side effects of this forced interruption are unverified** — treat it as an advanced/risky tool, not a routine one, and say so if a user wants to use it. |
| `AdvLoadScene` | An extension command usable via the `SendMessageByName` mechanism (see `utage-write-scenario`'s bundled reference chapter 06) that loads a Unity scene named in the command's `Arg3`. |
| `AdvSelectionTimeLimit` / `AdvSelectionTimeLimitText` | A countdown timer for timed player choices, plus a paired component for displaying that countdown as text. |
| `AdvTextSound` | Plays a sound effect in sync with text-crawl (letter-by-letter text display). `Type` switches between playing the sound on a time interval or a character-count interval. |
| `AdvVideoLoadPathChanger` | Changes the root path video assets are loaded from. |

If none of these match what the user wants, fall back to the main `SKILL.md`'s lifecycle-event
Workflows (Inspector-wired or code-subscribed) instead of assuming no solution exists — these
"Extra" components are a convenience layer over the same underlying events, not the only way to
achieve a given effect.
