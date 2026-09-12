using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using static ClassXPManager;

// XP System Manager for tracking damage and awarding XP
public static class ClassXPManager
{
    // Track damage dealt to creatures by players per class type
    // Structure: creatureDamageTracker[creature][playerID][className] = damageAmount
    private static Dictionary<Character, Dictionary<long, Dictionary<string, float>>> creatureDamageTracker =
        new Dictionary<Character, Dictionary<long, Dictionary<string, float>>>();

    // Snapshot of each creature's max health, taken while it's still alive.
    // Character.GetMaxHealth() reads the star-scaled value from the ZDO, but vanilla
    // Character.OnDeath() destroys the ZDO before our OnDeath postfix runs, so reading
    // it there falls back to the unscaled base health. Snapshot it on first tracked hit instead.
    private static Dictionary<Character, float> creatureMaxHealthTracker =
        new Dictionary<Character, float>();

    // Configuration for XP rates
    public static float KillBonusMultiplier = 1f; // Kill bonus = creature max health * this multiplier

    // NOTE: No Newtonsoft, no IO, no Reflection needed.
    // Keep the same public API so XPCurveHelper and the rest of your code keep working.

    internal static class XPRequirements
    {
        // This is the canonical cumulative "total XP required to reach level N" table.
        // Values copied 1:1 from XP_To_Level.json (levels 1..50).
        // If you expand later, just extend this table.
        private static readonly SortedDictionary<int, int> _thresholds =
            new SortedDictionary<int, int>
            {
                {  1,      180 },
                {  2,      409 },
                {  3,      695 },
                {  4,     1044 },
                {  5,     1468 },
                {  6,     1975 },
                {  7,     2579 },
                {  8,     3292 },
                {  9,     4129 },
                { 10,     5106 },
                { 11,     6243 },
                { 12,     7559 },
                { 13,     9078 },
                { 14,    10825 },
                { 15,    12830 },
                { 16,    15125 },
                { 17,    17745 },
                { 18,    20731 },
                { 19,    24128 },
                { 20,    27984 },
                { 21,    32355 },
                { 22,    37303 },
                { 23,    42897 },
                { 24,    49211 },
                { 25,    56331 },
                { 26,    64350 },
                { 27,    73374 },
                { 28,    83516 },
                { 29,    94907 },
                { 30,   107688 },
                { 31,   122017 },
                { 32,   138069 },
                { 33,   156039 },
                { 34,   176142 },
                { 35,   198616 },
                { 36,   223726 },
                { 37,   251762 },
                { 38,   283050 },
                { 39,   317946 },
                { 40,   356848 },
                { 41,   400193 },
                { 42,   448466 },
                { 43,   502203 },
                { 44,   561997 },
                { 45,   628502 },
                { 46,   702443 },
                { 47,   784619 },
                { 48,   875913 },
                { 49,   977301 },
                { 50,  1089861 },
            };

        /// <summary>
        /// Total XP required to *reach* a level (cumulative threshold).
        /// Level <= 0 returns 0. Levels above the table clamp to the table's max.
        /// </summary>
        public static int GetTotalXPForLevel(int level)
        {
            if (level <= 0) return 0;

            int maxKnownLevel = _thresholds.Keys.Max();
            if (level >= maxKnownLevel) return _thresholds[maxKnownLevel];

            return _thresholds.TryGetValue(level, out int total) ? total : 0;
        }

        /// <summary>
        /// XP required *within* a given level (delta from previous level's total).
        /// Example: L10 delta = total(10) - total(9) = 978 - 837 = 141.
        /// </summary>
        public static int GetDeltaXPForLevel(int level)
        {
            if (level <= 0) return 0;

            int prevTotal = (level <= 1) ? 0 : GetTotalXPForLevel(level - 1);
            int thisTotal = GetTotalXPForLevel(level);
            return Math.Max(0, thisTotal - prevTotal);
        }

        /// <summary>
        /// Highest level whose total requirement is <= totalXP.
        /// </summary>
        public static int GetLevelFromTotalXP(float totalXP)
        {
            int current = 0;
            foreach (var kv in _thresholds)
            {
                if (totalXP >= kv.Value) current = kv.Key; else break;
            }
            return current;
        }

        /// <summary>
        /// Progress within the current level for UI bars:
        /// returns (currentProgress, requiredForLevelUp).
        /// </summary>
        public static (float current, float required) GetProgress(float totalXP, int currentLevel)
        {
            int maxLevel = _thresholds.Keys.Max();
            int nextLevel = Math.Min(currentLevel + 1, maxLevel);

            int xpForCurrent = (currentLevel <= 0) ? 0 : GetTotalXPForLevel(currentLevel);
            int xpForNext = GetTotalXPForLevel(nextLevel);

            float current = Math.Max(0f, totalXP - xpForCurrent);
            float required = Math.Max(1f, xpForNext - xpForCurrent); // avoid divide-by-zero in UIs
            return (current, required);
        }

        /// <summary>Maximum level defined by the table.</summary>
        public static int GetMaxLevel() => _thresholds.Keys.Max();
    }


    // Check if target is a valid creature for XP
    private static bool IsValidCreature(Character target)
    {
        if (target == null) return false;

        // Must be a living creature (not a player)
        if (target is Player) return false;

        // Must have a MonsterAI component (indicates it's a creature)
        if (target.GetComponent<MonsterAI>() == null &&
            target.GetComponent<AnimalAI>() == null) return false;

        // Additional checks to exclude things like training dummies
        if (target.name.ToLower().Contains("dummy") ||
            target.name.ToLower().Contains("target")) return false;

        return true;
    }

    // Track damage dealt to a creature by class type
    public static void TrackDamageToCreature(Character creature, Player attacker, float damage, string className)
    {
        if (creature == null || attacker == null || damage <= 0 || string.IsNullOrEmpty(className)) return;

        long attackerID = attacker.GetPlayerID();

        // Initialize nested dictionaries if needed
        if (!creatureMaxHealthTracker.ContainsKey(creature))
        {
            creatureMaxHealthTracker[creature] = creature.GetMaxHealth();
        }

        if (!creatureDamageTracker.ContainsKey(creature))
        {
            creatureDamageTracker[creature] = new Dictionary<long, Dictionary<string, float>>();
        }

        if (!creatureDamageTracker[creature].ContainsKey(attackerID))
        {
            creatureDamageTracker[creature][attackerID] = new Dictionary<string, float>();
        }

        if (!creatureDamageTracker[creature][attackerID].ContainsKey(className))
        {
            creatureDamageTracker[creature][attackerID][className] = 0f;
        }

        creatureDamageTracker[creature][attackerID][className] += damage;

        //Debug.Log($"Tracked {damage:F1} damage to {creature.name} by {attacker.GetPlayerName()} using {className} (total: {creatureDamageTracker[creature][attackerID][className]:F1})");
    }

    // Award kill bonus XP when a creature dies
    public static void AwardKillBonusXP(Character deadCreature)
    {
        if (deadCreature == null || !creatureDamageTracker.ContainsKey(deadCreature))
        {
            DevLog.Log($"[XPDBG] AwardKillBonusXP: no tracked damage entry for {deadCreature?.name} (tracked creatures: {creatureDamageTracker.Count})");
            return;
        }

        var playerDamageByClass = creatureDamageTracker[deadCreature];
        if (playerDamageByClass.Count == 0)
        {
            DevLog.Log($"[XPDBG] AwardKillBonusXP: tracker entry for {deadCreature.name} has zero contributors");
            return;
        }

        // Calculate kill bonus based on creature's max health, using the snapshot taken
        // while it was still alive (see creatureMaxHealthTracker) so star-scaled health
        // is captured correctly. Falls back to GetMaxHealth() in case no hit was tracked.
        float maxHealth = creatureMaxHealthTracker.TryGetValue(deadCreature, out float trackedMaxHealth)
            ? trackedMaxHealth
            : deadCreature.GetMaxHealth();
        float baseKillBonus = maxHealth * KillBonusMultiplier;

        DevLog.Log($"Creature {deadCreature.name} died. Max health: {maxHealth}, base kill bonus: {baseKillBonus}");

        // Award XP to each player based on classes they used
        foreach (var playerEntry in playerDamageByClass)
        {
            long playerID = playerEntry.Key;
            var classDamageMap = playerEntry.Value;

            // Find the player (they might have disconnected)
            Player contributor = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == playerID);
            if (contributor == null)
            {
                DevLog.Log($"[XPDBG] AwardKillBonusXP: no connected Player found for tracked playerID {playerID}");
                continue;
            }

            var playerData = PlayerClassManager.GetPlayerData(contributor);
            if (playerData == null) continue;

            DevLog.Log($"[XPDBG] AwardKillBonusXP: contributor={contributor.GetPlayerName()}, trackedClasses={string.Join(",", classDamageMap.Keys)}, activeClasses={string.Join(",", playerData.activeClasses)}");

            // Find which classes this player used AND are currently active
            var eligibleClasses = new List<string>();
            foreach (var classDamageEntry in classDamageMap)
            {
                string className = classDamageEntry.Key;
                float damageDealt = classDamageEntry.Value;

                // Only award to classes that are currently active and dealt damage
                if (playerData.activeClasses.Contains(className) && damageDealt > 0)
                {
                    eligibleClasses.Add(className);
                }
            }

            if (eligibleClasses.Count == 0) continue;

            // Award the full mob-health-based kill bonus to each eligible class (no split)
            float bonusPerClass = baseKillBonus;

            DevLog.Log($"Player {contributor.GetPlayerName()} eligible for kill bonus with {eligibleClasses.Count} classes: {string.Join(", ", eligibleClasses)}");

            // Award kill bonus to each eligible class
            foreach (string className in eligibleClasses)
            {
                int oldLevel = playerData.GetClassLevel(className);
                playerData.AddClassXP(className, bonusPerClass);
                int newLevel = playerData.GetClassLevel(className);

                // Show XP gain message
                contributor.Message(MessageHud.MessageType.TopLeft, $"{className}: +{bonusPerClass:F0} XP");

                // Show level up message
                if (newLevel > oldLevel)
                {
                    contributor.Message(MessageHud.MessageType.Center, $"{className} Level Up! Level {newLevel}");

                    if (newLevel % 10 == 0)
                    {
                        contributor.Message(MessageHud.MessageType.Center, $"New {className} Perk Unlocked!");
                    }
                }

                DevLog.Log($"Awarded {bonusPerClass:F1} kill bonus XP to {className} for {contributor.GetPlayerName()}");
            }
        }

        // Clean up tracking for this creature
        creatureDamageTracker.Remove(deadCreature);
        creatureMaxHealthTracker.Remove(deadCreature);
    }

    // Clean up tracking for disconnected players or old creatures
    public static void CleanupTracking()
    {
        var deadCreatures = creatureDamageTracker.Keys.Where(c => c == null || c.IsDead()).ToList();
        foreach (var deadCreature in deadCreatures)
        {
            creatureDamageTracker.Remove(deadCreature);
        }
    }
}

// XPSystemManager.cs (snippet) — replace XPCurveHelper internals
public static class XPCurveHelper
{
    // Previously: level^2 * 50. Now: read from embedded XP table.
    public static float GetXPRequiredForLevel(int level)
    {
        // We interpret "required for level" as the *delta* inside the level
        // = total(level) - total(level-1)
        return XPRequirements.GetDeltaXPForLevel(level);
    }

    // Now returns the *cumulative* threshold to reach a level, from the JSON table
    public static float GetTotalXPForLevel(int level)
    {
        return XPRequirements.GetTotalXPForLevel(level);
    }

    public static int GetLevelFromXP(float totalXP)
    {
        // Highest level with total <= totalXP
        int lvl = XPRequirements.GetLevelFromTotalXP(totalXP);

        // Clamp to the table’s max, just like your old behavior capped at 50
        int max = XPRequirements.GetMaxLevel();
        return Math.Min(lvl, max);
    }

    public static (float current, float required) GetXPProgress(float totalXP, int currentLevel)
    {
        return XPRequirements.GetProgress(totalXP, currentLevel);
    }
}


// Enhanced XP tracking patches
[HarmonyPatch]
public static class XPTrackingPatches
{
    // Patch damage dealing to award XP and track damage. Patched on RPC_Damage rather than
    // the public Damage(HitData) wrapper: Damage() only ever runs once, on the attacking
    // player's own client (it just dispatches an RPC and returns) - RPC_Damage is the actual
    // handler that every hit against a creature is routed to, and is where vanilla itself
    // gates the "real" processing behind an IsOwner() check. Since Harmony postfixes still
    // run regardless of an early return inside the original method, we replicate that same
    // ownership gate ourselves so damage from every attacker ends up recorded on the same
    // single peer (whichever one owns the creature) - the same peer where OnDeath's kill-XP
    // award below actually runs. See the multiplayer-XP-attribution fix plan for the full
    // reasoning; without this, a creature's damage tracking and kill-XP awarding can execute
    // on two different peers, silently dropping every contributor except whichever peer
    // happens to be both the owner and one of the attackers.
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    [HarmonyPostfix]
    public static void Character_RPC_Damage_Postfix(Character __instance, long sender, HitData hit)
    {
        try
        {
            DevLog.Log($"[XPDBG] RPC_Damage postfix fired: target={__instance?.name}, sender={sender}");

            ZNetView nview = __instance?.GetComponent<ZNetView>();
            if (nview == null || !nview.IsOwner())
            {
                DevLog.Log($"[XPDBG] Skipping: nview={(nview == null ? "null" : "present")}, IsOwner={(nview != null && nview.IsOwner())}");
                return;
            }

            // Only process if damage was actually dealt
            if (hit.GetTotalDamage() <= 0)
            {
                DevLog.Log($"[XPDBG] Skipping: total damage {hit.GetTotalDamage()} <= 0");
                return;
            }

            Character hitAttacker = hit.GetAttacker();
            DevLog.Log($"[XPDBG] hit.GetAttacker() = {(hitAttacker == null ? "null" : hitAttacker.name)} ({(hitAttacker == null ? "?" : hitAttacker.GetType().Name)}), target is Player = {__instance is Player}");

            // Only award XP for player attacks on non-player creatures
            if (hitAttacker is Player attacker && __instance != null && !(__instance is Player))
            {
                // Use hit.m_skill rather than attacker.GetCurrentWeapon(): the attacker's live
                // weapon/inventory state is only reliably populated on their OWN client. This
                // postfix runs on whichever peer owns the TARGET, which for a remote attacker
                // is a different machine - GetCurrentWeapon() there reads back as "Unarmed"
                // regardless of what they're actually holding. HitData.m_skill is set by the
                // attacking client and travels with the networked hit itself, so it's reliable
                // no matter which peer evaluates it (same pattern ArcherPerkManager already
                // uses via hit.m_skill == Skills.SkillType.Bows).
                Skills.SkillType hitSkill = hit.m_skill;
                DevLog.Log($"[XPDBG] Attacker {attacker.GetPlayerName()} hit.m_skill = {hitSkill}");

                var playerData = PlayerClassManager.GetPlayerData(attacker);
                DevLog.Log($"[XPDBG] playerData null={playerData == null}, activeClasses={(playerData == null ? "n/a" : string.Join(",", playerData.activeClasses))}");
                if (playerData == null || playerData.activeClasses.Count == 0) return;

                // Track damage per active class whose skill type matches, for kill-bonus split calculation
                foreach (string activeClass in playerData.activeClasses)
                {
                    bool appropriate = ClassCombatManager.IsSkillAppropriateForClass(hitSkill, activeClass);
                    DevLog.Log($"[XPDBG] IsSkillAppropriateForClass(skill={hitSkill}, class={activeClass}) = {appropriate}");
                    if (appropriate)
                    {
                        ClassXPManager.TrackDamageToCreature(__instance, attacker, hit.GetTotalDamage(), activeClass);
                        DevLog.Log($"[XPDBG] TrackDamageToCreature called: creature={__instance.name}, attacker={attacker.GetPlayerName()}, damage={hit.GetTotalDamage()}, class={activeClass}");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_RPC_Damage_Postfix (XP): {ex.Message}");
        }
    }

    // Patch creature death to award kill bonus. Gated to the ZDO owner for the same reason
    // as the damage-tracking patch above: OnDeath() itself is invoked on every peer that has
    // the creature loaded (not just the owner), and a Harmony postfix runs regardless of
    // OnDeath's own internal ownership check - so without this gate, every such peer would
    // independently (and mostly fruitlessly, since only the owner ever recorded damage)
    // attempt to award kill XP from its own local, un-networked damage tracker.
    //
    // Ownership must be captured in a PREFIX, not read in the postfix: vanilla OnDeath()
    // itself calls ZNetScene.Destroy() near the end of the method (when it IS the owner),
    // which synchronously nulls the ZDO (ZNetView.ResetZDO()) - so by the time a postfix
    // runs, IsOwner() on a just-killed creature always reads false, even for the very peer
    // that owned and correctly processed the kill. (Confirmed via debug logging: IsOwner()
    // was true moments earlier during damage tracking on the same creature, then false in
    // the OnDeath postfix - textbook read-after-teardown, the same class of bug the earlier
    // star-health fix addressed for GetMaxHealth().)
    [HarmonyPatch(typeof(Character), "OnDeath")]
    [HarmonyPrefix]
    public static void Character_OnDeath_Prefix(Character __instance, out bool __state)
    {
        ZNetView nview = __instance?.GetComponent<ZNetView>();
        __state = nview != null && nview.IsOwner();
        DevLog.Log($"[XPDBG] OnDeath prefix: {__instance?.name}, capturedIsOwner={__state}");
    }

    [HarmonyPatch(typeof(Character), "OnDeath")]
    [HarmonyPostfix]
    public static void Character_OnDeath_Postfix(Character __instance, bool __state)
    {
        try
        {
            DevLog.Log($"[XPDBG] OnDeath postfix fired: {__instance?.name}, isPlayer={__instance is Player}, wasOwner={__state}");

            // Only process non-player deaths, using the ownership snapshot from the prefix
            if (__instance != null && !(__instance is Player) && __state)
            {
                ClassXPManager.AwardKillBonusXP(__instance);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_OnDeath_Postfix (XP): {ex.Message}");
        }
    }
}

// Console commands for testing XP system
// Dev-only: excluded from Release builds.
#if DEBUG
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class XPTestCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testxp", "Award test XP to current class (testxp [amount])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                float xpAmount = 100f;
                if (args.Length > 1 && float.TryParse(args.Args[1], out float parsedXP))
                {
                    xpAmount = parsedXP;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null || playerData.activeClasses.Count == 0)
                {
                    args.Context.AddString("No active classes!");
                    return;
                }

                foreach (string activeClass in playerData.activeClasses)
                {
                    int oldLevel = playerData.GetClassLevel(activeClass);
                    float oldXP = playerData.GetClassXP(activeClass);

                    playerData.AddClassXP(activeClass, xpAmount);

                    int newLevel = playerData.GetClassLevel(activeClass);
                    float newXP = playerData.GetClassXP(activeClass);

                    args.Context.AddString($"{activeClass}: +{xpAmount} XP");
                    args.Context.AddString($"Level: {oldLevel} -> {newLevel}");
                    args.Context.AddString($"Total XP: {oldXP:F0} -> {newXP:F0}");

                    if (newLevel > oldLevel)
                    {
                        args.Context.AddString($"*** LEVEL UP! ***");
                    }
                }
            }
        );

        new Terminal.ConsoleCommand("xprates", "Show/set XP rates (xprates [kill_multiplier])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length == 1)
                {
                    args.Context.AddString($"Current XP Rates:");
                    args.Context.AddString($"Kill bonus multiplier: {ClassXPManager.KillBonusMultiplier:F1}");
                    return;
                }

                if (args.Length >= 2 && float.TryParse(args.Args[1], out float killMultiplier))
                {
                    ClassXPManager.KillBonusMultiplier = killMultiplier;
                    args.Context.AddString($"Set kill bonus multiplier to: {killMultiplier:F1}");
                }
            }
        );

        new Terminal.ConsoleCommand("xpcurve", "Test XP curve calculations (xpcurve [level])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2 || !int.TryParse(args.Args[1], out int level))
                {
                    args.Context.AddString("Usage: xpcurve [level]");
                    args.Context.AddString("Shows XP requirements for specific level");
                    return;
                }

                if (level < 1 || level > 50)
                {
                    args.Context.AddString("Level must be between 1-50");
                    return;
                }

                float xpForLevel = XPCurveHelper.GetXPRequiredForLevel(level);
                float totalXP = XPCurveHelper.GetTotalXPForLevel(level);

                args.Context.AddString($"Level {level}:");
                args.Context.AddString($"XP required for this level: {xpForLevel:F0}");
                args.Context.AddString($"Total XP to reach level: {totalXP:F0}");

                if (level < 50)
                {
                    float nextLevelXP = XPCurveHelper.GetXPRequiredForLevel(level + 1);
                    args.Context.AddString($"XP required for level {level + 1}: {nextLevelXP:F0}");
                }
            }
        );
    }
}
#endif