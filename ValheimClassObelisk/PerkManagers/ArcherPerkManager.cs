using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

/// <summary>
/// Archer class perk system - persistent perks that activate based on class selection
/// </summary>
public static class ArcherPerkManager
{
    // Buff tracking for temporary effects
    private static Dictionary<long, float> arrowSlingerBuffs = new Dictionary<long, float>(); // playerID -> buff end time

    // Track original draw durations per weapon to restore after modification
    private static Dictionary<string, float> originalDrawDurations = new Dictionary<string, float>(); // weaponName -> original duration

    // Configuration
    public const float ARROW_SLINGER_DURATION = 10f;

    /// <summary>
    /// Check if player has Archer class active and at required level
    /// </summary>
    public static bool HasArcherPerk(Player player, int requiredLevel)
    {
        if (player == null) return false;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive(PlayerClass.Archer)) return false;

        return playerData.GetClassLevel(PlayerClass.Archer) >= requiredLevel;
    }

    #region Level 10 - Steady Draw
    /// <summary>
    /// Lv10 – Steady Draw: -15% stamina drain while drawing bows
    /// Apply this modifier when using stamina (checked per use)
    /// </summary>
    public static float ApplyLv10_SteadyDrawStamina(Player player, float staminaCost)
    {
        if (!HasArcherPerk(player, 10)) return staminaCost;

        // Only apply if player is drawing a bow
        if (player.IsDrawingBow())
        {
            return staminaCost * 0.85f; // 15% reduction
        }

        return staminaCost;
    }
    #endregion

    #region Level 20 - Arrow Slinger
    /// <summary>
    /// Store original draw duration and apply Arrow Slinger buff if active
    /// </summary>
    public static void ApplyArrowSlingerDrawSpeed(Player player, ItemDrop.ItemData weapon)
    {
        if (!HasArcherPerk(player, 20) || weapon?.m_shared?.m_attack == null) return;

        string weaponKey = weapon.m_shared.m_name;
        var attack = weapon.m_shared.m_attack;

        // Store original duration if not already stored
        if (!originalDrawDurations.ContainsKey(weaponKey))
        {
            originalDrawDurations[weaponKey] = attack.m_drawDurationMin;
        }

        long playerID = player.GetPlayerID();

        // Apply buff if active
        if (arrowSlingerBuffs.ContainsKey(playerID) && Time.time < arrowSlingerBuffs[playerID])
        {
            attack.m_drawDurationMin = originalDrawDurations[weaponKey] * 0.5f; // 50% faster
        }
        else
        {
            // Restore original duration if no buff
            attack.m_drawDurationMin = originalDrawDurations[weaponKey];
        }
    }

    /// <summary>
    /// Restore original draw duration after shot
    /// </summary>
    public static void RestoreDrawDuration(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared?.m_attack == null) return;

        string weaponKey = weapon.m_shared.m_name;
        if (originalDrawDurations.ContainsKey(weaponKey))
        {
            weapon.m_shared.m_attack.m_drawDurationMin = originalDrawDurations[weaponKey];
        }
    }

    /// <summary>
    /// Trigger Arrow Slinger buff when arrow hits target
    /// </summary>
    public static void TriggerArrowSlingerBuff(Player archer)
    {
        if (!HasArcherPerk(archer, 20)) return;

        long playerID = archer.GetPlayerID();
        float buffEndTime = Time.time + ARROW_SLINGER_DURATION;

        // Extend existing buff or create new one
        if (!arrowSlingerBuffs.ContainsKey(playerID) || arrowSlingerBuffs[playerID] < buffEndTime)
        {
            arrowSlingerBuffs[playerID] = buffEndTime;

            // Add visual status effect
            AddArrowSlingerStatusEffect(archer);

            archer.Message(MessageHud.MessageType.TopLeft, "Arrow Slinger: 50% faster draw for 10s!");
            Debug.Log($"Triggered Arrow Slinger buff for {archer.GetPlayerName()}");
        }
    }

    /// <summary>
    /// Add visual status effect for Arrow Slinger buff
    /// </summary>
    public static void AddArrowSlingerStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            // Remove existing arrow slinger effect if present
            RemoveArrowSlingerStatusEffect(player);

            // Get current weapon icon (bow)
            var weapon = player.GetCurrentWeapon();
            Sprite weaponIcon = weapon?.GetIcon();

            // Fallback to a default icon if weapon has no icon
            if (weaponIcon == null)
            {
                weaponIcon = GetDefaultBowIcon();
            }

            // Create status effect
            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_ArrowSlinger";
            statusEffect.m_name = "Arrow Slinger";
            statusEffect.m_tooltip = "Draw speed increased by 50%";
            statusEffect.m_icon = weaponIcon;
            statusEffect.m_ttl = ARROW_SLINGER_DURATION;
            statusEffect.m_startMessage = "";
            statusEffect.m_startMessageType = MessageHud.MessageType.Center;
            statusEffect.m_stopMessage = "";
            statusEffect.m_stopMessageType = MessageHud.MessageType.Center;

            // Add the status effect
            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding Arrow Slinger status effect: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove Arrow Slinger status effect
    /// </summary>
    private static void RemoveArrowSlingerStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_ArrowSlinger".GetStableHashCode(), quiet: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error removing Arrow Slinger status effect: {ex.Message}");
        }
    }

    /// <summary>
    /// Get a default bow icon as fallback
    /// </summary>
    private static Sprite GetDefaultBowIcon()
    {
        try
        {
            // Try to find a bow prefab and get its icon
            string[] bowNames = { "Bow", "BowFineWood", "BowHuntsman", "BowDraugrFang", "CrossbowArbalest" };
            foreach (string bowName in bowNames)
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(bowName);
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

    /// <summary>
    /// Check if player has active Arrow Slinger buff (for external access)
    /// </summary>
    public static bool HasActiveArrowSlingerBuff(long playerID)
    {
        return arrowSlingerBuffs.ContainsKey(playerID) && Time.time < arrowSlingerBuffs[playerID];
    }
    #endregion

    #region Level 30 - Wind Reader
    /// <summary>
    /// Lv30 – Wind Reader: +15% damage beyond 25m travel distance; -25% stamina while aiming
    /// Apply damage bonus when calculating projectile damage
    /// </summary>
    public static float ApplyLv30_WindReaderDamage(Player archer, Vector3 shotOrigin, Vector3 hitPoint, float baseDamage)
    {
        if (!HasArcherPerk(archer, 30)) return baseDamage;

        // Calculate travel distance
        float distance = Vector3.Distance(shotOrigin, hitPoint);

        if (distance > 25f)
        {
            float bonusDamage = baseDamage * 0.15f; // 15% bonus
            Debug.Log($"Wind Reader: Long-range shot ({distance:F1}m) +15% damage (+{bonusDamage:F1})");

            // Show message occasionally
            if (Random.Range(0f, 1f) < 0.2f)
            {
                archer.Message(MessageHud.MessageType.TopLeft, $"Long Shot! +15% damage ({distance:F0}m)");
            }

            return baseDamage + bonusDamage;
        }

        return baseDamage;
    }

    /// <summary>
    /// Apply Wind Reader stamina reduction while aiming (checked when using stamina)
    /// </summary>
    public static float ApplyLv30_WindReaderStamina(Player archer, float staminaDrain)
    {
        if (!HasArcherPerk(archer, 30)) return staminaDrain;

        // Only apply if player is aiming (drawing bow)
        if (archer.IsDrawingBow())
        {
            return staminaDrain * 0.75f; // 25% reduction
        }

        return staminaDrain;
    }
    #endregion

    #region Level 40 - Magic Shot
    /// <summary>
    /// Lv40 – Magic Shot: 50% chance to not consume an arrow on attack
    /// Check this when consuming ammunition
    /// </summary>
    public static bool ShouldConsumeArrow(Player archer)
    {
        if (!HasArcherPerk(archer, 40)) return true;

        // 50% chance to not consume arrow
        if (Random.Range(0f, 1f) < 0.5f)
        {
            archer.Message(MessageHud.MessageType.TopLeft, "Magic Shot! Arrow not consumed");
            Debug.Log($"Magic Shot triggered for {archer.GetPlayerName()} - arrow not consumed");
            return false;
        }

        return true;
    }
    #endregion

    #region Level 50 - Adrenaline Rush
    /// <summary>
    /// Lv50 – Adrenaline Rush: Arrow hits return 5% stamina
    /// Simple stamina restoration on every hit
    /// </summary>
    public static void TriggerAdrenalineRush(Player archer)
    {
        if (!HasArcherPerk(archer, 50)) return;

        // Restore stamina (5% of max stamina per hit)
        float maxStamina = archer.GetMaxStamina();
        float staminaRestore = maxStamina * 0.05f;

        archer.AddStamina(staminaRestore);

        // Show message occasionally to avoid spam
        if (Random.Range(0f, 1f) < 0.3f) // 30% chance
        {
            archer.Message(MessageHud.MessageType.TopLeft, $"Adrenaline Rush! +{staminaRestore:F0} stamina");
        }

        Debug.Log($"Adrenaline Rush: Restored {staminaRestore:F1} stamina for {archer.GetPlayerName()}");
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Clean up expired buffs
    /// </summary>
    public static void UpdateBuffs()
    {
        float currentTime = Time.time;

        // Clean up expired Arrow Slinger buffs
        var expiredBuffs = arrowSlingerBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredBuffs)
        {
            arrowSlingerBuffs.Remove(playerID);

            // Remove visual status effect from the player if they're still in game
            var player = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == playerID);
            if (player != null)
            {
                RemoveArrowSlingerStatusEffect(player);
            }

            Debug.Log($"Arrow Slinger buff expired for player {playerID}");
        }
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Archer perks with game systems
/// </summary>
[HarmonyPatch]
public static class ArcherPerkPatches
{
    #region Bow Draw Patches
    /// <summary>
    /// Apply Arrow Slinger draw speed when bow is being drawn
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateAttackBowDraw")]
    [HarmonyPrefix]
    public static void Player_UpdateAttackBowDraw_Prefix(Player __instance, ItemDrop.ItemData weapon, float dt)
    {
        try
        {
            if (__instance == null || weapon == null || !ClassCombatManager.IsBowWeapon(weapon)) return;

            // Apply Arrow Slinger buff to draw speed
            ArcherPerkManager.ApplyArrowSlingerDrawSpeed(__instance, weapon);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UpdateAttackBowDraw_Prefix (Archer): {ex.Message}");
        }
    }
    #endregion

    #region Projectile Hit Patches
    /// <summary>
    /// Trigger archer perks when projectiles hit targets and restore draw duration
    /// </summary>
    [HarmonyPatch(typeof(Projectile), "OnHit")]
    [HarmonyPrefix]
    public static void Projectile_OnHit_Prefix(Projectile __instance, Collider collider, Vector3 hitPoint)
    {
        try
        {
            if (__instance == null || collider == null) return;

            // Check if this is an arrow/bolt hitting a valid target
            var hitCharacter = collider.GetComponent<Character>();
            if (hitCharacter == null || hitCharacter is Player) return;

            // Find the archer who fired this projectile
            var archer = FindProjectileOwner(__instance);
            if (archer == null) return;

            // Only trigger for players with Archer class active
            var playerData = PlayerClassManager.GetPlayerData(archer);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Archer)) return;

            // Restore draw duration after shot (clean slate for next shot)
            var currentWeapon = archer.GetCurrentWeapon();
            if (ClassCombatManager.IsBowWeapon(currentWeapon))
            {
                ArcherPerkManager.RestoreDrawDuration(currentWeapon);
            }

            // Trigger Arrow Slinger buff (Level 20)
            ArcherPerkManager.TriggerArrowSlingerBuff(archer);

            // Trigger Adrenaline Rush (Level 50)
            ArcherPerkManager.TriggerAdrenalineRush(archer);

            Debug.Log($"Archer perk triggers for {archer.GetPlayerName()} hitting {hitCharacter.name}");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_OnHit_Prefix (Archer): {ex.Message}");
        }
    }

    /// <summary>
    /// Apply Wind Reader damage bonus to projectile hits
    /// Only applies when using bows/crossbows
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Prefix_ArcherPerks(Character __instance, ref HitData hit)
    {
        try
        {
            if (!(hit.GetAttacker() is Player archer) || __instance == null || __instance is Player) return;

            // Only apply to projectile damage from bows
            if (!IsProjectileDamage(hit)) return;

            // Check if archer is using a bow weapon and has Archer class active
            var currentWeapon = archer.GetCurrentWeapon();
            if (!ClassCombatManager.IsBowWeapon(currentWeapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(archer);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Archer)) return;

            // Apply Wind Reader damage bonus
            float originalDamage = hit.GetTotalDamage();
            Vector3 shotOrigin = archer.transform.position;
            Vector3 hitPoint = hit.m_point;

            float modifiedDamage = ArcherPerkManager.ApplyLv30_WindReaderDamage(archer, shotOrigin, hitPoint, originalDamage);

            if (modifiedDamage > originalDamage)
            {
                float multiplier = modifiedDamage / originalDamage;
                // Apply multiplier to all damage types
                hit.m_damage.m_damage *= multiplier;
                hit.m_damage.m_pierce *= multiplier;
                hit.m_damage.m_slash *= multiplier;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Prefix_ArcherPerks: {ex.Message}");
        }
    }
    #endregion

    #region Stamina Patches
    /// <summary>
    /// Apply Archer stamina reduction perks when using stamina
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (__instance == null) return;

            // Apply Steady Draw stamina reduction (Level 10)
            v = ArcherPerkManager.ApplyLv10_SteadyDrawStamina(__instance, v);

            // Apply Wind Reader stamina reduction (Level 30)
            v = ArcherPerkManager.ApplyLv30_WindReaderStamina(__instance, v);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_Prefix (Archer): {ex.Message}");
        }
    }
    #endregion

    #region Periodic Cleanup
    // Track last cleanup time to ensure consistent intervals
    private static float lastCleanupTime = 0f;

    /// <summary>
    /// Clean up expired buffs every 1 second, regardless of framerate
    /// </summary>
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Postfix()
    {
        try
        {
            // Clean up buffs every 1 second
            if (Time.time - lastCleanupTime >= 1f)
            {
                lastCleanupTime = Time.time;
                ArcherPerkManager.UpdateBuffs();
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Postfix (Archer cleanup): {ex.Message}");
        }
    }
    #endregion

    #region Helper Methods
    /// <summary>
    /// Find the player who owns a projectile
    /// </summary>
    private static Player FindProjectileOwner(Projectile projectile)
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
            if (localPlayer != null && Vector3.Distance(localPlayer.transform.position, projectile.transform.position) < 100f)
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

    /// <summary>
    /// Check if hit data represents projectile damage
    /// </summary>
    private static bool IsProjectileDamage(HitData hit)
    {
        // Check for projectile-related damage types or hit sources
        return hit.m_skill == Skills.SkillType.Bows ||
               hit.m_skill == Skills.SkillType.Crossbows ||
               (hit.m_damage.m_pierce > 0 && hit.m_damage.m_blunt == 0 && hit.m_damage.m_slash == 0);
    }
    #endregion

    #region Magic Shot Implementation
    /// <summary>
    /// Simplified Magic Shot implementation - just add to existing stacks (allow temporary overflow)
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "RemoveItem", new System.Type[] { typeof(ItemDrop.ItemData), typeof(int) })]
    [HarmonyPrefix]
    public static void Inventory_RemoveItem_MagicShot_PreFix(Inventory __instance, ItemDrop.ItemData item, int amount)
    {
        try
        {
            // Only proceed if this is an arrow/bolt being removed
            if (item == null || !IsArrowItem(item)) return;

            // Find the player who owns this inventory
            var player = FindPlayerWithInventory(__instance);
            if (player == null) return;

            // Check if player is using a bow and has Magic Shot perk
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBowWeapon(weapon)) return;

            if (ArcherPerkManager.HasArcherPerk(player, 40))
            {
                // Check if Magic Shot should trigger (50% chance)
                if (Random.Range(0f, 1f) < 0.5f)
                {
                    // Find the smallest existing stack of this item type
                    var existingStacks = __instance.GetAllItems()
                        .Where(i => i.m_shared.m_name == item.m_shared.m_name)
                        .OrderBy(i => i.m_stack)
                        .ToList();

                    if (existingStacks.Count > 0)
                    {
                        // Add to the smallest stack (even if it goes over max - the removal will balance it)
                        var smallestStack = existingStacks.First();
                        smallestStack.m_stack += amount;

                        player.Message(MessageHud.MessageType.TopLeft, "Magic Shot! Arrow not consumed");
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Inventory_RemoveItem_MagicShot_PreFix: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper method to check if item is an arrow/bolt
    /// </summary>
    private static bool IsArrowItem(ItemDrop.ItemData item)
    {
        if (item?.m_shared == null) return false;

        return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
               item.m_shared.m_name.ToLower().Contains("arrow") ||
               item.m_shared.m_name.ToLower().Contains("bolt");
    }

    private static Player FindPlayerWithInventory(Inventory inventory)
    {
        var allPlayers = Player.GetAllPlayers();
        return allPlayers.FirstOrDefault(p => p.GetInventory() == inventory);
    }
    #endregion
}