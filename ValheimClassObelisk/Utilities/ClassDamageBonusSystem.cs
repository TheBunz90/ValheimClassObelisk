using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;

// Class damage bonus system for scaling weapon damage based on class levels
public static class ClassDamageBonusManager
{
    // Configuration for damage scaling
    public static float DamageBonusPerLevel = 0.005f; // 0.5% per level (default)
    public static float MaxDamageBonus = 0.25f; // 25% at level 50 (default)

    // Apply class-based damage bonuses to weapon attacks
    public static float CalculateDamageBonus(Player attacker, ItemDrop.ItemData weapon)
    {
        if (attacker == null || weapon == null) return 1f;

        var playerData = PlayerClassManager.GetPlayerData(attacker);
        if (playerData == null || playerData.activeClasses.Count == 0) return 1f;

        float totalBonus = 1f; // Start with no bonus (100% damage)
        bool foundMatchingClass = false;

        // Check each active class for weapon compatibility
        foreach (string activeClass in playerData.activeClasses)
        {
            if (IsWeaponAppropriateForClass(weapon, activeClass))
            {
                int classLevel = playerData.GetClassLevel(activeClass);
                float classBonus = classLevel * DamageBonusPerLevel;

                // Cap the bonus at the maximum
                classBonus = Mathf.Min(classBonus, MaxDamageBonus);

                // Add to total bonus (bonuses stack if player has multiple matching classes)
                totalBonus += classBonus;
                foundMatchingClass = true;

                //Debug.Log($"Applied {classBonus * 100f:F1}% damage bonus from {activeClass} level {classLevel}");
            }
        }

        // Only log if we found a matching class
        if (foundMatchingClass)
        {
            //Debug.Log($"Total damage multiplier for {attacker.GetPlayerName()}: {totalBonus:F3}x ({(totalBonus - 1f) * 100f:F1}% bonus)");
        }

        return totalBonus;
    }

    // Check if a weapon is appropriate for a class (reusing XP system logic)
    private static bool IsWeaponAppropriateForClass(ItemDrop.ItemData weapon, string className)
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
            case "Brawler":
                return ClassCombatManager.IsUnarmedAttack(weapon);
            case "Wizard":
                return ClassCombatManager.IsMagicWeapon(weapon);
            case "Lancer":
                return ClassCombatManager.IsSpearWeapon(weapon);
            case "Bulwark":
                return false; // Bulwark doesn't get damage bonuses (defensive class)
            default:
                return false;
        }
    }

    // Get detailed damage bonus information for display
    public static string GetDamageBonusInfo(Player player, ItemDrop.ItemData weapon)
    {
        if (player == null || weapon == null) return "No weapon equipped";

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || playerData.activeClasses.Count == 0)
            return "No active classes";

        var bonusInfo = new System.Text.StringBuilder();
        float totalBonus = 0f;
        bool foundMatching = false;

        foreach (string activeClass in playerData.activeClasses)
        {
            if (IsWeaponAppropriateForClass(weapon, activeClass))
            {
                int classLevel = playerData.GetClassLevel(activeClass);
                float classBonus = classLevel * DamageBonusPerLevel;
                classBonus = Mathf.Min(classBonus, MaxDamageBonus);

                bonusInfo.AppendLine($"{activeClass} Lv.{classLevel}: +{classBonus * 100f:F1}%");
                totalBonus += classBonus;
                foundMatching = true;
            }
        }

        if (!foundMatching)
        {
            return $"No active classes match {weapon.m_shared.m_name}";
        }

        bonusInfo.AppendLine($"Total Bonus: +{totalBonus * 100f:F1}%");
        bonusInfo.AppendLine($"Damage Multiplier: {1f + totalBonus:F3}x");

        return bonusInfo.ToString().TrimEnd();
    }
}

// Harmony patches for applying damage bonuses
[HarmonyPatch]
public static class DamageBonusPatches
{
    // Apply damage bonuses before damage calculation
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // Only modify damage for player attacks
            if (!(hit.GetAttacker() is Player attacker)) return;

            // Don't modify PvP damage (optional - remove if you want PvP bonuses)
            if (__instance is Player) return;

            // Get the weapon used for this attack
            ItemDrop.ItemData weapon = attacker.GetCurrentWeapon();
            if (weapon == null) return;

            // Calculate and apply damage bonus
            float damageMultiplier = ClassDamageBonusManager.CalculateDamageBonus(attacker, weapon);

            if (damageMultiplier > 1f) // Only modify if there's actually a bonus
            {
                // Apply multiplier to all damage types
                hit.m_damage.m_damage *= damageMultiplier;
                hit.m_damage.m_blunt *= damageMultiplier;
                hit.m_damage.m_slash *= damageMultiplier;
                hit.m_damage.m_pierce *= damageMultiplier;
                hit.m_damage.m_chop *= damageMultiplier;
                hit.m_damage.m_pickaxe *= damageMultiplier;
                hit.m_damage.m_fire *= damageMultiplier;
                hit.m_damage.m_frost *= damageMultiplier;
                hit.m_damage.m_lightning *= damageMultiplier;
                hit.m_damage.m_poison *= damageMultiplier;
                hit.m_damage.m_spirit *= damageMultiplier;

                // Debug.Log($"Applied {damageMultiplier:F3}x damage multiplier to {attacker.GetPlayerName()}'s attack");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Prefix (Damage Bonus): {ex.Message}");
        }
    }
}

// Console commands for testing damage bonuses
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class DamageBonusCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testdamage", "Show current damage bonus for equipped weapon",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
                if (weapon == null)
                {
                    args.Context.AddString("No weapon equipped!");
                    return;
                }

                args.Context.AddString($"Weapon: {weapon.m_shared.m_name}");
                args.Context.AddString("=== Damage Bonus Info ===");

                string bonusInfo = ClassDamageBonusManager.GetDamageBonusInfo(Player.m_localPlayer, weapon);
                foreach (string line in bonusInfo.Split('\n'))
                {
                    if (!string.IsNullOrEmpty(line))
                        args.Context.AddString(line);
                }
            }
        );

        new Terminal.ConsoleCommand("damagerates", "Show/set damage bonus rates (damagerates [bonus_per_level] [max_bonus])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length == 1)
                {
                    args.Context.AddString($"Current Damage Bonus Rates:");
                    args.Context.AddString($"Bonus per level: {ClassDamageBonusManager.DamageBonusPerLevel * 100f:F1}%");
                    args.Context.AddString($"Maximum bonus: {ClassDamageBonusManager.MaxDamageBonus * 100f:F1}%");
                    args.Context.AddString($"At level 50: {(50 * ClassDamageBonusManager.DamageBonusPerLevel) * 100f:F1}% (capped at max)");
                    return;
                }

                if (args.Length >= 2 && float.TryParse(args.Args[1], out float bonusPerLevel))
                {
                    ClassDamageBonusManager.DamageBonusPerLevel = bonusPerLevel / 100f; // Convert percentage to decimal
                    args.Context.AddString($"Set bonus per level to: {bonusPerLevel:F1}%");
                }

                if (args.Length >= 3 && float.TryParse(args.Args[2], out float maxBonus))
                {
                    ClassDamageBonusManager.MaxDamageBonus = maxBonus / 100f; // Convert percentage to decimal
                    args.Context.AddString($"Set maximum bonus to: {maxBonus:F1}%");
                }
            }
        );

        new Terminal.ConsoleCommand("simulatedamage", "Simulate damage at specific level (simulatedamage [class] [level])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 3)
                {
                    args.Context.AddString("Usage: simulatedamage [class] [level]");
                    args.Context.AddString("Example: simulatedamage \"Sword Master\" 25");
                    return;
                }

                string className = args.Args[1];
                if (args.Length > 3) className += " " + args.Args[2]; // Handle spaces in class names

                if (!int.TryParse(args.Args[args.Length - 1], out int level))
                {
                    args.Context.AddString("Invalid level number");
                    return;
                }

                if (level < 1 || level > 50)
                {
                    args.Context.AddString("Level must be between 1-50");
                    return;
                }

                float bonus = level * ClassDamageBonusManager.DamageBonusPerLevel;
                bonus = Mathf.Min(bonus, ClassDamageBonusManager.MaxDamageBonus);
                float multiplier = 1f + bonus;

                args.Context.AddString($"{className} Level {level}:");
                args.Context.AddString($"Damage Bonus: +{bonus * 100f:F1}%");
                args.Context.AddString($"Damage Multiplier: {multiplier:F3}x");
                args.Context.AddString($"Example: 100 damage becomes {100f * multiplier:F1} damage");
            }
        );
    }
}