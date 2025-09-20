using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Logger = Jotunn.Logger;

// XP System Manager for tracking damage and awarding XP
public static class ClassXPManager
{
    // Track damage dealt to creatures by players
    private static Dictionary<Character, Dictionary<long, float>> creatureDamageTracker = new Dictionary<Character, Dictionary<long, float>>();

    // Configuration for XP rates
    public static float DamageToXPRatio = 1f; // 1 damage = 1 XP
    public static float KillBonusMultiplier = 1f; // Kill bonus = creature max health * this multiplier

    // Award XP for damage dealt with appropriate weapon (only for creatures)
    public static void AwardDamageXP(Player player, ItemDrop.ItemData weapon, float damageDealt, Character target)
    {
        if (player == null || damageDealt <= 0 || target == null) return;

        // Only award XP for damage to creatures (not players, not objects)
        if (target is Player || !IsValidCreature(target)) return;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || playerData.activeClasses.Count == 0) return;

        // Award XP to appropriate active classes based on weapon type
        foreach (string activeClass in playerData.activeClasses)
        {
            if (!IsWeaponAppropriateForClass(weapon, activeClass)) return;

            // Award XP based on damage dealt (no cooldown restrictions)
            float xpToAward = damageDealt * DamageToXPRatio;

            float oldXP = playerData.GetClassXP(activeClass);
            int oldLevel = playerData.GetClassLevel(activeClass);

            playerData.AddClassXP(activeClass, xpToAward);

            int newLevel = playerData.GetClassLevel(activeClass);

            // Show XP gain message (occasionally to avoid spam)
            if (Random.Range(0f, 1f) < 0.15f) // 15% chance
            {
                player.Message(MessageHud.MessageType.TopLeft, $"{activeClass}: +{xpToAward:F0} XP");
            }

            // Show level up message
            if (newLevel > oldLevel)
            {
                player.Message(MessageHud.MessageType.Center, $"{activeClass} Level Up! Level {newLevel}");

                // Check for perk unlocks
                if (newLevel % 10 == 0)
                {
                    player.Message(MessageHud.MessageType.Center, $"New {activeClass} Perk Unlocked!");
                }

                Debug.Log($"Player {player.GetPlayerName()} leveled up {activeClass} to level {newLevel}");
            }

            Debug.Log($"Awarded {xpToAward:F1} XP to {activeClass} for {player.GetPlayerName()} (damage: {damageDealt:F1} to {target.name})");
        }
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

    // Track damage dealt to a creature
    public static void TrackDamageToCreature(Character creature, Player attacker, float damage)
    {
        if (creature == null || attacker == null || damage <= 0) return;

        long attackerID = attacker.GetPlayerID();

        if (!creatureDamageTracker.ContainsKey(creature))
        {
            creatureDamageTracker[creature] = new Dictionary<long, float>();
        }

        if (!creatureDamageTracker[creature].ContainsKey(attackerID))
        {
            creatureDamageTracker[creature][attackerID] = 0f;
        }

        creatureDamageTracker[creature][attackerID] += damage;

        Debug.Log($"Tracked {damage:F1} damage to {creature.name} by {attacker.GetPlayerName()} (total: {creatureDamageTracker[creature][attackerID]:F1})");
    }

    // Award kill bonus XP when a creature dies
    public static void AwardKillBonusXP(Character deadCreature)
    {
        if (deadCreature == null || !creatureDamageTracker.ContainsKey(deadCreature)) return;

        var damageContributors = creatureDamageTracker[deadCreature];
        if (damageContributors.Count == 0) return;

        // Calculate kill bonus based on creature's max health
        float maxHealth = deadCreature.GetMaxHealth();
        float totalKillBonus = maxHealth * KillBonusMultiplier;
        float bonusPerPlayer = totalKillBonus / damageContributors.Count;

        Debug.Log($"Creature {deadCreature.name} died. Max health: {maxHealth}, kill bonus: {totalKillBonus}, split among {damageContributors.Count} players");

        // Award XP to each contributor
        foreach (var contributorPair in damageContributors)
        {
            long playerID = contributorPair.Key;
            float damageDealt = contributorPair.Value;

            // Find the player (they might have disconnected)
            Player contributor = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == playerID);
            if (contributor == null) return;

            var playerData = PlayerClassManager.GetPlayerData(contributor);
            if (playerData == null || playerData.activeClasses.Count == 0) return;

            // Award kill bonus to all active classes
            foreach (string activeClass in playerData.activeClasses)
            {
                int oldLevel = playerData.GetClassLevel(activeClass);
                playerData.AddClassXP(activeClass, bonusPerPlayer);
                int newLevel = playerData.GetClassLevel(activeClass);

                // Show kill bonus message
                contributor.Message(MessageHud.MessageType.TopLeft, $"{activeClass}: +{bonusPerPlayer:F0} Kill Bonus");

                // Show level up message
                if (newLevel > oldLevel)
                {
                    contributor.Message(MessageHud.MessageType.Center, $"{activeClass} Level Up! Level {newLevel}");

                    if (newLevel % 10 == 0)
                    {
                        contributor.Message(MessageHud.MessageType.Center, $"New {activeClass} Perk Unlocked!");
                    }
                }

                Debug.Log($"Awarded {bonusPerPlayer:F1} kill bonus XP to {activeClass} for {contributor.GetPlayerName()}");
            }
        }

        // Clean up tracking for this creature
        creatureDamageTracker.Remove(deadCreature);
    }

    // Check if a weapon is appropriate for a class
    public static bool IsWeaponAppropriateForClass(ItemDrop.ItemData weapon, string className)
    {
        switch (className)
        {
            case "Sword Master":
                return ClassCombatManager.IsSwordWeapon(weapon);
            case "Archer":
                return ClassCombatManager.IsBowWeapon(weapon);
            case "Crusher":
                return ClassCombatManager.IsBluntWeapon(weapon);
            case "Assassin":
                return ClassCombatManager.IsKnifeWeapon(weapon);
            case "Pugilist":
                return ClassCombatManager.IsUnarmedAttack(weapon);
            case "Mage":
                return ClassCombatManager.IsMagicWeapon(weapon);
            case "Lancer":
                return ClassCombatManager.IsSpearWeapon(weapon);
            case "Bulwark":
                return true; // Bulwark gains XP from any combat (defensive class)
            default:
                return false;
        }
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

// Updated PlayerClassData with exponential XP curve
public static class XPCurveHelper
{
    // Calculate XP required for a specific level (exponential curve)
    public static float GetXPRequiredForLevel(int level)
    {
        if (level <= 1) return 0f;

        // Exponential curve: level^2 * 50
        // Level 10 = 5,000 XP
        // Level 20 = 20,000 XP  
        // Level 30 = 45,000 XP
        // Level 40 = 80,000 XP
        // Level 50 = 125,000 XP
        return level * level * 50f;
    }

    // Calculate total XP needed to reach a level (sum of all previous levels)
    public static float GetTotalXPForLevel(int level)
    {
        float total = 0f;
        for (int i = 2; i <= level; i++)
        {
            total += GetXPRequiredForLevel(i);
        }
        return total;
    }

    // Get level from total XP
    public static int GetLevelFromXP(float totalXP)
    {
        for (int level = 1; level <= 50; level++)
        {
            if (totalXP < GetTotalXPForLevel(level + 1))
                return level;
        }
        return 50; // Max level
    }

    // Get XP progress toward next level
    public static (float current, float required) GetXPProgress(float totalXP, int currentLevel)
    {
        float xpForCurrentLevel = GetTotalXPForLevel(currentLevel);
        float xpForNextLevel = GetTotalXPForLevel(currentLevel + 1);

        float currentProgress = totalXP - xpForCurrentLevel;
        float requiredForNext = xpForNextLevel - xpForCurrentLevel;

        return (currentProgress, requiredForNext);
    }
}

// Enhanced XP tracking patches
[HarmonyPatch]
public static class XPTrackingPatches
{
    // Patch damage dealing to award XP and track damage
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Postfix(Character __instance, HitData hit)
    {
        try
        {
            // Only process if damage was actually dealt
            if (hit.GetTotalDamage() <= 0) return;

            // Only award XP for player attacks on non-player creatures
            if (hit.GetAttacker() is Player attacker && __instance != null && !(__instance is Player))
            {
                // Award XP for damage dealt (now includes target validation)
                ItemDrop.ItemData weapon = attacker.GetCurrentWeapon();

                if (weapon != null) return;

                var playerData = PlayerClassManager.GetPlayerData(attacker);
                var activeClasses = playerData.activeClasses;
                var isWeaponActive = false;
                foreach (var activeClass in activeClasses)
                {
                    if (ClassXPManager.IsWeaponAppropriateForClass(weapon, activeClass)) isWeaponActive = true;
                }

                if (isWeaponActive)
                {
                    ClassXPManager.AwardDamageXP(attacker, weapon, hit.GetTotalDamage(), __instance);

                    // Track damage for kill bonus calculation
                    ClassXPManager.TrackDamageToCreature(__instance, attacker, hit.GetTotalDamage());
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Postfix (XP): {ex.Message}");
        }
    }

    // Patch creature death to award kill bonus
    [HarmonyPatch(typeof(Character), "OnDeath")]
    [HarmonyPostfix]
    public static void Character_OnDeath_Postfix(Character __instance)
    {
        try
        {
            // Only process non-player deaths
            if (__instance != null && !(__instance is Player))
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

        new Terminal.ConsoleCommand("xprates", "Show/set XP rates (xprates [damage_ratio] [kill_multiplier])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length == 1)
                {
                    args.Context.AddString($"Current XP Rates:");
                    args.Context.AddString($"Damage to XP ratio: {ClassXPManager.DamageToXPRatio:F1}");
                    args.Context.AddString($"Kill bonus multiplier: {ClassXPManager.KillBonusMultiplier:F1}");
                    return;
                }

                if (args.Length >= 2 && float.TryParse(args.Args[1], out float damageRatio))
                {
                    ClassXPManager.DamageToXPRatio = damageRatio;
                    args.Context.AddString($"Set damage to XP ratio to: {damageRatio:F1}");
                }

                if (args.Length >= 3 && float.TryParse(args.Args[2], out float killMultiplier))
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