using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Logger = Jotunn.Logger;

// Combat system manager for handling class-based bonuses
public static class ClassCombatManager
{
    // Weapon type detection. Classified by the weapon's own m_skillType (the same
    // Skills.SkillType the game uses for weapon-skill leveling) rather than matching
    // keywords in the item name - authoritative, and doesn't need updating when new
    // named/legendary weapons are added in future updates.
    private static bool IsWeaponItemType(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared == null) return false;
        ItemDrop.ItemData.ItemType type = weapon.m_shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
               type == ItemDrop.ItemData.ItemType.Bow;
    }

    // Mirrors vanilla's own (private) ItemDrop.ItemData.IsTwoHanded() - confirmed via decompile.
    // The one generic 1H/2H primitive for classes that need different perk behavior per hand
    // count (Axemaster now; Crusher/Sword Master/Lancer in later reworks).
    public static bool IsTwoHandedWeapon(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared == null) return false;
        ItemDrop.ItemData.ItemType type = weapon.m_shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
               type == ItemDrop.ItemData.ItemType.Bow;
    }

    public static bool IsSwordWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Swords;
    }

    public static bool IsAxeWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Axes;
    }

    public static bool IsBowWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;

        // Crossbows count as Archer weapons alongside regular bows.
        return weapon.m_shared.m_skillType == Skills.SkillType.Bows ||
               weapon.m_shared.m_skillType == Skills.SkillType.Crossbows;
    }

    public static bool IsBluntWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Clubs;
    }

    public static bool IsKnifeWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Knives;
    }

    public static bool IsSpearWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;

        // Polearms (Atgeirs) count alongside Spears, as they did before this refactor.
        return weapon.m_shared.m_skillType == Skills.SkillType.Spears ||
               weapon.m_shared.m_skillType == Skills.SkillType.Polearms;
    }

    public static bool IsUnarmedAttack(ItemDrop.ItemData weapon)
    {
        return weapon == null ||
               weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None ||
               weapon.m_shared.m_skillType == Skills.SkillType.Unarmed ||
               weapon.m_shared.m_name == "Unarmed";
    }

    // Same class <-> skill mapping as the IsXWeapon functions above, but keyed directly off
    // a Skills.SkillType (e.g. from HitData.m_skill) instead of an ItemData object. Needed
    // because ItemDrop.ItemData.GetCurrentWeapon() reads live inventory/equipment state that
    // is only reliably populated on the attacking player's own client - when a hit is
    // processed on a different peer (the ZDO owner of whoever got hit, not necessarily the
    // attacker), a remote attacker's weapon reads back as an "Unarmed" placeholder. HitData's
    // own m_skill field is set by the attacking client and travels with the networked hit,
    // so it's reliable regardless of which peer evaluates it.
    public static bool IsSkillAppropriateForClass(Skills.SkillType skill, string className)
    {
        switch (className)
        {
            case "Sword Master":
                return skill == Skills.SkillType.Swords;
            case "Axemaster":
                return skill == Skills.SkillType.Axes;
            case "Archer":
                return skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
            case "Crusher":
                return skill == Skills.SkillType.Clubs;
            case "Assassin":
                return skill == Skills.SkillType.Knives;
            case "Brawler":
                return skill == Skills.SkillType.Unarmed;
            case "Wizard":
                return skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic;
            case "Lancer":
                return skill == Skills.SkillType.Spears || skill == Skills.SkillType.Polearms;
            case "Bulwark":
                return true; // Bulwark gains XP from any combat (defensive class)
            default:
                return false;
        }
    }

    public static bool IsMagicWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.ElementalMagic ||
               weapon.m_shared.m_skillType == Skills.SkillType.BloodMagic;
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

            case "Axemaster":
                return GetAxemasterDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Brawler":
                return GetBrawlerDamageBonus(classLevel, weapon);

            case "Wizard":
                return GetWizardDamageBonus(classLevel, weapon);

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

        // Level 10: +10% sword damage (additional)
        if (level >= 10) bonus += 0.10f;

        // Level 50: Dancing Steel effect would be handled separately
        // For now, just the base damage bonus

        return 1f + bonus;
    }

    private static float GetAxemasterDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsAxeWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Chopper's Training - +8% axe damage (1H and 2H alike)
        if (level >= 10) bonus += 0.08f;

        // Level 50: Executioner - +15% axe damage (flat component; the conditional
        // low-health bonus is handled separately in AxemasterPerkManager)
        if (level >= 50) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetArcherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBowWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 50: Eagle Eye - fully drawn shots deal +20% damage (additional)
        if (level >= 50) bonus += 0.20f;

        return 1f + bonus;
    }

    private static float GetCrusherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBluntWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: +12% blunt damage (additional)
        if (level >= 10) bonus += 0.12f;

        return 1f + bonus;
    }

    private static float GetAssassinDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsKnifeWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Cutthroat - +7% knife damage (the only flat bonus Assassin gets; Twist the
        // Knife at 50 is conditional on the target being poisoned, handled in AssassinPerkManager)
        if (level >= 10) bonus += 0.07f;

        return 1f + bonus;
    }

    private static float GetBrawlerDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsUnarmedAttack(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Bare-Knuckle Training - +8% unarmed damage
        if (level >= 10) bonus += 0.08f;

        return 1f + bonus;
    }

    private static float GetWizardDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsMagicWeapon(weapon)) return 1f;

        float bonus = 0f;

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

    // Public version of damage multiplier calculation for console commands
    public static float GetClassSpecificDamageMultiplierPublic(string className, PlayerClassData playerData, ItemDrop.ItemData weapon)
    {
        int classLevel = playerData.GetClassLevel(className);

        switch (className)
        {
            case "Sword Master":
                return GetSwordMasterDamageBonus(classLevel, weapon);

            case "Axemaster":
                return GetAxemasterDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Brawler":
                return GetBrawlerDamageBonus(classLevel, weapon);

            case "Wizard":
                return GetWizardDamageBonus(classLevel, weapon);

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
            // Skip synthetic damage-over-time ticks (e.g. a class's own bleed/poison DoT
            // re-entering Character.Damage on a timer). A real weapon swing always has
            // hit.m_skill set by Valheim's own attack pipeline; a hand-built HitData for a DoT
            // tick leaves it at the default None unless deliberately set otherwise. Without this,
            // a DoT that uses a physical damage field (m_slash/m_pierce/m_blunt/m_chop, the only
            // fields this prefix touches) would get re-multiplied by the class's own damage bonus
            // on every tick.
            if (hit.m_skill == Skills.SkillType.None) return;

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
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Prefix: {ex.Message}");
        }
    }
}

// Projectile damage tracking - inspired by your HarpoonFish mod approach
[HarmonyPatch]
public static class ProjectilePatches
{
    // Track active projectiles and their owners
    private static Dictionary<int, ProjectileInfo> trackedProjectiles = new Dictionary<int, ProjectileInfo>();

    private class ProjectileInfo
    {
        public Player owner;
        public ItemDrop.ItemData weapon;
        public float createdTime;
        public string projectileName;
    }

    // Patch projectile creation/setup methods to register them
    [HarmonyPatch(typeof(Projectile), "Setup")]
    [HarmonyPostfix]
    public static void Projectile_Setup_Postfix(Projectile __instance, Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item)
    {
        try
        {
            if (owner is Player player && __instance != null)
            {
                // Determine weapon type from the projectile
                ItemDrop.ItemData weapon = GetWeaponFromProjectileContext(player, __instance, item);

                var projectileInfo = new ProjectileInfo
                {
                    owner = player,
                    weapon = weapon,
                    createdTime = Time.time,
                    projectileName = __instance.name
                };

                trackedProjectiles[__instance.GetInstanceID()] = projectileInfo;

                Debug.Log($"Registered projectile {__instance.name} for {player.GetPlayerName()} with weapon type {weapon?.m_shared?.m_name ?? "unknown"}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_Setup_Postfix: {ex.Message}");
        }
    }

    // Alternative patch for projectiles that don't use Setup
    [HarmonyPatch(typeof(Projectile), "Awake")]
    [HarmonyPostfix]
    public static void Projectile_Awake_Postfix(Projectile __instance)
    {
        try
        {
            // Some projectiles might not go through Setup, try to get owner info later
            if (__instance != null && !trackedProjectiles.ContainsKey(__instance.GetInstanceID()))
            {
                // We'll try to resolve the owner when the projectile hits something
                var projectileInfo = new ProjectileInfo
                {
                    owner = null, // Will resolve on hit
                    weapon = null,
                    createdTime = Time.time,
                    projectileName = __instance.name
                };

                trackedProjectiles[__instance.GetInstanceID()] = projectileInfo;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_Awake_Postfix: {ex.Message}");
        }
    }

    // Patch the projectile hit with the correct signature
    //[HarmonyPatch(typeof(Projectile), "OnHit", new System.Type[] { typeof(Collider), typeof(Vector3), typeof(bool) })]
    //[HarmonyPrefix]
    //public static void Projectile_OnHit_Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water)
    //{
    //    try
    //    {
    //        if (__instance == null) return;

    //        int projectileId = __instance.GetInstanceID();
    //        if (!trackedProjectiles.TryGetValue(projectileId, out ProjectileInfo info)) return;

    //        // Try to resolve owner if we don't have it
    //        if (info.owner == null)
    //        {
    //            info.owner = ResolveProjectileOwner(__instance);
    //            info.weapon = GetWeaponFromProjectileContext(info.owner, __instance, null);
    //        }

    //        if (info.owner == null) return;

    //        // Check if we hit a valid creature
    //        Character hitCharacter = collider?.GetComponent<Character>();
    //        if (hitCharacter == null || hitCharacter is Player) return;

    //        // Calculate damage multiplier for projectile
    //        float multiplier = ClassCombatManager.GetClassDamageMultiplier(info.owner, info.weapon);

    //        if (multiplier > 1f)
    //        {
    //            Debug.Log($"Projectile {info.projectileName} hit {hitCharacter.name}, would apply {multiplier:F2}x multiplier");
    //            // Note: We can't modify the damage here as it hasn't been calculated yet
    //            // The damage bonus will need to be applied elsewhere
    //        }

    //        // Clean up tracking
    //        trackedProjectiles.Remove(projectileId);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogError($"Error in Projectile_OnHit_Prefix: {ex.Message}");
    //    }
    //}

    // Helper method to determine weapon from projectile context
    private static ItemDrop.ItemData GetWeaponFromProjectileContext(Player player, Projectile projectile, ItemDrop.ItemData item)
    {
        if (item != null) return item; // Use provided item if available
        if (player == null || projectile == null) return null;

        string projectileName = projectile.name.ToLower();

        // Check for arrow projectiles
        if (projectileName.Contains("arrow"))
        {
            // Find bow in player's inventory
            return player.GetInventory().GetAllItems()
                .FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow);
        }

        // Check for spear projectiles
        if (projectileName.Contains("spear") || projectileName.Contains("harpoon"))
        {
            // Create dummy spear weapon for damage calculation
            var dummySpear = new ItemDrop.ItemData();
            dummySpear.m_shared = new ItemDrop.ItemData.SharedData();
            dummySpear.m_shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            dummySpear.m_shared.m_name = "spear";
            return dummySpear;
        }

        // Fallback: check player's current weapon
        return player.GetCurrentWeapon();
    }

    // Helper method to resolve projectile owner (similar to your HarpoonFish approach)
    private static Player ResolveProjectileOwner(Projectile projectile)
    {
        try
        {
            // Try to get owner from ZNetView
            var znetView = projectile.GetComponent<ZNetView>();
            if (znetView != null && znetView.IsValid())
            {
                long ownerID = znetView.GetZDO().GetLong("owner");
                if (ownerID != 0)
                {
                    return Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == ownerID);
                }
            }

            // Fallback to local player if nearby
            var localPlayer = Player.m_localPlayer;
            if (localPlayer != null && Vector3.Distance(localPlayer.transform.position, projectile.transform.position) < 50f)
            {
                return localPlayer;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Cleanup old projectiles periodically
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Postfix()
    {
        try
        {
            if (Time.frameCount % 300 != 0) return; // Check every 300 frames (~5 seconds)

            var toRemove = new List<int>();
            float currentTime = Time.time;

            foreach (var kvp in trackedProjectiles)
            {
                if (currentTime - kvp.Value.createdTime > 30f) // Remove after 30 seconds
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int id in toRemove)
            {
                trackedProjectiles.Remove(id);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Postfix (projectile cleanup): {ex.Message}");
        }
    }
}

// Additional console commands for testing combat bonuses
// Dev-only: excluded from Release builds.
#if DEBUG
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
#endif