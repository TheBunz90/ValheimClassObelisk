using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using Logger = Jotunn.Logger;

/// <summary>
/// Axemaster class perk system - focused on one-handed axes and battleaxes, bleed damage,
/// and finishing power against weakened enemies. Axes previously lived under Sword Master
/// (see ClassCombatManager.IsSwordWeapon's history); this is their own class now.
/// </summary>
public static class AxemasterPerkManager
{
    public static bool HasAxemasterPerk(Player player, int requiredLevel)
    {
        if (player == null) return false;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive(PlayerClass.Axemaster)) return false;

        return playerData.GetClassLevel(PlayerClass.Axemaster) >= requiredLevel;
    }

    // Description metadata, shown in the class selection GUI - locked perks display as "???"
    private const string Intro = "Axe specialists who trade finesse for brutal chopping power, wounds, and relentless finishing pressure.";
    private const string Outro = "Best for players who want axes to become their own class identity instead of living under Sword Master.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Chopper's Training", Description = "+8% axe damage with one-handed axes and battleaxes." },
        new PerkInfo { RequiredLevel = 20, Name = "Woodsman's Carry", Description = "Axes weigh 50% less and impose no movement speed penalties." },
        new PerkInfo { RequiredLevel = 30, Name = "Rending Rhythm", Description = "One-handed axe hits against the same target build up to 3 stacks; each stack grants +5% damage to that target for 5s. Battleaxe special attacks apply all 3 stacks at once." },
        new PerkInfo { RequiredLevel = 40, Name = "Hemorrhage", Description = "Axe attacks apply bleed, dealing slash damage over time equal to 12% of weapon damage over 5s. Reapplying bleed refreshes the duration." },
        new PerkInfo { RequiredLevel = 50, Name = "Executioner", Description = "+15% axe damage. Axe attacks deal an additional +10% damage against enemies below 40% health." },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Axemaster) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 30 - Rending Rhythm
    private const float RENDING_RHYTHM_DURATION = 5f;
    private const float RENDING_RHYTHM_PER_STACK = 0.05f;
    private const int RENDING_RHYTHM_MAX_STACKS = 3;

    private class RendingRhythmData
    {
        public int stacks = 0;
        public float expireTime = 0f;
    }

    // Keyed by target (per-target-safe, matching the pattern already used for Assassin's
    // poison tracking) rather than a module-level flag shared across every player/target pair.
    private static readonly Dictionary<Character, RendingRhythmData> rendingRhythmStacks = new Dictionary<Character, RendingRhythmData>();

    private static float GetRendingRhythmBonus(Character target)
    {
        if (target == null) return 0f;
        if (!rendingRhythmStacks.TryGetValue(target, out var data)) return 0f;
        if (Time.time >= data.expireTime) return 0f;

        return data.stacks * RENDING_RHYTHM_PER_STACK;
    }

    private static void AddRendingRhythmStack(Character target, bool applyAllStacks)
    {
        if (target == null) return;

        if (!rendingRhythmStacks.TryGetValue(target, out var data) || Time.time >= data.expireTime)
        {
            data = new RendingRhythmData();
            rendingRhythmStacks[target] = data;
        }

        data.stacks = applyAllStacks ? RENDING_RHYTHM_MAX_STACKS : Mathf.Min(data.stacks + 1, RENDING_RHYTHM_MAX_STACKS);
        data.expireTime = Time.time + RENDING_RHYTHM_DURATION;
    }
    #endregion

    #region Level 40 - Hemorrhage
    private const float HEMORRHAGE_DURATION = 5f;
    private const float HEMORRHAGE_PERCENT = 0.12f;
    private const int HEMORRHAGE_TICKS = 5;

    private class BleedData
    {
        public float totalDamage = 0f;
        public float damagePerTick = 0f;
        public float expireTime = 0f;
        public float lastTickTime = 0f;
        public Player source = null;
    }

    // Vanilla has no bleed/slash-over-time handler equivalent to Character.AddPoisonDamage -
    // RPC_Damage only special-cases poison/fire/spirit, not slash - so this has to be
    // self-ticked, unlike Assassin's poison (which was fixed earlier to hand off to vanilla's
    // own SE_Poison instead of fighting it). There's nothing to hand off to here.
    private static readonly Dictionary<Character, BleedData> bleedTracking = new Dictionary<Character, BleedData>();

    private static void ApplyHemorrhage(Player player, Character target, float hitDamage)
    {
        if (!HasAxemasterPerk(player, 40) || target == null || target.IsDead()) return;

        float bleedTotal = hitDamage * HEMORRHAGE_PERCENT;
        if (bleedTotal <= 0f) return;

        if (!bleedTracking.TryGetValue(target, out var bleed) || Time.time >= bleed.expireTime)
        {
            bleed = new BleedData();
            bleedTracking[target] = bleed;
        }

        // Reapplying refreshes the duration - keep whichever total deals more overall, mirroring
        // vanilla's own SE_Poison "replace if greater" convention rather than stacking totals.
        bleed.totalDamage = Mathf.Max(bleed.totalDamage, bleedTotal);
        bleed.damagePerTick = bleed.totalDamage / HEMORRHAGE_TICKS;
        bleed.expireTime = Time.time + HEMORRHAGE_DURATION;
        bleed.source = player;
    }

    private static void ProcessHemorrhageTicks()
    {
        float currentTime = Time.time;
        var toRemove = new List<Character>();

        foreach (var kvp in bleedTracking)
        {
            var target = kvp.Key;
            var bleed = kvp.Value;

            if (target == null || target.IsDead() || currentTime > bleed.expireTime)
            {
                toRemove.Add(target);
                continue;
            }

            if (currentTime - bleed.lastTickTime >= 1f)
            {
                bleed.lastTickTime = currentTime;

                HitData bleedHit = new HitData();
                bleedHit.m_damage.m_slash = bleed.damagePerTick;
                bleedHit.m_attacker = bleed.source?.GetZDOID() ?? ZDOID.None;
                bleedHit.m_point = target.transform.position;
                bleedHit.m_hitType = HitData.HitType.Poisoned; // closest vanilla "DoT tick" hit type
                // m_skill intentionally left at its default (None) - marks this as a synthetic
                // DoT tick so ClassCombatManager's and this file's own damage-bonus patches skip
                // it, instead of re-multiplying/re-stacking off of the bleed's own damage.

                target.Damage(bleedHit);
            }
        }

        foreach (var target in toRemove)
        {
            bleedTracking.Remove(target);
        }
    }
    #endregion

    #region Level 20 - Woodsman's Carry
    /// <summary>
    /// Cancels an equipped axe's own movement-speed penalty without touching the item's shared
    /// data (which would leak the change to every player using that item type) - only offsets
    /// the axe's own contribution to this specific player's equipment movement modifier.
    /// </summary>
    [HarmonyPatch(typeof(Player), "GetEquipmentMovementModifier")]
    [HarmonyPostfix]
    public static void Player_GetEquipmentMovementModifier_Axemaster_Postfix(Player __instance, ref float __result)
    {
        try
        {
            if (!HasAxemasterPerk(__instance, 20)) return;

            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsAxeWeapon(weapon)) return;

            float weaponPenalty = weapon.m_shared.m_movementModifier;
            if (weaponPenalty < 0f) __result -= weaponPenalty;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_GetEquipmentMovementModifier_Axemaster_Postfix: {ex.Message}");
        }
    }

    /// <summary>
    /// Halves an axe's carry weight for the local player only (encumbrance is evaluated
    /// client-side) - reads a per-instance computed value rather than mutating shared item data.
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetWeight")]
    [HarmonyPostfix]
    public static void ItemData_GetWeight_Axemaster_Postfix(ItemDrop.ItemData __instance, ref float __result)
    {
        try
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || !HasAxemasterPerk(localPlayer, 20)) return;
            if (!ClassCombatManager.IsAxeWeapon(__instance)) return;

            __result *= 0.5f;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in ItemData_GetWeight_Axemaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Attack Type Tracking
    // Tracks whether each player's current attack is a battleaxe secondary/special attack, so
    // the damage patch below knows whether to add one Rending Rhythm stack or apply all three
    // at once. Per-player dictionary (not a module-level flag) so this is multiplayer-safe.
    private static readonly Dictionary<Player, bool> pendingTwoHandedSpecialAttack = new Dictionary<Player, bool>();

    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    [HarmonyPrefix]
    public static void Humanoid_StartAttack_Axemaster_Prefix(Humanoid __instance, bool secondaryAttack)
    {
        try
        {
            if (!(__instance is Player player)) return;

            var weapon = player.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsAxeWeapon(weapon))
            {
                pendingTwoHandedSpecialAttack.Remove(player);
                return;
            }

            pendingTwoHandedSpecialAttack[player] = secondaryAttack && ClassCombatManager.IsTwoHandedWeapon(weapon);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_StartAttack_Axemaster_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Damage Patches
    /// <summary>
    /// Applies Rending Rhythm's existing stack bonus (Level 30) and Executioner's low-health
    /// bonus (Level 50) to outgoing axe damage. Chopper's Training and Executioner's flat +15%
    /// are handled entirely by ClassCombatManager.GetAxemasterDamageBonus - not duplicated here.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Axemaster_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // Skip synthetic DoT ticks (our own Hemorrhage bleed, or anything else built the
            // same way) - see the m_skill comment in ProcessHemorrhageTicks.
            if (hit.m_skill == Skills.SkillType.None) return;

            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsAxeWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Axemaster)) return;

            float bonus = 0f;

            if (HasAxemasterPerk(player, 30))
            {
                bonus += GetRendingRhythmBonus(__instance);
            }

            if (HasAxemasterPerk(player, 50) && __instance.GetHealthPercentage() < 0.4f)
            {
                bonus += 0.10f; // Executioner's conditional low-health bonus
            }

            if (bonus > 0f)
            {
                float multiplier = 1f + bonus;
                hit.m_damage.m_slash *= multiplier;
                hit.m_damage.m_pierce *= multiplier;
                hit.m_damage.m_blunt *= multiplier;
                hit.m_damage.m_chop *= multiplier;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Axemaster_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies Hemorrhage (Level 40) and advances Rending Rhythm's stack count (Level 30) after
    /// a successful axe hit.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Axemaster_Postfix(Character __instance, HitData hit)
    {
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
            if (hit.GetTotalDamage() <= 0) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsAxeWeapon(weapon)) return;

            if (HasAxemasterPerk(player, 30))
            {
                bool applyAllStacks = pendingTwoHandedSpecialAttack.TryGetValue(player, out var isSpecial) && isSpecial;
                AddRendingRhythmStack(__instance, applyAllStacks);
            }

            if (HasAxemasterPerk(player, 40))
            {
                ApplyHemorrhage(player, __instance, hit.GetTotalDamage());
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Axemaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Updates
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Axemaster_Postfix()
    {
        try
        {
            ProcessHemorrhageTicks();
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Axemaster_Postfix: {ex.Message}");
        }
    }
    #endregion
}
