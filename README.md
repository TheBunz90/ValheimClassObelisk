# ValheimClassObelisk - Developer README

## 1. Overview

Class Obelisk is a Valheim mod (BepInEx plugin, built on the [Jotunn](https://valheim-modding.github.io/Jotunn/) modding library) that adds a craftable "Class Obelisk" piece. A player interacts with the Obelisk to choose one of 8 combat classes:

`SwordMaster`, `Archer`, `Crusher`, `Assassin`, `Brawler`, `Wizard`, `Lancer`, `Bulwark`

Each class deals bonus damage with its associated weapon type and gains XP from combat. Classes level from 1 to 50, unlocking a passive perk every 10 levels (10 / 20 / 30 / 40 / 50).

**Plugin identity (BepInEx):**

| Field | Value |
|---|---|
| GUID | `com.bunzboi.valheimweaponclass` |
| Name | ValheimWeaponClasses |
| Version | 1.0.3 |

## 2. Requirements / Dependencies

- .NET Framework 4.7.2
- BepInEx + 0Harmony (referenced from the local Steam Valheim install)
- Jotunn (declared as a hard dependency via `[BepInDependency(Jotunn.Main.ModGuid)]`) — used for prefab cloning, the piece/build system, and all custom GUI (the class-selection window)

The `.csproj` references game/BepInEx/Jotunn DLLs via HintPaths pointing at a local Steam install (`Program Files (x86)\Steam\steamapps\common\Valheim\...`). If you're setting this up on a new machine, update those HintPaths (or your Steam install path) if the build fails to find references.

A `Newtonsoft.Json.dll` is vendored under `Libs/` but is **not** currently used for anything - all save data goes through Unity's own `JsonUtility` instead.

## 3. Project Layout

```
ClassObeliskMod.cs          Plugin entry point (BaseUnityPlugin). Creates the
                             Obelisk piece and contains the entire class-
                             selection GUI (built with Jotunn's GUIManager).

Utilities/
    PlayerClass.cs             The PlayerClass enum + PlayerClassHelper
                                (display names, internal names, weapon-type
                                lookups).
    PlayerClassManager.cs      Per-player class data (PlayerClassData) and
                                the static registry (PlayerClassManager).
                                Also contains the Save/Load Harmony patches.
    ClassCombatManager.cs      Weapon-type detection (is this a sword? a
                                bow? etc.) and class damage multipliers.
    ClassDamageBonusSystem.cs  Generic per-level damage bonus (separate
                                from ClassCombatManager - see section 5).
    XPSystemManager.cs         XP awarding, leveling curve, and the Harmony
                                patches that hook combat events for XP.
    AnimationSpeedManager.cs   Reusable stacking multiplier utility for
                                attack/animation speed (used by some perks).
    SEUtils.cs                 Helpers for inspecting vanilla StatusEffects
                                (poison/burning/frost stacks, timers, etc.)

PerkManagers/                One file per class, each implementing that
                             class's level 10/20/30/40/50 passive perks
                             (SwordMasterPerkManager.cs, ArcherPerkManager.cs,
                             CrusherPerkManager.cs, AssassinPerkManager.cs,
                             BrawlerPerkManager.cs, WizardPerkManager.cs,
                             LancerPerkManager.cs, BulwarkPerkManager.cs).
                             This is where you go to add or tune a specific
                             class's abilities.

TestingResources/            Debug-only console commands (API discovery,
                             bow testing, general debug utilities). Not part
                             of the intended production feature set.

Resources/Icons/             Embedded .rgba icon assets used by some perk
                             status effects.

Libs/                        Vendored third-party DLLs (Newtonsoft.Json,
                             AnimationSpeedManager).
```

## 4. How Class Selection Works

1. The player crafts the Class Obelisk with a Hammer. It's built by cloning the vanilla `piece_workbench` prefab and stripping its `CraftingStation` component, so it's purely an interactable piece, not a crafting station (see `ClassObeliskMod.AddClassObelisk` / `CreateClassObeliskPrefab`).

2. Interacting with it (E) calls `ClassObeliskInteract.Interact`, which opens a class-selection window built entirely with Jotunn's `GUIManager` (no Unity prefab/asset UI) - a grid of class buttons, a description panel populated from the hardcoded `ClassDescriptions` dictionary in `ClassObeliskMod.cs`, and a "Select Class" button.

3. Clicking "Select Class" calls:

   ```csharp
   PlayerClassManager.SetPlayerActiveClass(player, className)
   ```

   which resolves the class and calls `PlayerClassData.SetActiveClass`, **replacing** any currently active class - by default only one class can be active at a time.

> **Note:** `PlayerClassData.CanSelectSecondClass()` returns true once any class reaches level 50, implying a second, simultaneously-active class is intended at that point. The selection GUI currently always calls the single-class "replace" path rather than an "add second class" path, so dual-classing appears to be a planned but not fully wired-up feature.

## 5. Where Class Settings Live

There is no external data file (no JSON/YAML) for class definitions - everything is hardcoded in C#:

- **`Utilities/PlayerClass.cs`** — the `PlayerClass` enum (the 8 classes) plus `PlayerClassHelper`, which maps each class to a display name, an internal/save name, and a weapon-type description.
- **`ClassObeliskMod.cs` (`ClassDescriptions` dictionary)** — flavor-text descriptions of each class's perks, shown in the selection GUI. This is display text only, not mechanics.
- **`Utilities/ClassCombatManager.cs`** — weapon-type detection (`IsSwordWeapon`, `IsBowWeapon`, `IsBluntWeapon`, `IsKnifeWeapon`, `IsSpearWeapon`, `IsUnarmedAttack`, `IsMagicWeapon` - mostly string-matching on weapon names) plus `GetClassDamageMultiplier`. Used both to gate XP eligibility and to apply combat damage bonuses.
- **`Utilities/ClassDamageBonusSystem.cs`** — a generic, class-agnostic damage bonus that scales with level (0.5% per level, capped at 25% at level 50), applied when the equipped weapon matches the active class's weapon type.
  > **Note:** this appears to overlap with the multiplier logic in `ClassCombatManager` - both patch `Character.Damage`. Worth reconciling if you're touching damage math, so bonuses don't double up unexpectedly.
- **`PerkManagers/*.cs`** — the actual mechanical implementation of each class's Lv10/20/30/40/50 perks (buffs, status effects, stamina discounts, stagger, etc). This is the primary place to add new perks or rebalance existing ones.
- **Config** — only one BepInEx config value exists: `EnableMod` (bool, default `true`), bound in `ClassObeliskMod.Awake()`. All other balance numbers (damage bonus per level, XP rates, perk thresholds/durations) are plain C# constants scattered across `ClassDamageBonusSystem.cs`, `XPSystemManager.cs`, and the individual PerkManagers - there's no config file for tuning them outside of code changes (aside from a couple of values adjustable at runtime via the `xprates` debug command, which are not persisted).

## 6. Player Data & Persistence

- **`PlayerClassData`** (`Utilities/PlayerClassManager.cs`) is the per-player state container: class levels, XP per class, currently active class(es), and per-class "unlocked" flags.
  > **Note:** `classUnlocked` currently initializes every class as unlocked ("All unlocked for testing") - unlock-gating isn't actually enforced yet.

- **`PlayerClassManager`** holds a static in-memory dictionary, `Dictionary<long, PlayerClassData>`, keyed by the player's ID. This is populated lazily via `PlayerClassManager.GetPlayerData(player)`.

- Persistence rides on the vanilla character save, not a separate file or the world save:
  - `Player_Save_Patch` (postfix on `Player.Save`) serializes `PlayerClassData` with Unity's `JsonUtility` and appends it to the character's `ZPackage`.
  - `Player_Load_Patch` (postfix on `Player.Load`) reads that trailing data back out (and safely no-ops for older/pre-mod character files).

  Practical effect: class progress travels with the player's character file, and is unaffected by which world/server they join.

> **Multiplayer caveat:** there is no custom RPC/ZDO sync code anywhere in the mod. Class/level/XP state lives only in local memory on whichever machine computes it (client or dedicated server) and is not broadcast between peers. On a dedicated server with multiple simultaneous players this is a likely gap worth testing/hardening before relying on it in real multiplayer sessions.

## 7. Experience & Leveling

Core logic: `Utilities/XPSystemManager.cs` (`ClassXPManager`)

- **Kill bonus XP** — the sole XP source. Damage dealt to a creature by (possibly multiple) classes is tracked (no XP awarded for the damage itself); on the creature's death, a bonus XP pool (creature max health x `KillBonusMultiplier`) is split across the classes that contributed damage.
- These are hooked via Harmony on `Character.Damage` (post, for tracking only) and `Character.OnDeath` (`XPTrackingPatches`).
- **Leveling curve** — `XPRequirements` is a hardcoded cumulative XP table for levels 1-50 (a comment notes it was originally derived from an `XP_To_Level.json` reference file that is no longer present in the repo - the values are now inlined directly in code). `XPCurveHelper` exposes lookups for XP-required-per-level, cumulative XP, current level from XP, and progress (for UI bars).
- Max level is 50. Every 10 levels (10/20/30/40/50) unlocks a passive perk (implemented in the matching `PerkManagers/*.cs` file). Reaching level 50 on any class also unlocks a second active class slot (see section 4).

## 8. Harmony Patches at a Glance

All patches are discovered and applied with a single call in `ClassObeliskMod.Awake()`: `new Harmony(PluginGUID).PatchAll()`.

| File | Targets | Purpose |
|---|---|---|
| `ClassObeliskMod.cs` | `Terminal.InitTerminal` | Registers mod console commands |
| `Utilities/PlayerClassManager.cs` | `Player.Save` / `Player.Load` | Persist/restore `PlayerClassData` |
| | `Terminal.InitTerminal` | `resetclass` debug command |
| `Utilities/XPSystemManager.cs` | `Character.Damage` (post) | Track per-class damage contribution for kill-bonus split |
| | `Character.OnDeath` | Trigger kill-bonus XP split |
| | `Terminal.InitTerminal` | `testxp`/`xprates`/`xpcurve` commands |
| `Utilities/ClassCombatManager.cs` | `Character.Damage`, `Projectile.*`, `Game.Update`, `Terminal.InitTerminal` | Class damage multipliers, weapon-type detection, misc bookkeeping/debug |
| `Utilities/ClassDamageBonusSystem.cs` | `Character.Damage`, `Terminal.InitTerminal` | Generic per-level damage bonus + debug command |
| `Utilities/AnimationSpeedManager.cs` | `CharacterAnimEvent.CustomFixedUpdate`, `Character.Update` | Attack/animation speed multiplier stacking utility used by several perks |
| `PerkManagers/*.cs` | `Character.Damage`, Player/Humanoid combat & equip hooks, `Player.OnSpawned`, `Game.Update` | Each class's Lv10-50 perk effects (see section 5) |
| `TestingResources/*.cs` | `Terminal.InitTerminal` | Debug/API-discovery console commands |

## 9. Debug / Testing Commands

All of these are registered via `Terminal.ConsoleCommand` in Harmony postfixes on `Terminal.InitTerminal`, so they're available in the in-game console (backtick / F5, depending on your Valheim console keybind) any time the mod is loaded - there's no separate "debug build" flag. They exist purely for development and are **not** intended as production features.

### 9.1 Class progress & XP (`Utilities/PlayerClassManager.cs`, `Utilities/XPSystemManager.cs`)

| Command | Usage | What it does |
|---|---|---|
| `resetclass` | `resetclass` | Resets your currently active class's level and XP back to 0. Useful for re-testing leveling/perk unlocks from scratch. |
| `testxp` | `testxp [amount]` | Grants XP (default 100 if omitted) to **every** currently active class without needing to fight anything. Prints old level/XP -> new level/XP and flags a level-up. |
| `xprates` | `xprates` <br> `xprates <kill_multiplier>` | With no args, prints the current `KillBonusMultiplier`. With an arg, overwrites it live for this session (e.g. `xprates 1.5`). **Not persisted** - resets to the coded default on restart. |
| `xpcurve` | `xpcurve <level>` (1-50) | Prints the XP required to reach `<level>`, the cumulative XP total, and the requirement for the next level - handy for sanity-checking `XPRequirements`/`XPCurveHelper` changes without playing. |

### 9.2 Combat & damage bonuses (`Utilities/ClassCombatManager.cs`, `Utilities/ClassDamageBonusSystem.cs`)

| Command | Usage | What it does |
|---|---|---|
| `weapontype` | `weapontype` | Shows how your currently equipped weapon is classified (sword/bow/blunt/knife/spear/unarmed/magic) by `ClassCombatManager`'s name-matching logic. |
| `classlevels` | `classlevels` | Dumps all of your class levels alongside the damage bonus each would apply, plus your currently equipped weapon. |
| `damagerates` | `damagerates` <br> `damagerates <bonus_per_level> [max_bonus]` | Shows or overrides `ClassDamageBonusManager`'s per-level bonus and cap (values are percentages, e.g. `damagerates 1.0 30` sets 1%/level capped at 30%). Not persisted. |
| `simulatedamage` | `simulatedamage <class> <level>` | Prints the damage multiplier and a worked example (100 damage -> X) for a hypothetical class/level, without needing a character at that level. Class names with spaces need quotes, e.g. `simulatedamage "Sword Master" 25`. |
| `testdamage` | `testdamage` | Shows the damage bonus/multiplier currently applying to your equipped weapon. |

> **Heads up:** `testdamage` is registered **twice** - once in `CombatDebugCommands` (`ClassCombatManager.cs`) and once in `DamageBonusCommands` (`ClassDamageBonusSystem.cs`), with slightly different output. Because both patch classes run their `InitTerminal` postfix, only one registration will actually win at runtime (whichever `Terminal.ConsoleCommand` was added last for that name). If you're debugging damage math and the output looks wrong/stale, this collision is likely why - worth renaming one before relying on it.

### 9.3 Bow draw-duration testing (`TestingResources/BowTestingCommands.cs`)

| Command | Usage | What it does |
|---|---|---|
| `inspectdraw` | `inspectdraw` | Logs detailed draw-mechanic reflection info for your currently equipped bow (requires a bow equipped). |
| `testdrawduration` | `testdrawduration <multiplier>` | Multiplies the equipped bow's `m_drawDurationMin` live (e.g. `0.5` = twice as fast, `1.5` = 50% slower). Persists only for the current session/weapon instance. |
| `resetdraw` | `resetdraw` | Resets the draw-duration multiplier back to 1.0 (normal). |
| `drawinfo` | `drawinfo` | Shows the current draw-duration multiplier and whether testing is active. |

There's a near-duplicate, simpler version of the multiplier command - `test_draw_duration <multiplier>` - in `ApiDiscoveryCommands.cs` (section 9.4); the two don't share state, so don't mix them in the same test session.

### 9.4 API / reflection discovery (`TestingResources/ApiDiscoveryCommands.cs`)

These exist to explore Valheim's internals (find the right field/method to Harmony-patch) without decompiling by hand. Run `api_help` in-game for a live summary.

| Command | Usage | What it does |
|---|---|---|
| `list_methods` | `list_methods <className> [filter]` | Lists up to 25 methods on a type, optionally filtered by name substring. Accepts shortcuts: `player`, `skills`, `character`, `humanoid`, `itemdrop`, `piece`, `attack`. Example: `list_methods Character attack`. |
| `list_fields` | `list_fields <className> [filter]` | Same idea as `list_methods` but for fields. Example: `list_fields ItemDrop shared`. |
| `list_skills` | `list_skills` | Attempts to reflect into the local player's skills object and report its type - a starting point for `list_methods`/`list_fields` exploration, not a full skill dump. |
| `inspect_object` | `inspect_object <target>` | Dumps current field values for a known target: `player`, `weapon`, `skills`, or `character`. |
| `find_type` | `find_type <pattern>` | Searches all loaded assemblies for type names containing `<pattern>` (case-insensitive), shows up to 20 matches with full namespace. |
| `test_draw_duration` | `test_draw_duration <multiplier>` | Directly multiplies the equipped bow's draw duration for a quick one-off test (see note in 9.3). |
| `inspect_bow_stats` | `inspect_bow_stats` | Prints the equipped bow's draw duration, speed factor, reload time, and stamina drain. |
| `api_help` | `api_help` | Prints an in-game cheat sheet for this command group. |

### 9.5 General reflection dumps to file (`TestingResources/ValheimDebugUtility.cs`)

Unlike the commands above (which print to the in-game console), these write their results to text files so you can review large dumps outside the game - look under BepInEx's log/debug output path printed by `debugpath`.

| Command | Usage | What it does |
|---|---|---|
| `listmethods` | `listmethods <classname>` | Writes every public/private/instance/static method on a type to `DebugLogs/methods_<classname>.txt`, including Harmony-ready parameter type lists. |
| `findmethod` | `findmethod <classname> <keyword>` | Same as `listmethods` but filtered to methods whose name contains `<keyword>`; writes to `DebugLogs/findmethod_<classname>_<keyword>.txt`. |
| `methodsig` | `methodsig <classname> <methodname>` | Writes every overload's full signature for one method name, plus a ready-to-paste `[HarmonyPatch(...)]` attribute and prefix method stub, to `DebugLogs/methodsig_<classname>_<methodname>.txt`. Very useful when wiring up a new Harmony patch. |
| `debugpath` | `debugpath` | Prints the directory these dump files are written to (and whether it currently exists). |

### 9.6 Misc / sanity checks (`ClassObeliskMod.cs`)

| Command | Usage | What it does |
|---|---|---|
| `obelisktest` | `obelisktest` | Prints a static "mod is working" message - a quick smoke test that the plugin loaded and its Harmony patches applied. |
| `debugpieces` | `debugpieces` | Writes the full list of hammer-buildable pieces to `hammer_pieces.txt` in the BepInEx root, flagging any whose name contains "Obelisk" or "Class" - useful for confirming the custom piece actually registered. |

## 10. Getting Started (Building the Mod)

1. Open `ValheimClassObelisk.sln` in Visual Studio (or your preferred .NET Framework-capable IDE).
2. Confirm the HintPaths in `ValheimClassObelisk.csproj` match your local Steam Valheim install (BepInEx core, Jotunn plugin folder, and the game's managed assemblies). Update them if your install path differs.
3. Ensure Jotunn is installed in your local Valheim `BepInEx/plugins` folder - it's a runtime dependency, not just a compile-time reference.
4. Build (Debug or Release).
5. Copy the resulting `ValheimClassObelisk.dll` (and anything under `Libs/` it needs) into your Valheim `BepInEx/plugins` folder to test in-game.
