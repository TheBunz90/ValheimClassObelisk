using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

/// <summary>
/// Crusher class perk system - focused on heavy blunt weapons, elemental damage, and stamina efficiency
/// </summary>
public static class CrusherPerkManager
{
    // Configuration
    public const float COLD_STEEL_FROST_MULTIPLIER = 0.20f; // 20% of weapon damage as frost
    public const float THUNDERING_BLOWS_RADIUS = 5f; // 5 meter shockwave

    /// <summary>
    /// Check if player has Crusher class active and at required level
    /// </summary>
    public static bool HasCrusherPerk(Player player, int requiredLevel)
    {
        if (player == null) return false;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive(PlayerClass.Crusher)) return false;

        return playerData.GetClassLevel(PlayerClass.Crusher) >= requiredLevel;
    }

    #region Level 10 - Bonebreaker
    /// <summary>
    /// Lv10 – Bonebreaker: +15% blunt damage; +25% stagger power
    /// </summary>
    public static float ApplyLv10_BonebreakerDamage(Player player, float baseDamage)
    {
        if (!HasCrusherPerk(player, 10)) return baseDamage;

        return baseDamage * 1.15f; // 15% increased blunt damage
    }

    public static float ApplyLv10_BonebreakerStagger(Player player, float baseStagger)
    {
        if (!HasCrusherPerk(player, 10)) return baseStagger;

        return baseStagger * 1.25f; // 25% increased stagger power
    }
    #endregion

    #region Level 20 - Cold Steel
    /// <summary>
    /// Lv20 – Cold Steel: Your melee weapon attacks are imbued with frost dealing +20% weapon damage as frost damage
    /// </summary>
    public static void ApplyLv20_ColdSteelFrost(Player player, ref HitData hit, float weaponDamage)
    {
        if (!HasCrusherPerk(player, 20)) return;

        // Add frost damage equal to 20% of weapon damage
        float frostDamage = weaponDamage * COLD_STEEL_FROST_MULTIPLIER;
        hit.m_damage.m_frost += frostDamage;

        // Show visual effect occasionally
        if (Random.Range(0f, 1f) < 0.3f) // 30% chance to show message
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Cold Steel! +{frostDamage:F0} frost damage");
        }
    }
    #endregion

    #region Level 30 - Thundering Blows
    /// <summary>
    /// Lv30 – Thundering Blows: Heavy melee attacks generate a 2m shockwave of lightning damage
    /// </summary>
    public static void TriggerLv30_ThunderingBlows(Player player, Vector3 hitPoint, float baseDamage)
    {
        if (!HasCrusherPerk(player, 30)) return;

        // Find all enemies within radius
        var nearbyEnemies = Physics.OverlapSphere(hitPoint, THUNDERING_BLOWS_RADIUS)
            .Select(c => c.GetComponent<Character>())
            .Where(c => c != null && c != player && !c.IsDead() && c.IsMonsterFaction(0f))
            .ToList();

        if (nearbyEnemies.Count == 0) return;

        // Calculate lightning damage (50% of base damage for shockwave)
        float lightningDamage = baseDamage * 0.5f;

        foreach (var enemy in nearbyEnemies)
        {
            // Create hit data for lightning damage
            HitData shockwaveHit = new HitData();
            shockwaveHit.m_attacker = player.GetZDOID();
            shockwaveHit.m_damage.m_lightning = lightningDamage;
            shockwaveHit.m_point = enemy.transform.position;
            shockwaveHit.m_dir = (enemy.transform.position - hitPoint).normalized;
            shockwaveHit.m_skill = Skills.SkillType.Clubs; // Use clubs skill for blunt weapons

            enemy.Damage(shockwaveHit);
        }

        // Visual effect - try to create lightning VFX
        // Note: removed this visual effect because it nukes the player killing them.
        // May want to try other effects in the future for fun.
        // CreateThunderingBlowsEffect(hitPoint);

        player.Message(MessageHud.MessageType.TopLeft, $"Thundering Blow! Hit {nearbyEnemies.Count} enemies");
    }

    private static void CreateThunderingBlowsEffect(Vector3 position)
    {
        try
        {
            // Try to find and use a lightning effect from the game
            var lightningAOE = ZNetScene.instance?.GetPrefab("lightningAOE");
            if (lightningAOE != null)
            {
                Object.Instantiate(lightningAOE, position, Quaternion.identity);
            }
            else
            {
                // Fallback to a generic impact effect
                var impactEffect = ZNetScene.instance?.GetPrefab("vfx_HitSparks");
                if (impactEffect != null)
                {
                    Object.Instantiate(impactEffect, position, Quaternion.identity);
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogWarning($"Could not create Thundering Blows effect: {ex.Message}");
        }
    }
    #endregion

    #region Level 40 - Might of the Earth
    /// <summary>
    /// Lv40 – Might of the Earth: Your strength from wielding heavy weapons mitigates stamina drain from attacks. -30% stamina drain on attacks
    /// </summary>
    public static float ApplyLv40_MightOfTheEarthStamina(Player player, float staminaCost)
    {
        if (!HasCrusherPerk(player, 40)) return staminaCost;

        // Check if this is an attack action (not blocking or other stamina uses)
        var weapon = player.GetCurrentWeapon();
        if (weapon != null && ClassCombatManager.IsBluntWeapon(weapon))
        {
            return staminaCost * 0.70f; // 30% reduction in stamina cost
        }

        return staminaCost;
    }
    #endregion

    #region level 50 - Colossus
    // This perk gets applied by the EquipItem patch in patches.
    // Summary: it checks for negative movespeed modifiers and removes them.
    #endregion

    #region Utility Methods
    /// <summary>
    /// Check if an attack is a heavy attack (secondary attack)
    /// </summary>
    public static bool IsHeavyAttack(Attack.AttackType attackType)
    {
        // In Valheim, secondary attacks are typically considered "heavy" attacks
        return attackType == Attack.AttackType.Vertical;
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Crusher perks with game systems
/// </summary>
[HarmonyPatch]
public static class CrusherPerkPatches
{
    private static bool triggerThunderingBlowsDamage;

    #region Damage Patches
    /// <summary>
    /// Apply Crusher damage bonuses and elemental effects when using blunt weapons
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Crusher_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only apply to blunt weapon damage
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBluntWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Crusher)) return;

            float originalDamage = hit.GetTotalDamage();

            // Apply Bonebreaker damage bonus (Level 10)
            float bluntDamage = hit.m_damage.m_blunt;
            if (bluntDamage > 0)
            {
                hit.m_damage.m_blunt = CrusherPerkManager.ApplyLv10_BonebreakerDamage(player, bluntDamage);
            }

            // Apply Bonebreaker stagger bonus (Level 10)
            hit.m_staggerMultiplier = CrusherPerkManager.ApplyLv10_BonebreakerStagger(player, hit.m_staggerMultiplier);

            // Apply Cold Steel frost damage (Level 20)
            CrusherPerkManager.ApplyLv20_ColdSteelFrost(player, ref hit, originalDamage);

            // Check for Thundering Blows trigger (Level 30)
            // We'll trigger this in the postfix after the hit lands
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Crusher_Prefix: {ex.Message}");
        }
    }

    // Summary
    // Patch Humanoid StartAttack and set a flag for triggering
    // Thundering Blows if the attack was a Secondary Attack.
    // End Summary
    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    [HarmonyPrefix]
    public static void Humanoid_StartAttack_Prefix(Character target, bool secondaryAttack)
    {
        // get current weapon.
        var player = Player.m_localPlayer;
        var currentWeapon = player.GetCurrentWeapon();
        // check weapon matches active class weapons.
        if (!ClassCombatManager.IsBluntWeapon(currentWeapon)) return;
        // set flag true.
        if (secondaryAttack) triggerThunderingBlowsDamage = true;
    }

    /// <summary>
    /// Trigger Thundering Blows shockwave after heavy attack hits
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Crusher_Postfix(Character __instance, HitData hit)
    {
        try
        {
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only apply to blunt weapon damage
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBluntWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Crusher)) return;

            // Check if this was a heavy attack for Thundering Blows
            if (hit.m_damage.m_blunt > 0 && triggerThunderingBlowsDamage)
            {
                CrusherPerkManager.TriggerLv30_ThunderingBlows(player, hit.m_point, hit.GetTotalDamage());
                triggerThunderingBlowsDamage = false;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Crusher_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Stamina Patches
    /// <summary>
    /// Apply Might of the Earth stamina reduction when using stamina for attacks
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_Crusher_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (__instance == null) return;

            // Apply Might of the Earth stamina reduction (Level 40)
            v = CrusherPerkManager.ApplyLv40_MightOfTheEarthStamina(__instance, v);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_Crusher_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Movement Speed Patches
    [HarmonyPatch(typeof(Humanoid), "EquipItem")]
    [HarmonyPostfix]
    public static void Humanoid_EquipItem_Prefix(ItemDrop.ItemData item, bool triggerEquipEffects = true)
    {
        try
        {
            // Check item is null.
            if (item == null) return;

            // Check is player has colossus perk.
            var player = Player.m_localPlayer;
            if (!CrusherPerkManager.HasCrusherPerk(player, 50)) return;

            // TODO: remove the movementModifier for item if it's negative.
            var movementModifier = item.m_shared.m_movementModifier;
            if (movementModifier > 0f) return;

            item.m_shared.m_movementModifier = 0f;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_GetRunSpeedFactor_Crusher_Postfix: {ex.Message}");
        }
    }
    #endregion
}

/// <summary>
/// Console commands for testing Crusher perks
/// </summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class CrusherPerkCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testcrusherperk", "Test specific crusher perk (testcrusherperk [level])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Crusher))
                {
                    args.Context.AddString("Crusher class not active!");
                    return;
                }

                if (args.Length < 2 || !int.TryParse(args.Args[1], out int testLevel))
                {
                    args.Context.AddString("Usage: testcrusherperk [level]");
                    args.Context.AddString("Levels: 30 (Thundering Blows test)");
                    return;
                }

                switch (testLevel)
                {
                    case 30:
                        // Test thundering blows at player position
                        var testPosition = Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 2f;
                        CrusherPerkManager.TriggerLv30_ThunderingBlows(Player.m_localPlayer, testPosition, 50f);
                        args.Context.AddString("Triggered Thundering Blows shockwave");
                        break;
                    default:
                        args.Context.AddString($"No direct test for level {testLevel}");
                        break;
                }
            }
        );

        new Terminal.ConsoleCommand("testmovespeed", "Test movespeed for player",
            delegate (Terminal.ConsoleEventArgs args)
            {
                var player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var getJogFactorMethod = typeof(Humanoid).GetMethod("GetJogSpeedFactor",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                var jogSpeedFactor = getJogFactorMethod.Invoke(player, null);

                var getRunFactorMethod = typeof(Humanoid).GetMethod("GetRunSpeedFactor",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                var runSpeedFactor = getRunFactorMethod.Invoke(player, null);

                args.Context.AddString($"Jog Speed Factor: {jogSpeedFactor}");
                args.Context.AddString($"Run Speed Factor: {runSpeedFactor}");

            }
        );

        new Terminal.ConsoleCommand("crusherstatus", "Show current crusher perk status",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null)
                {
                    args.Context.AddString("No player data found!");
                    return;
                }

                int crusherLevel = playerData.GetClassLevel(PlayerClass.Crusher);
                bool isActive = playerData.IsClassActive(PlayerClass.Crusher);

                args.Context.AddString($"=== Crusher Status ===");
                args.Context.AddString($"Class Active: {isActive}");
                args.Context.AddString($"Crusher Level: {crusherLevel}");
                args.Context.AddString("");

                args.Context.AddString("Available Perks:");
                if (crusherLevel >= 10) args.Context.AddString("✓ Lv10 - Bonebreaker: +15% blunt damage, +25% stagger");
                else args.Context.AddString("✗ Lv10 - Bonebreaker: Not unlocked");

                if (crusherLevel >= 20) args.Context.AddString("✓ Lv20 - Cold Steel: +20% weapon damage as frost");
                else args.Context.AddString("✗ Lv20 - Cold Steel: Not unlocked");

                if (crusherLevel >= 30) args.Context.AddString("✓ Lv30 - Thundering Blows: Heavy attacks create lightning shockwave");
                else args.Context.AddString("✗ Lv30 - Thundering Blows: Not unlocked");

                if (crusherLevel >= 40) args.Context.AddString("✓ Lv40 - Might of the Earth: -30% attack stamina cost");
                else args.Context.AddString("✗ Lv40 - Might of the Earth: Not unlocked");

                if (crusherLevel >= 50) args.Context.AddString("✓ Lv50 - Colossus: Ignore armor movement penalty");
                else args.Context.AddString("✗ Lv50 - Colossus: Not unlocked");

                // Show current weapon compatibility
                var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                args.Context.AddString("");
                if (currentWeapon != null)
                {
                    bool isBlunt = ClassCombatManager.IsBluntWeapon(currentWeapon);
                    args.Context.AddString($"Current weapon: {currentWeapon.m_shared.m_name}");
                    args.Context.AddString($"Weapon compatible: {(isBlunt ? "Yes (Blunt)" : "No (Not blunt)")}");
                }
                else
                {
                    args.Context.AddString("No weapon equipped");
                }

                // Show armor weight if Colossus is unlocked
                //if (crusherLevel >= 50)
                //{
                //    float armorWeight = CrusherPerkManager.GetTotalArmorWeight(Player.m_localPlayer);
                //    args.Context.AddString($"Total armor weight: {armorWeight:F1}");
                //}
            }
        );
    }
}