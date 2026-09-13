# Planned redesign: implicit classes (weapon-driven) instead of explicit selection

> **Status: on hold.** Saved for future reference — not currently being implemented. See open questions at the bottom before picking this back up.

## Context

Classes currently require explicit selection via the Class Obelisk (`PlayerClassData.activeClasses`, max 1-2 slots), which required a custom RPC sync mechanism to keep consistent across peers (broadcast-on-change, roster-resync-on-connect). Even working correctly, it means a player must reselect after certain reconnect scenarios.

The proposed new design removes selection entirely: **a class's perks and XP apply automatically whenever you're using the matching weapon/gear**, determined fresh on every hit. This is a better model for the stated goal ("use a sword, get Sword Master") and, as a side effect, would make the whole cross-peer "active class" sync problem moot for combat/XP purposes — there's no persistent selection state left to sync, since eligibility would be derived per-hit from `HitData.m_skill` (already proven reliable cross-peer during testing) plus each player's own class level (which already loads correctly from their own save).

Exploration confirmed this is feasible and reasonably well-scoped:
- `activeClasses` and the vestigial `classUnlocked` (always `true`, per its own "for testing" comment, gates nothing today) are independent, separately-serialized fields — removing them doesn't disturb `classLevels`/`classXP`, and `JsonUtility` silently ignores the leftover keys in old save files. No migration needed.
- In 6 of 8 perk managers (Sword Master, Archer, Crusher, Assassin, Lancer, and Bulwark's shield/block check), every `IsClassActive(...)` check already sits redundantly alongside a weapon/gear-type check in the same method — dropping the active-class half changes nothing else.
- Two exceptions needed explicit decisions (resolved below): Brawler's damage perks currently apply with any weapon (no unarmed check at all), and two standing passives (Wizard's Eitr Weave, Crusher's Colossus) aren't tied to an attack at all.
- A duplicate damage-bonus system was found (`ClassCombatManager` and `ClassDamageBonusSystem` both independently patch `Character.Damage` and apply a class multiplier — likely double-applying bonuses today); consolidating them was decided as part of this pass since both need their gating rewritten anyway.

**Decisions made when this was last discussed:**
1. Obelisk becomes a perks/status viewer (per-class level + description), no "Select Class" button.
2. Brawler's damage perks now require actually fighting unarmed.
3. Eitr Weave and Colossus stay always-on once the required level is reached (no weapon/gear check) — i.e. they become permanent unlocks.
4. The two damage-bonus systems get consolidated into one (`ClassCombatManager`), fixing the likely double-bonus bug as a byproduct.

**Bulwark's XP note (assumption, needs re-confirming):** Bulwark has no weapon of its own — its own code already says "gains XP from any combat." Today that only worked if a player deliberately selected Bulwark as a 2nd class alongside a weapon class. Under the new model there's no selection to gate it, so Bulwark would simply **always** earn XP passively alongside whatever weapon-matched class also earns XP on a hit — consistent with its existing "any combat" design intent, just automatic now instead of requiring a slot. **This raised more questions on review (see below) — revisit before implementing.**

**Explicitly out of scope:** two pre-existing bugs found during exploration where a couple of perk prefixes substitute `Player.m_localPlayer` for the actual attacker/target (`AssassinPerkManager.cs`, `CrusherPerkManager.cs`'s `Humanoid_StartAttack_Prefix`). The Assassin one sits directly on the `IsClassActive` line that would be deleted, so it'd go away as a natural byproduct — not a deliberate fix. The Crusher one is unrelated to this refactor and would be left alone. Also out of scope: whether per-level damage-bonus scaling has its own cross-peer visibility gaps today (separate from what was already fixed for XP/weapon-type) — this refactor wouldn't change where those checks execute, so it'd be neither better nor worse than before; worth a separate investigation later if teammates' damage bonuses look off.

## Approach (as last designed)

### 1. Per-class eligibility helper (`Utilities/ClassCombatManager.cs`)

Each perk manager already has its own `HasXxxPerk(player, level)` helper used throughout that file — this is the single choke point. Change each to check **only** `GetClassLevel(class) >= level`, dropping its internal `IsClassActive` check. Since the surrounding damage prefixes already independently verify weapon/gear type (`IsSwordWeapon`, `IsBowWeapon`, shield+blocking for Bulwark, etc.), this alone correctly makes those classes' perks weapon-implicit with no other change — and, as a side effect, makes Eitr Weave and Colossus "always on once leveled" automatically, since neither has a weapon check today (matching decision #3).

A few call sites check `IsClassActive` directly instead of through the helper (e.g. `ArcherPerkManager.cs` for its projectile-hit trigger, `LancerPerkManager.cs`'s Spear of Relocation, `AssassinPerkManager.cs`'s top-of-prefix gate). Remove these directly — most are already redundant with an adjacent skill/weapon check (e.g. Lancer's already verifies `hit.m_skill == Spears`); where one isn't, replace it with the class's own `HasXxxPerk` level check.

**Brawler exception (decision #2):** its damage prefix (`BrawlerPerkManager.cs`, One-Two-Combo/Iron Fist/Rage bonuses) currently has no weapon check at all. Add `ClassCombatManager.IsUnarmedAttack(weapon)` alongside the level check here, matching the pattern every other class already uses.

Add one new small helper, `ClassCombatManager.GetClassForSkill(Skills.SkillType skill)`, returning the single non-Bulwark class name whose skill-set matches (reusing the same skill groupings already in `IsSkillAppropriateForClass`), or `null` for no match. This is what the XP system would use instead of iterating a selection list (below).

### 2. Consolidate the two damage-bonus systems (decision #4)

Merge `ClassDamageBonusSystem.cs`'s `ClassDamageBonusManager.CalculateDamageBonus` / `DamageBonusPatches.Character_Damage_Prefix` into `ClassCombatManager.cs`'s existing `GetClassDamageMultiplier` / `CombatPatches.Character_Damage_Prefix`, then remove `ClassDamageBonusSystem.cs`'s patch entirely so only one prefix computes and applies the multiplier.

Update `GetClassDamageMultiplier` itself: instead of `if (activeClasses.Count == 0) return 1f;` + looping `activeClasses`, use the player's currently-equipped weapon to find the one matching class (reusing the existing per-class `IsXWeapon` checks already used inside the per-class bonus functions) and apply just that class's bonus at its current level. Bulwark contributes no attack-damage bonus either way (`GetBulwarkDamageBonus` already always returns `1f`), so it's unaffected.

### 3. XP system (`Utilities/XPSystemManager.cs`)

In `Character_RPC_Damage_Postfix`: remove the `activeClasses.Count == 0` check and the `foreach (activeClass in playerData.activeClasses)` loop. Replace with: look up the one class matching `hit.m_skill` via the new `GetClassForSkill` helper (if any), and always additionally include `"Bulwark"` (per the Bulwark note above) — track damage under whichever of those 1-2 classes apply. No `activeClasses`/selection lookup needed at all.

In `AwardKillBonusXP`: remove the `playerData.activeClasses.Contains(className)` filter — every class already recorded in the per-creature damage tracker for a contributor was already validated as eligible at tracking time, so nothing further to check.

### 4. Remove the selection/sync plumbing entirely

This whole layer becomes dead once there's no "active class" concept:
- `Utilities/PlayerClass.cs` (`PlayerClassData`): remove `activeClasses`, `classUnlocked` (+ its serialization shim list), `IsClassActive`, `CanActivateClass`, `SetActiveClass`, `AddActiveClass`, `RemoveActiveClass`, `GetMaxActiveClasses`, `CanSelectSecondClass`, `GetActiveClassEnums`. Keep `classLevels`/`classXP` and their shim lists untouched.
- `Utilities/PlayerClassManager.cs`: remove `SetPlayerActiveClass` (both overloads), `HasActiveClasses`, `GetActiveClassesString`, `SetActiveClassesDirectly`, `GetAllPlayerData`, `BroadcastFullRoster`, the `ClassSyncRpc` Harmony patch class (the `"CO_SetActiveClasses"` RPC registration/handler), and the `BroadcastFullRoster()` call inside `Player_Load_Patch`.
- Update `BuildActiveClassesCompendiumText` (still needed — it's what the Compendium's "Active Classes" entry renders, and it currently reads `activeClasses`, so leaving it untouched would silently break to "No active class selected" for everyone): swap its iteration to `PlayerClassHelper.GetAllClasses()` (all 8, always), drop the empty-selection early return and the "second slot available" tail. Consider renaming the Compendium entry from "Active Classes" to something like "Class Levels" (`UI/ActiveClassesTextsDialogPatch.cs` just calls this function, so no logic change needed there beyond maybe the entry title).

### 5. Class Obelisk UI (`ClassObeliskMod.cs`, decision #1)

Remove the selection half: `CreateSelectClassButton`, `OnClassSelected`, `UpdateSelectButton`, and the "Active" label branch in `OnClassButtonClicked` (`IsClassActive` check). Keep `CreateWoodenGUIPanel`, `CreateTwoColumnClassButtons`, and `CreateDescriptionWindow` as the browsing UI (click a class, see its level + perk descriptions with locked perks masked, same as today). Replace the "Current Active Classes: ..." header text with something like "Use the matching weapon or gear to earn each class's XP and perks."

## Files that would be touched

- `Utilities/ClassCombatManager.cs` — add `GetClassForSkill`, absorb the consolidated damage-bonus calculation.
- `Utilities/ClassDamageBonusSystem.cs` — removed (or gutted if anything unrelated lives there — verify before deleting the whole file).
- `Utilities/XPSystemManager.cs` — rewrite eligibility in `Character_RPC_Damage_Postfix` and `AwardKillBonusXP` as described.
- `Utilities/PlayerClass.cs`, `Utilities/PlayerClassManager.cs` — remove the selection/sync layer, update the Compendium text builder.
- `ClassObeliskMod.cs` — remove selection UI, keep browsing UI.
- All 8 `PerkManagers/*.cs` — drop `IsClassActive` from each `HasXxxPerk` helper and any direct call sites; add the unarmed check to Brawler's damage prefix.
- `README.md` — update the class-selection description to match (lower priority, do last).

## Verification (planned)

1. Build and deploy locally first (solo world, as usual).
2. Equip a sword and damage/kill something — confirm Sword Master XP and level-appropriate perks apply with **no Obelisk interaction at all**. Repeat for at least one more weapon-based class (e.g. bow → Archer).
3. Fight unarmed — confirm Brawler perks now apply, and confirm they do **not** apply while a weapon is equipped (the tightened behavior from decision #2).
4. Equip a shield and block a hit — confirm Bulwark's perks still trigger, and confirm Bulwark XP accrues passively alongside whatever weapon class you're also using.
5. Level Wizard/Crusher enough to reach Eitr Weave/Colossus, then switch to an unrelated weapon — confirm those two stay active regardless of current weapon (decision #3).
6. Open the Class Obelisk — confirm it shows all 8 classes' levels/descriptions with no "Select Class" button, and open the Compendium's class-levels entry to confirm it also lists all 8 unconditionally.
7. Multiplayer retest of the exact scenario that drove this redesign: have a second player join **without** ever touching an Obelisk, fight alongside them, and confirm their weapon-matched class XP/perks apply correctly on your client immediately — no reselection, no waiting on any sync.
8. Load an existing pre-redesign character save and confirm it loads without error (old `activeClasses`/`classUnlocked` JSON keys are silently ignored, `classLevels`/`classXP` carry over intact).

## Open questions to resolve before picking this back up

These surfaced on final review and are why this was put on hold — revisit and re-decide before implementing:

- The Bulwark "always earns XP from any combat, automatically, with no cap" behavior may need more thought now that there's no selection slot limiting it — is passive free XP on every single hit (regardless of weapon) actually the desired balance, or should Bulwark require something active (e.g. only while a shield is equipped, even if not currently blocking)?
- Whether "every class always available" changes the intended pacing/progression of the mod (currently gated by choosing 1-2 classes to invest in) — with implicit classes, a player could plausibly level all 8 simultaneously just by switching gear, which may or may not be the intended long-term balance.
- Confirm the Eitr Weave / Colossus "permanent unlock" decision still feels right once you think about it alongside the Bulwark question above — both are cases of "a perk that doesn't care what you're currently holding."
