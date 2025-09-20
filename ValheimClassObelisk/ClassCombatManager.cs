using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Logger = Jotunn.Logger;

// Combat system manager for handling class-based bonuses
public static class ClassCombatManager
{
    // Weapon type detection
    public static bool IsSwordWeapon(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon &&
            weapon?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon) return false;

        // Check weapon name for sword indicators
        string weaponName = weapon.m_shared.m_name?.ToLower() ?? "";
        return weaponName.Contains("sword") || weaponName.Contains("blade") ||
               weaponName.Contains("saber") || weaponName.Contains("katana");
    }

    public static bool IsBowWeapon(ItemDrop.ItemData weapon)
    {
        return weapon?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Bow;
    }

    public static bool IsBluntWeapon(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon &&
            weapon?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon) return false;

        string weaponName = weapon.m_shared.m_name?.ToLower() ?? "";
        return weaponName.Contains("mace") || weaponName.Contains("hammer") ||
               weaponName.Contains("club") || weaponName.Contains("sledge");
    }

    public static bool IsKnifeWeapon(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared?.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon) return false;

        string weaponName = weapon.m_shared.m_name?.ToLower() ?? "";
        return weaponName.Contains("knife") || weaponName.Contains("dagger") ||
               weaponName.Contains("seax") || weaponName.Contains("razor");
    }

    public static bool IsSpearWeapon(ItemDrop.ItemData weapon)
    {
        return weapon?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon &&
               (weapon.m_shared.m_name?.ToLower().Contains("spear") == true ||
                weapon.m_shared.m_name?.ToLower().Contains("atgeir") == true ||
                weapon.m_shared.m_name?.ToLower().Contains("halberd") == true);
    }

    public static bool IsUnarmedAttack(ItemDrop.ItemData weapon)
    {
        return weapon == null || weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None;
    }

    public static bool IsMagicWeapon(ItemDrop.ItemData weapon)
    {
        return weapon?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon &&
               (weapon.m_shared.m_name?.ToLower().Contains("staff") == true ||
                weapon.m_shared.m_attackStatusEffect != null); // Has magical effects
    }

    // Calculate damage multiplier based on player's active classes and weapon type
    public static float GetClassDamageMultiplier(Player player, ItemDrop.ItemData weapon)
    {
        if (player == null) return 1f;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || playerData.activeClasses.Count == 0) return 1f;

        float totalMultiplier = 1f;

        foreach (string activeClass in playerData.activeClasses)
        {
            float classMultiplier = GetClassSpecificDamageMultiplier(activeClass, playerData, weapon);
            if (classMultiplier > 1f)
            {
                // Add the bonus (not multiply) - so 10% + 15% = 25% total bonus
                totalMultiplier += (classMultiplier - 1f);
            }
        }

        return totalMultiplier;
    }

    private static float GetClassSpecificDamageMultiplier(string className, PlayerClassData playerData, ItemDrop.ItemData weapon)
    {
        int classLevel = playerData.GetClassLevel(className);

        switch (className)
        {
            case "Sword Master":
                return GetSwordMasterDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Pugilist":
                return GetPugilistDamageBonus(classLevel, weapon);

            case "Mage":
                return GetMageDamageBonus(classLevel, weapon);

            case "Lancer":
                return GetLancerDamageBonus(classLevel, weapon);

            case "Bulwark":
                return GetBulwarkDamageBonus(classLevel, weapon);

            default:
                return 1f;
        }
    }

    // Class-specific damage bonus calculations
    private static float GetSwordMasterDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsSwordWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +10% sword damage (additional)
        if (level >= 10) bonus += 0.10f;

        // Level 50: Dancing Steel effect would be handled separately
        // For now, just the base damage bonus

        return 1f + bonus;
    }

    private static float GetArcherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBowWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 50: Eagle Eye - fully drawn shots deal +20% damage (additional)
        if (level >= 50) bonus += 0.20f;

        return 1f + bonus;
    }

    private static float GetCrusherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBluntWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +12% blunt damage (additional)
        if (level >= 10) bonus += 0.12f;

        return 1f + bonus;
    }

    private static float GetAssassinDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsKnifeWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +12% knife damage (additional)
        if (level >= 10) bonus += 0.12f;

        return 1f + bonus;
    }

    private static float GetPugilistDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsUnarmedAttack(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +15% unarmed damage (additional)
        if (level >= 10) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetMageDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsMagicWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +8% magic damage (additional)
        if (level >= 10) bonus += 0.08f;

        // Level 40: +12% magic damage when below 50% Eitr (would need separate check)
        // For now, just base bonus

        return 1f + bonus;
    }

    private static float GetLancerDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsSpearWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Base: 0.5% per level
        bonus += level * 0.005f;

        // Level 10: +10% spear damage (additional)
        if (level >= 10) bonus += 0.10f;

        return 1f + bonus;
    }

    private static float GetBulwarkDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        // Bulwark doesn't get weapon damage bonuses, they get defensive bonuses
        // Those would be handled in different patches (block power, etc.)
        return 1f;
    }

    // Method to show damage bonus feedback to player
    public static void ShowDamageBonusMessage(Player player, float multiplier, string weaponType)
    {
        if (multiplier > 1f && player == Player.m_localPlayer)
        {
            float bonusPercent = (multiplier - 1f) * 100f;
            // Only show occasionally to avoid spam
            if (Random.Range(0f, 1f) < 0.1f) // 10% chance
            {
                player.Message(MessageHud.MessageType.TopLeft, $"{weaponType} Mastery: +{bonusPercent:F0}% damage!");
            }
        }
    }

    // Public version of damage multiplier calculation for console commands
    public static float GetClassSpecificDamageMultiplierPublic(string className, PlayerClassData playerData, ItemDrop.ItemData weapon)
    {
        int classLevel = playerData.GetClassLevel(className);

        switch (className)
        {
            case "Sword Master":
                return GetSwordMasterDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Pugilist":
                return GetPugilistDamageBonus(classLevel, weapon);

            case "Mage":
                return GetMageDamageBonus(classLevel, weapon);

            case "Lancer":
                return GetLancerDamageBonus(classLevel, weapon);

            case "Bulwark":
                return GetBulwarkDamageBonus(classLevel, weapon);

            default:
                return 1f;
        }
    }
}

// Harmony patches for applying damage bonuses
[HarmonyPatch]
public static class CombatPatches
{
    // Patch for melee weapon damage - using the correct method name
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // Only apply to player attacks
            if (hit.GetAttacker() is Player player)
            {
                // Get the weapon used for this attack
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();

                // Calculate class-based damage multiplier
                float multiplier = ClassCombatManager.GetClassDamageMultiplier(player, weapon);

                if (multiplier > 1f)
                {
                    // Apply the multiplier to physical damage
                    hit.m_damage.m_slash *= multiplier;
                    hit.m_damage.m_pierce *= multiplier;
                    hit.m_damage.m_blunt *= multiplier;
                    hit.m_damage.m_chop *= multiplier;

                    // Show feedback message
                    string weaponType = GetWeaponTypeName(weapon);
                    ClassCombatManager.ShowDamageBonusMessage(player, multiplier, weaponType);

                    // Debug logging
                    Debug.Log($"Applied {multiplier:F2}x damage multiplier for {player.GetPlayerName()} using {weaponType}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Prefix: {ex.Message}");
        }
    }

    // Note: Projectile damage patching for arrows and thrown weapons
    // We'll try a different approach by patching the projectile damage method

    // Helper method to get weapon type name for feedback
    public static string GetWeaponTypeName(ItemDrop.ItemData weapon)
    {
        if (ClassCombatManager.IsSwordWeapon(weapon)) return "Sword";
        if (ClassCombatManager.IsBowWeapon(weapon)) return "Bow";
        if (ClassCombatManager.IsBluntWeapon(weapon)) return "Blunt";
        if (ClassCombatManager.IsKnifeWeapon(weapon)) return "Knife";
        if (ClassCombatManager.IsSpearWeapon(weapon)) return "Spear";
        if (ClassCombatManager.IsUnarmedAttack(weapon)) return "Unarmed";
        if (ClassCombatManager.IsMagicWeapon(weapon)) return "Magic";
        return "Weapon";
    }
}

// Additional patches for projectile damage
[HarmonyPatch]
public static class ProjectilePatches
{
    // Try patching the projectile DoAreaDamage method instead
    [HarmonyPatch(typeof(Projectile), "DoAreaDamage")]
    [HarmonyPrefix]
    public static void Projectile_DoAreaDamage_Prefix(Projectile __instance, ref HitData hit, Vector3 center, float radius)
    {
        try
        {
            // Check if this projectile was fired by a player
            Character owner = GetProjectileOwner(__instance);
            if (owner is Player player)
            {
                // Determine weapon type based on projectile
                ItemDrop.ItemData weapon = GetWeaponFromProjectile(__instance, player);

                // Calculate class-based damage multiplier
                float multiplier = ClassCombatManager.GetClassDamageMultiplier(player, weapon);

                if (multiplier > 1f)
                {
                    // Apply the multiplier to projectile damage
                    hit.m_damage.m_pierce *= multiplier;
                    hit.m_damage.m_slash *= multiplier;
                    hit.m_damage.m_blunt *= multiplier;

                    // Show feedback message
                    string weaponType = CombatPatches.GetWeaponTypeName(weapon);
                    ClassCombatManager.ShowDamageBonusMessage(player, multiplier, weaponType);

                    Debug.Log($"Applied {multiplier:F2}x projectile damage multiplier for {player.GetPlayerName()} using {weaponType}");
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_DoAreaDamage_Prefix: {ex.Message}");
        }
    }

    // Alternative approach - patch the main projectile hit method with correct signature
    [HarmonyPatch(typeof(Projectile), "OnHit", new System.Type[] { typeof(Collider), typeof(Vector3), typeof(bool) })]
    [HarmonyPrefix]
    public static void Projectile_OnHit_Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water)
    {
        try
        {
            // Only process hits on characters
            Character hitCharacter = collider?.GetComponent<Character>();
            if (hitCharacter == null) return;

            // Check if this projectile was fired by a player
            Character owner = GetProjectileOwner(__instance);
            if (owner is Player player)
            {
                // For XP tracking, we need to create a hit data approximation
                // This is more complex since we don't have the actual HitData yet
                // We'll handle this in a postfix to get the damage after it's calculated
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_OnHit_Prefix: {ex.Message}");
        }
    }

    // Helper method to get projectile owner
    private static Character GetProjectileOwner(Projectile projectile)
    {
        try
        {
            // Try different ways to get the owner
            //if (projectile.m_owner != null)
            //    return projectile.m_owner;

            // Alternative: check for ZNetView and get owner from that
            var znetView = projectile.GetComponent<ZNetView>();
            if (znetView != null && znetView.IsValid())
            {
                long ownerID = znetView.GetZDO().GetLong("owner");
                if (ownerID != 0)
                {
                    // Find player by ID
                    foreach (Player p in Player.GetAllPlayers())
                    {
                        if (p.GetPlayerID() == ownerID)
                            return p;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Helper method to determine weapon type from projectile
    private static ItemDrop.ItemData GetWeaponFromProjectile(Projectile projectile, Player owner)
    {
        try
        {
            // Check projectile name to determine weapon type
            string projectileName = projectile.name.ToLower();

            if (projectileName.Contains("arrow"))
            {
                // Find bow in player's inventory
                return owner.GetInventory().GetAllItems()
                    .FirstOrDefault(item => item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow);
            }
            else if (projectileName.Contains("spear"))
            {
                // Create dummy spear weapon
                var dummySpear = new ItemDrop.ItemData();
                dummySpear.m_shared = new ItemDrop.ItemData.SharedData();
                dummySpear.m_shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
                dummySpear.m_shared.m_name = "spear";
                return dummySpear;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

// Additional console commands for testing combat bonuses
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class CombatDebugCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testdamage", "Test current weapon damage bonus",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var weapon = Player.m_localPlayer.GetCurrentWeapon();
                var multiplier = ClassCombatManager.GetClassDamageMultiplier(Player.m_localPlayer, weapon);
                var weaponName = weapon?.m_shared?.m_name ?? "Unarmed";

                args.Context.AddString($"Current weapon: {weaponName}");
                args.Context.AddString($"Damage multiplier: {multiplier:F2}x ({(multiplier - 1f) * 100f:F0}% bonus)");

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData != null)
                {
                    args.Context.AddString($"Active classes: {string.Join(", ", playerData.activeClasses)}");
                }
            }
        );

        new Terminal.ConsoleCommand("weapontype", "Check what type the current weapon is detected as",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var weapon = Player.m_localPlayer.GetCurrentWeapon();
                var weaponName = weapon?.m_shared?.m_name ?? "None";

                args.Context.AddString($"Current weapon: {weaponName}");
                args.Context.AddString($"Is Sword: {ClassCombatManager.IsSwordWeapon(weapon)}");
                args.Context.AddString($"Is Bow: {ClassCombatManager.IsBowWeapon(weapon)}");
                args.Context.AddString($"Is Blunt: {ClassCombatManager.IsBluntWeapon(weapon)}");
                args.Context.AddString($"Is Knife: {ClassCombatManager.IsKnifeWeapon(weapon)}");
                args.Context.AddString($"Is Spear: {ClassCombatManager.IsSpearWeapon(weapon)}");
                args.Context.AddString($"Is Unarmed: {ClassCombatManager.IsUnarmedAttack(weapon)}");
                args.Context.AddString($"Is Magic: {ClassCombatManager.IsMagicWeapon(weapon)}");
            }
        );

        new Terminal.ConsoleCommand("classlevels", "Show all class levels and damage bonuses",
            delegate (Terminal.ConsoleEventArgs args)
            {
                try
                {
                    if (Player.m_localPlayer == null)
                    {
                        args.Context.AddString("No local player found!");
                        return;
                    }

                    var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                    if (playerData == null)
                    {
                        args.Context.AddString("No class data found!");
                        return;
                    }

                    var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                    var weaponName = currentWeapon?.m_shared?.m_name ?? "Unarmed";

                    args.Context.AddString($"=== CLASS LEVELS & DAMAGE BONUSES ===");
                    args.Context.AddString($"Current weapon: {weaponName}");
                    args.Context.AddString($"Active classes: {string.Join(", ", playerData.activeClasses)}");
                    args.Context.AddString("");

                    foreach (var className in PlayerClassManager.GetAllClassNames())
                    {
                        int level = playerData.GetClassLevel(className);
                        float xp = playerData.GetClassXP(className);
                        bool isActive = playerData.activeClasses.Contains(className);

                        // Get damage multiplier for current weapon
                        float multiplier = 1f;
                        if (isActive)
                        {
                            multiplier = ClassCombatManager.GetClassSpecificDamageMultiplierPublic(className, playerData, currentWeapon);
                        }

                        string activeMarker = isActive ? " [ACTIVE]" : "";
                        string bonusText = multiplier > 1f ? $" (Damage: +{(multiplier - 1f) * 100f:F1}%)" : " (No bonus)";

                        args.Context.AddString($"{className}: Level {level} (XP: {xp:F0}){activeMarker}{bonusText}");

                        // Show next perk info
                        int nextPerkLevel = ((level / 10) + 1) * 10;
                        if (nextPerkLevel <= 50)
                        {
                            float totalXPForNextPerk = XPCurveHelper.GetTotalXPForLevel(nextPerkLevel);
                            float xpNeeded = totalXPForNextPerk - xp;
                            args.Context.AddString($"  Next perk at level {nextPerkLevel} (need {xpNeeded:F0} more XP)");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    args.Context.AddString($"Error: {ex.Message}");
                }
            }
        );
    }
}