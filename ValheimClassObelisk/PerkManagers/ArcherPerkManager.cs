using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

/// <summary>
/// Archer class perk system - focused on bows and crossbows, split by
/// ItemDrop.ItemData.SharedData.m_skillType (Bows vs. Crossbows) for Combat Rhythm.
/// </summary>
public static class ArcherPerkManager
{
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

    // Description metadata, shown in the class selection GUI - locked perks display as "???"
    private const string Intro = "Expert marksmen with disciplined bow technique and practical crossbow mastery.";
    private const string Outro = "Best for players who want ranged weapons to feel steady early, distinct by weapon type mid-game, and deadly at mastery.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Practiced Aim", Description = "+7% bow and crossbow damage." },
        new PerkInfo { RequiredLevel = 20, Name = "Magic Shot", Description = "25% chance to not consume arrows or bolts." },
        new PerkInfo { RequiredLevel = 30, Name = "Combat Rhythm", Description = "Bow hits grant Arrow Slinger for 6s, reducing draw time by 25%. Crossbow hits grant Quick Crank for 6s, reducing reload time by 25%." },
        new PerkInfo { RequiredLevel = 40, Name = "Storm Fletching", Description = "Bow and crossbow attacks deal bonus lightning damage equal to 12% of weapon damage." },
        new PerkInfo { RequiredLevel = 50, Name = "Deadeye", Description = "+15% bow and crossbow damage. Hits beyond 25m gain an additional +10% damage." },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Archer) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 30 - Combat Rhythm
    public const float COMBAT_RHYTHM_DURATION = 10f;
    public const float COMBAT_RHYTHM_REDUCTION = 0.25f;

    // Separate per-player expiry tracking for the two branches - a player could in principle
    // swap weapons mid-buff, so these are independent rather than a single shared timer.
    private static readonly Dictionary<Player, float> bowBuffExpire = new Dictionary<Player, float>();
    private static readonly Dictionary<Player, float> crossbowBuffExpire = new Dictionary<Player, float>();

    public static void TriggerArrowSlinger(Player player)
    {
        bowBuffExpire[player] = Time.time + COMBAT_RHYTHM_DURATION;
        ApplyCombatRhythmStatusEffect(player, "SE_ArrowSlinger", "Arrow Slinger", "Draw speed increased by 25%");
        player.Message(MessageHud.MessageType.TopLeft, $"Arrow Slinger: 25% faster draw for {COMBAT_RHYTHM_DURATION:F0}s!");
    }

    public static void TriggerQuickCrank(Player player)
    {
        crossbowBuffExpire[player] = Time.time + COMBAT_RHYTHM_DURATION;
        ApplyCombatRhythmStatusEffect(player, "SE_QuickCrank", "Quick Crank", "Reload speed increased by 25%");
        DevLog.Log($"[QuickCrank] Triggered for {player.GetPlayerName()} at t={Time.time:F2}, expires at {Time.time + COMBAT_RHYTHM_DURATION:F2}");
        player.Message(MessageHud.MessageType.TopLeft, $"Quick Crank: 25% faster reload for {COMBAT_RHYTHM_DURATION:F0}s!");
    }

    /// <summary>
    /// Visual status effect for the active Combat Rhythm buff, using the player's currently
    /// equipped weapon icon (matching the pattern already used for Sword Master's Riposte Ready
    /// and Brawler's Rage).
    /// </summary>
    private static void ApplyCombatRhythmStatusEffect(Player player, string effectName, string displayName, string tooltip)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect(effectName.GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = effectName;
            statusEffect.m_name = displayName;
            statusEffect.m_tooltip = tooltip;
            statusEffect.m_icon = player.GetCurrentWeapon()?.GetIcon();
            statusEffect.m_ttl = COMBAT_RHYTHM_DURATION;
            statusEffect.m_startMessage = "";
            statusEffect.m_startMessageType = MessageHud.MessageType.Center;
            statusEffect.m_stopMessage = "";
            statusEffect.m_stopMessageType = MessageHud.MessageType.Center;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding Combat Rhythm status effect ({effectName}): {ex.Message}");
        }
    }

    public static bool HasArrowSlingerActive(Player player)
    {
        return player != null && bowBuffExpire.TryGetValue(player, out var expire) && Time.time < expire;
    }

    public static bool HasQuickCrankActive(Player player)
    {
        return player != null && crossbowBuffExpire.TryGetValue(player, out var expire) && Time.time < expire;
    }

    public static void UpdateBuffs()
    {
        float currentTime = Time.time;

        var expiredBow = bowBuffExpire.Where(kvp => kvp.Key == null || currentTime >= kvp.Value).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredBow) bowBuffExpire.Remove(player);

        var expiredCrossbow = crossbowBuffExpire.Where(kvp => kvp.Key == null || currentTime >= kvp.Value).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredCrossbow) crossbowBuffExpire.Remove(player);
    }
    #endregion

    #region Level 40 - Storm Fletching
    public const float STORM_FLETCHING_LIGHTNING_PERCENT = 0.12f;

    public static void ApplyStormFletchingLightning(ref HitData hit, float weaponDamage)
    {
        hit.m_damage.m_lightning += weaponDamage * STORM_FLETCHING_LIGHTNING_PERCENT;
    }
    #endregion

    #region Level 50 - Deadeye
    public const float DEADEYE_RANGE_BONUS = 0.10f;
    public const float DEADEYE_RANGE_THRESHOLD = 25f;
    #endregion
}

/// <summary>
/// Harmony patches to integrate Archer perks with game systems
/// </summary>
[HarmonyPatch]
public static class ArcherPerkPatches
{
    #region Damage Patches
    /// <summary>
    /// Apply Storm Fletching's instant lightning bonus (Level 40) and Deadeye's conditional
    /// beyond-25m bonus (Level 50). Practiced Aim's flat +7% (Level 10) and Deadeye's flat +15%
    /// (Level 50) live only in ClassCombatManager.GetArcherDamageBonus - not duplicated here.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Archer_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBowWeapon(weapon)) return;
            if (!ArcherPerkManager.HasArcherPerk(player, 1)) return;

            float originalDamage = hit.GetTotalDamage();

            if (ArcherPerkManager.HasArcherPerk(player, 40))
            {
                ArcherPerkManager.ApplyStormFletchingLightning(ref hit, originalDamage);
            }

            if (ArcherPerkManager.HasArcherPerk(player, 50))
            {
                float distance = Vector3.Distance(player.transform.position, hit.m_point);
                if (distance > ArcherPerkManager.DEADEYE_RANGE_THRESHOLD)
                {
                    float multiplier = 1f + ArcherPerkManager.DEADEYE_RANGE_BONUS;
                    hit.m_damage.m_damage *= multiplier;
                    hit.m_damage.m_blunt *= multiplier;
                    hit.m_damage.m_slash *= multiplier;
                    hit.m_damage.m_pierce *= multiplier;
                    hit.m_damage.m_chop *= multiplier;
                    hit.m_damage.m_fire *= multiplier;
                    hit.m_damage.m_frost *= multiplier;
                    hit.m_damage.m_lightning *= multiplier;
                    hit.m_damage.m_poison *= multiplier;
                    hit.m_damage.m_spirit *= multiplier;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Archer_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Trigger Combat Rhythm's bow/crossbow buff (Level 30) after a successful hit.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Archer_Postfix(Character __instance, HitData hit)
    {
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
            if (hit.GetTotalDamage() <= 0) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBowWeapon(weapon)) return;
            if (!ArcherPerkManager.HasArcherPerk(player, 30)) return;

            if (weapon.m_shared.m_skillType == Skills.SkillType.Crossbows)
            {
                ArcherPerkManager.TriggerQuickCrank(player);
            }
            else
            {
                ArcherPerkManager.TriggerArrowSlinger(player);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Archer_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Combat Rhythm Patches
    /// <summary>
    /// Arrow Slinger's non-mutating draw-speed boost: scales the draw-completion percentage up
    /// directly, rather than mutating the shared weapon.m_shared.m_attack.m_drawDurationMin field
    /// (the old approach, which leaked the buff to any other player wielding the same bow model).
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "GetAttackDrawPercentage")]
    [HarmonyPostfix]
    public static void Humanoid_GetAttackDrawPercentage_Archer_Postfix(Humanoid __instance, ref float __result)
    {
        try
        {
            if (__result <= 0f || !(__instance is Player player)) return;
            if (!ArcherPerkManager.HasArrowSlingerActive(player)) return;

            __result = Mathf.Clamp01(__result / (1f - ArcherPerkManager.COMBAT_RHYTHM_REDUCTION));
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_GetAttackDrawPercentage_Archer_Postfix: {ex.Message}");
        }
    }

    /// <summary>
    /// Quick Crank's reload-speed boost. The straightforward approach - scaling
    /// ItemDrop.ItemData.GetWeaponLoadingTime()'s result - doesn't work: that duration is baked
    /// into the queued MinorActionData exactly once, the instant a bolt is fired (confirmed via
    /// logging - the reload gets queued essentially simultaneously with, and just before, the hit
    /// that would trigger the buff), so the buff is never active yet when it matters. Instead,
    /// this speeds up the *live* in-progress reload directly: Player.UpdateActionQueue advances
    /// the current action via `m_time += dt` every FixedUpdate tick, so scaling up dt while a
    /// Reload action is at the head of the queue and Quick Crank is active works regardless of
    /// when that reload happened to be queued relative to the buff turning on.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UpdateActionQueue")]
    [HarmonyPrefix]
    public static void Player_UpdateActionQueue_Archer_Prefix(Player __instance, ref float dt, List<Player.MinorActionData> ___m_actionQueue)
    {
        try
        {
            if (___m_actionQueue.Count == 0) return;

            var current = ___m_actionQueue[0];
            if (current.m_type != Player.MinorActionData.ActionType.Reload) return;
            if (!ArcherPerkManager.HasQuickCrankActive(__instance)) return;

            float before = dt;
            dt /= 1f - ArcherPerkManager.COMBAT_RHYTHM_REDUCTION;

            // Only log once per reload (right as it starts) rather than every tick.
            if (current.m_time <= 0f)
            {
                DevLog.Log($"[QuickCrank] Speeding up in-progress reload at t={Time.time:F2}: dt {before:F3} -> {dt:F3}, duration={current.m_duration:F2}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UpdateActionQueue_Archer_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Magic Shot
    /// <summary>
    /// Magic Shot (Level 20): 25% chance to not consume arrows/bolts.
    /// </summary>
    [HarmonyPatch(typeof(Inventory), "RemoveItem", new System.Type[] { typeof(ItemDrop.ItemData), typeof(int) })]
    [HarmonyPrefix]
    public static void Inventory_RemoveItem_MagicShot_PreFix(Inventory __instance, ItemDrop.ItemData item, int amount)
    {
        try
        {
            if (item == null || !IsArrowItem(item)) return;

            var player = FindPlayerWithInventory(__instance);
            if (player == null) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBowWeapon(weapon)) return;
            if (!ArcherPerkManager.HasArcherPerk(player, 20)) return;

            if (Random.Range(0f, 1f) < 0.25f)
            {
                var existingStacks = __instance.GetAllItems()
                    .Where(i => i.m_shared.m_name == item.m_shared.m_name)
                    .OrderBy(i => i.m_stack)
                    .ToList();

                if (existingStacks.Count > 0)
                {
                    var smallestStack = existingStacks.First();
                    smallestStack.m_stack += amount;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Inventory_RemoveItem_MagicShot_PreFix: {ex.Message}");
        }
    }

    private static bool IsArrowItem(ItemDrop.ItemData item)
    {
        if (item?.m_shared == null) return false;

        return item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
               item.m_shared.m_name.ToLower().Contains("arrow") ||
               item.m_shared.m_name.ToLower().Contains("bolt");
    }

    private static Player FindPlayerWithInventory(Inventory inventory)
    {
        return Player.GetAllPlayers().FirstOrDefault(p => p.GetInventory() == inventory);
    }
    #endregion

    #region Periodic Cleanup
    private static float lastCleanupTime = 0f;

    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Archer_Postfix()
    {
        try
        {
            if (Time.time - lastCleanupTime >= 1f)
            {
                lastCleanupTime = Time.time;
                ArcherPerkManager.UpdateBuffs();
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Archer_Postfix: {ex.Message}");
        }
    }
    #endregion
}
