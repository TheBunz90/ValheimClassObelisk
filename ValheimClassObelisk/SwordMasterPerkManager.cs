using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

/// <summary>
/// Sword Master class perk system - focused on parrying, speed, and precision
/// </summary>
public static class SwordMasterPerkManager
{
    // Buff tracking for temporary effects
    private static Dictionary<long, float> riposteBuffs = new Dictionary<long, float>(); // playerID -> buff end time
    private static Dictionary<long, float> fencerFootworkBuffs = new Dictionary<long, float>(); // playerID -> movement speed buff end time

    // Configuration
    public const float RIPOSTE_DURATION = 2f;
    public const float FENCER_FOOTWORK_DURATION = 3f;

    /// <summary>
    /// Check if player has Sword Master class active and at required level
    /// </summary>
    public static bool HasSwordMasterPerk(Player player, int requiredLevel)
    {
        if (player == null) return false;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive(PlayerClass.SwordMaster)) return false;

        return playerData.GetClassLevel(PlayerClass.SwordMaster) >= requiredLevel;
    }

    #region Level 10 - Riposte Training
    /// <summary>
    /// Lv10 – Riposte Training: +10% sword damage. After you parry, your next sword hit within 2s deals +25% damage.
    /// </summary>
    public static float ApplyLv10_RiposteTrainingDamage(Player player, float baseDamage)
    {
        if (!HasSwordMasterPerk(player, 10)) return baseDamage;

        long playerID = player.GetPlayerID();
        float bonusDamage = baseDamage * 0.10f; // Base 10% sword damage bonus

        // Check for active riposte buff
        if (riposteBuffs.ContainsKey(playerID) && Time.time < riposteBuffs[playerID])
        {
            bonusDamage += baseDamage * 0.25f; // Additional 25% from riposte
            // Remove the buff after use (both internal tracking and visual effect)
            riposteBuffs.Remove(playerID);
            RemoveRiposteStatusEffect(player);
            player.Message(MessageHud.MessageType.TopLeft, "Riposte! +25% damage");
            Logger.LogInfo($"Riposte Training: Bonus riposte damage applied for {player.GetPlayerName()}");
        }

        return baseDamage + bonusDamage;
    }

    /// <summary>
    /// Trigger riposte buff when player successfully parries
    /// </summary>
    public static void TriggerRiposteBuff(Player player)
    {
        Logger.LogInfo("Apply riposte buff.");
        if (!HasSwordMasterPerk(player, 10)) return;

        long playerID = player.GetPlayerID();
        float buffEndTime = Time.time + RIPOSTE_DURATION;

        riposteBuffs[playerID] = buffEndTime;

        // Add visual status effect
        AddRiposteStatusEffect(player);

        player.Message(MessageHud.MessageType.TopLeft, "Riposte ready! Next sword hit +25% damage");
        Logger.LogInfo($"Riposte buff activated for {player.GetPlayerName()}");
    }

    /// <summary>
    /// Add visual status effect for riposte buff
    /// </summary>
    public static void AddRiposteStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            // Remove existing riposte effect if present
            RemoveRiposteStatusEffect(player);

            // Get current weapon icon
            var weapon = player.GetCurrentWeapon();
            Sprite weaponIcon = weapon?.GetIcon();

            // Fallback to a default icon if weapon has no icon
            if (weaponIcon == null)
            {
                // Try to get a sword icon from known items, or use a default
                weaponIcon = GetDefaultSwordIcon();
            }

            // Create status effect
            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_RiposteReady";
            statusEffect.m_name = "Riposte Ready";
            statusEffect.m_tooltip = "Next sword attack deals +25% damage";
            statusEffect.m_icon = weaponIcon;
            statusEffect.m_ttl = RIPOSTE_DURATION;
            statusEffect.m_startMessage = "";
            statusEffect.m_startMessageType = MessageHud.MessageType.Center;
            statusEffect.m_stopMessage = "";
            statusEffect.m_stopMessageType = MessageHud.MessageType.Center;

            // Add the status effect
            seman.AddStatusEffect(statusEffect, resetTime: true);

            Logger.LogInfo($"Added riposte visual status effect for {player.GetPlayerName()}");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding riposte status effect: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove riposte status effect
    /// </summary>
    private static void RemoveRiposteStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_RiposteReady".GetStableHashCode(), quiet: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error removing riposte status effect: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a default sword icon as fallback
    /// </summary>
    private static Sprite GetDefaultSwordIcon()
    {
        try
        {
            // Try to find a sword prefab and get its icon
            var swordPrefab = ObjectDB.instance?.GetItemPrefab("SwordBronze");
            if (swordPrefab != null)
            {
                var itemDrop = swordPrefab.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData?.GetIcon() != null)
                {
                    return itemDrop.m_itemData.GetIcon();
                }
            }

            // If that fails, try other sword types
            string[] swordNames = { "SwordIron", "SwordSilver", "SwordBlackmetal", "Knife" };
            foreach (string swordName in swordNames)
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(swordName);
                if (prefab != null)
                {
                    var itemDrop = prefab.GetComponent<ItemDrop>();
                    if (itemDrop?.m_itemData?.GetIcon() != null)
                    {
                        return itemDrop.m_itemData.GetIcon();
                    }
                }
            }

            return null; // No fallback found
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Level 20 - Dancing Steel
    /// <summary>
    /// Lv20 – Dancing Steel: 15% increased attack speed with swords
    /// Apply this when calculating attack speed
    /// </summary>
    public static float ApplyLv20_DancingSteelAttackSpeed(Player player, float baseSpeed)
    {
        if (!HasSwordMasterPerk(player, 20)) return baseSpeed;

        return baseSpeed * 1.15f; // 15% faster attack speed
    }
    #endregion

    #region Level 30 - Fencer's Footwork
    /// <summary>
    /// Lv30 – Fencer's Footwork: -15% sword attack stamina cost; +10% movement speed for 3s after hitting with a sword
    /// </summary>
    public static float ApplyLv30_FencerFootworkStamina(Player player, float staminaCost)
    {
        if (!HasSwordMasterPerk(player, 30)) return staminaCost;

        return staminaCost * 0.85f; // 15% stamina reduction
    }

    /// <summary>
    /// Apply Fencer's Footwork movement speed bonus
    /// </summary>
    public static float ApplyLv30_FencerFootworkMovementSpeed(Player player, float baseSpeed)
    {
        if (!HasSwordMasterPerk(player, 30)) return baseSpeed;

        long playerID = player.GetPlayerID();

        // Check if player has active footwork buff
        if (fencerFootworkBuffs.ContainsKey(playerID) && Time.time < fencerFootworkBuffs[playerID])
        {
            return baseSpeed * 1.10f; // 10% movement speed bonus
        }

        return baseSpeed;
    }

    /// <summary>
    /// Trigger Fencer's Footwork movement speed buff on sword hit
    /// </summary>
    public static void TriggerFencerFootworkBuff(Player player)
    {
        if (!HasSwordMasterPerk(player, 30)) return;

        long playerID = player.GetPlayerID();
        float buffEndTime = Time.time + FENCER_FOOTWORK_DURATION;

        fencerFootworkBuffs[playerID] = buffEndTime;
        // Don't show message for every hit to avoid spam
        if (Random.Range(0f, 1f) < 0.3f) // 30% chance
        {
            player.Message(MessageHud.MessageType.TopLeft, "Fencer's Footwork! +10% movement speed");
        }
        Logger.LogInfo($"Fencer's Footwork buff activated for {player.GetPlayerName()}");
    }
    #endregion

    #region Level 40 - Weakpoint Cut
    /// <summary>
    /// Lv40 – Weakpoint Cut: +15% extra damage as True Damage; +25% stagger damage vs. humanoids/undead
    /// </summary>
    public static void ApplyLv40_WeakpointCutTrueDamage(Player player, Character target, ref HitData hit)
    {
        if (!HasSwordMasterPerk(player, 40)) return;

        // Calculate 15% of original damage as true damage
        float originalTotalDamage = hit.GetTotalDamage();
        float trueDamageAmount = originalTotalDamage * 0.15f;

        // Add true damage that bypasses armor
        hit.m_damage.m_damage += trueDamageAmount;

        // Show message occasionally to indicate armor penetration
        if (Random.Range(0f, 1f) < 0.2f) // 20% chance
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Weakpoint! +{trueDamageAmount:F0} true damage");
        }

        Logger.LogInfo($"Weakpoint Cut: Added {trueDamageAmount:F1} true damage for {player.GetPlayerName()}");
    }

    /// <summary>
    /// Apply Weakpoint Cut stagger bonus against humanoids/undead
    /// </summary>
    public static float ApplyLv40_WeakpointCutStagger(Player player, Character target, float baseStagger)
    {
        if (!HasSwordMasterPerk(player, 40)) return baseStagger;

        // Check if target is humanoid or undead
        if (IsHumanoidOrUndead(target))
        {
            return baseStagger * 1.25f; // 25% more stagger damage
        }

        return baseStagger;
    }

    /// <summary>
    /// Check if character is humanoid or undead for Weakpoint Cut
    /// </summary>
    private static bool IsHumanoidOrUndead(Character character)
    {
        if (character == null) return false;

        string name = character.name.ToLower();
        return name.Contains("skeleton") || name.Contains("draugr") || name.Contains("greydwarf") ||
               name.Contains("troll") || name.Contains("fuling") || name.Contains("cultist") ||
               character is Player; // Players count as humanoids
    }
    #endregion

    #region Level 50 - Counter Attacker
    /// <summary>
    /// Lv50 – Counter Attacker: +50% Parry Bonus, +40 Block Power with all swords
    /// </summary>
    public static float ApplyLv50_CounterAttackerParryBonus(Player player, float baseParryBonus)
    {
        if (!HasSwordMasterPerk(player, 50)) return baseParryBonus;

        return baseParryBonus * 1.50f; // 50% increased parry bonus
    }

    /// <summary>
    /// Apply Counter Attacker block power bonus
    /// </summary>
    public static float ApplyLv50_CounterAttackerBlockPower(Player player, float baseBlockPower)
    {
        if (!HasSwordMasterPerk(player, 50)) return baseBlockPower;

        return baseBlockPower + 40f; // +40 flat block power
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Check if player has active riposte buff
    /// </summary>
    public static bool HasActiveRiposteBuff(long playerID)
    {
        return riposteBuffs.ContainsKey(playerID) && Time.time < riposteBuffs[playerID];
    }

    /// <summary>
    /// Check if player has active fencer's footwork buff
    /// </summary>
    public static bool HasActiveFencerFootworkBuff(long playerID)
    {
        return fencerFootworkBuffs.ContainsKey(playerID) && Time.time < fencerFootworkBuffs[playerID];
    }

    /// <summary>
    /// Clean up expired buffs
    /// </summary>
    public static void UpdateBuffs()
    {
        float currentTime = Time.time;

        // Clean up expired riposte buffs
        var expiredRiposte = riposteBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredRiposte)
        {
            riposteBuffs.Remove(playerID);
        }

        // Clean up expired fencer's footwork buffs
        var expiredFootwork = fencerFootworkBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredFootwork)
        {
            fencerFootworkBuffs.Remove(playerID);
        }
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Sword Master perks with game systems
/// </summary>
[HarmonyPatch]
public static class SwordMasterPerkPatches
{
    #region Damage Patches
    /// <summary>
    /// Apply Sword Master damage bonuses when using swords
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_SwordMaster_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only apply to sword damage
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.SwordMaster)) return;

            float originalDamage = hit.GetTotalDamage();

            // Apply Riposte Training damage bonus (Level 10)
            float modifiedDamage = SwordMasterPerkManager.ApplyLv10_RiposteTrainingDamage(player, originalDamage);

            if (modifiedDamage > originalDamage)
            {
                float multiplier = modifiedDamage / originalDamage;
                // Apply multiplier to all damage types (physical and elemental)
                hit.m_damage.m_damage *= multiplier;
                hit.m_damage.m_blunt *= multiplier;
                hit.m_damage.m_slash *= multiplier;
                hit.m_damage.m_pierce *= multiplier;
                hit.m_damage.m_chop *= multiplier;
                hit.m_damage.m_pickaxe *= multiplier;
                hit.m_damage.m_fire *= multiplier;
                hit.m_damage.m_frost *= multiplier;
                hit.m_damage.m_lightning *= multiplier;
                hit.m_damage.m_poison *= multiplier;
                hit.m_damage.m_spirit *= multiplier;
            }

            // Apply Weakpoint Cut true damage (Level 40) - this bypasses armor entirely
            SwordMasterPerkManager.ApplyLv40_WeakpointCutTrueDamage(player, __instance, ref hit);

            // Apply Weakpoint Cut stagger bonus (Level 40)
            hit.m_staggerMultiplier = SwordMasterPerkManager.ApplyLv40_WeakpointCutStagger(player, __instance, hit.m_staggerMultiplier);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_SwordMaster_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Parry Patches
    /// <summary>
    /// Trigger riposte buff when player successfully blocks/parries using Humanoid.BlockAttack
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    [HarmonyPostfix]
    public static void Humanoid_BlockAttack_SwordMaster_Postfix(Humanoid __instance, bool __result, HitData hit, Character attacker)
    {
        try
        {
            Logger.LogInfo("A block has occured.");
            if (!__result || !(__instance is Player player))
            {
                Logger.LogInfo("Block was not done by a player.");
                return; // Only proceed if block was successful and it's a player
            }

            // Check if player has sword equipped and sword master class
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon))
            {
                Logger.LogInfo("[SWORD MASTER] Not a sword!");
                return;
            }

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.SwordMaster))
            {
                Logger.LogInfo("[SWORD MASTER] Not a sword master!");
                return;
            }

            // Trigger riposte buff on successful block/parry
            SwordMasterPerkManager.TriggerRiposteBuff(player);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_BlockAttack_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Hit Trigger Patches
    /// <summary>
    /// Trigger Fencer's Footwork on sword hits
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_SwordMaster_Postfix(Character __instance, HitData hit)
    {
        try
        {
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only trigger on sword hits
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.SwordMaster)) return;

            // Trigger Fencer's Footwork movement buff
            SwordMasterPerkManager.TriggerFencerFootworkBuff(player);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Stamina Patches
    /// <summary>
    /// Apply Fencer's Footwork stamina reduction when using stamina for sword attacks
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_SwordMaster_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (__instance == null) return;

            // Check if this is a sword attack
            var weapon = __instance.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;

            // Apply Fencer's Footwork stamina reduction (Level 30)
            v = SwordMasterPerkManager.ApplyLv30_FencerFootworkStamina(__instance, v);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_SwordMaster_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Movement Speed Patches
    /// <summary>
    /// Apply Fencer's Footwork movement speed bonus
    /// </summary>
    [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")]
    [HarmonyPostfix]
    public static void Player_GetJogSpeedFactor_SwordMaster_Postfix(Player __instance, ref float __result)
    {
        try
        {
            if (__instance == null) return;

            __result = SwordMasterPerkManager.ApplyLv30_FencerFootworkMovementSpeed(__instance, __result);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_GetJogSpeedFactor_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Block Power Patches
    /// <summary>
    /// Apply Counter Attacker block power bonus - specify exact method signature
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetBlockPower", new System.Type[] { typeof(float) })]
    [HarmonyPostfix]
    public static void ItemData_GetBlockPower_SwordMaster_Postfix(ItemDrop.ItemData __instance, ref float __result, float skillFactor)
    {
        try
        {
            if (__instance == null || !ClassCombatManager.IsSwordWeapon(__instance)) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            __result = SwordMasterPerkManager.ApplyLv50_CounterAttackerBlockPower(player, __result);

        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in ItemData_GetBlockPower_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Cleanup
    /// <summary>
    /// Clean up expired buffs every 1 second
    /// </summary>
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_SwordMaster_Postfix()
    {
        try
        {
            // Piggyback on existing cleanup from other perk managers
            if (Time.time % 1f < Time.deltaTime)
            {
                SwordMasterPerkManager.UpdateBuffs();
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion
}

/// <summary>
/// Console commands for testing Sword Master perks
/// </summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class SwordMasterPerkCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testsprite", "Test sprite buff",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                SwordMasterPerkManager.AddRiposteStatusEffect(Player.m_localPlayer);
                Logger.LogInfo("[SWORD MASTER] Adding Sprite Buff");
            }
        );

        new Terminal.ConsoleCommand("testswordperk", "Test specific sword master perk (testswordperk [level])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.SwordMaster))
                {
                    args.Context.AddString("Sword Master class not active!");
                    return;
                }

                if (args.Length < 2 || !int.TryParse(args.Args[1], out int testLevel))
                {
                    args.Context.AddString("Usage: testswordperk [level]");
                    args.Context.AddString("Levels: 10, 30");
                    return;
                }

                switch (testLevel)
                {
                    case 10:
                        SwordMasterPerkManager.TriggerRiposteBuff(Player.m_localPlayer);
                        args.Context.AddString("Triggered Riposte buff (2 seconds)");
                        break;
                    case 30:
                        SwordMasterPerkManager.TriggerFencerFootworkBuff(Player.m_localPlayer);
                        args.Context.AddString("Triggered Fencer's Footwork buff (3 seconds)");
                        break;
                    default:
                        args.Context.AddString($"No direct test for level {testLevel}");
                        args.Context.AddString("Available tests: 10 (Riposte), 30 (Fencer's Footwork)");
                        break;
                }
            }
        );

        new Terminal.ConsoleCommand("swordstatus", "Show current sword master perk status",
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

                int swordLevel = playerData.GetClassLevel(PlayerClass.SwordMaster);
                bool isActive = playerData.IsClassActive(PlayerClass.SwordMaster);

                args.Context.AddString($"=== Sword Master Status ===");
                args.Context.AddString($"Class Active: {isActive}");
                args.Context.AddString($"Sword Master Level: {swordLevel}");
                args.Context.AddString("");

                args.Context.AddString("Available Perks:");
                if (swordLevel >= 10) args.Context.AddString("✓ Lv10 - Riposte Training: +10% sword damage, +25% after parry");
                else args.Context.AddString("✗ Lv10 - Riposte Training: Not unlocked");

                if (swordLevel >= 20) args.Context.AddString("✓ Lv20 - Dancing Steel: +15% attack speed with swords");
                else args.Context.AddString("✗ Lv20 - Dancing Steel: Not unlocked");

                if (swordLevel >= 30) args.Context.AddString("✓ Lv30 - Fencer's Footwork: -15% stamina, +10% speed after hits");
                else args.Context.AddString("✗ Lv30 - Fencer's Footwork: Not unlocked");

                if (swordLevel >= 40) args.Context.AddString("✓ Lv40 - Weakpoint Cut: +15% armor pen, +25% stagger vs humanoids");
                else args.Context.AddString("✗ Lv40 - Weakpoint Cut: Not unlocked");

                if (swordLevel >= 50) args.Context.AddString("✓ Lv50 - Counter Attacker: +50% parry bonus, +40 block power");
                else args.Context.AddString("✗ Lv50 - Counter Attacker: Not unlocked");

                // Show current weapon compatibility
                var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                args.Context.AddString("");
                if (currentWeapon != null)
                {
                    bool isSword = ClassCombatManager.IsSwordWeapon(currentWeapon);
                    args.Context.AddString($"Current weapon: {currentWeapon.m_shared.m_name}");
                    args.Context.AddString($"Weapon compatible: {(isSword ? "Yes (Sword)" : "No (Not a sword)")}");
                }
                else
                {
                    args.Context.AddString("No weapon equipped");
                }
            }
        );
    }
}