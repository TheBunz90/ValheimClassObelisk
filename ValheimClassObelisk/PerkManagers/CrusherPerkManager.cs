using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using ValheimClassObelisk;

/// <summary>
/// Crusher class perk system - focused on heavy blunt weapons, split between 1H maces and
/// 2H hammers (which have no Heavy Attack, so several perks branch on ClassCombatManager.IsTwoHandedWeapon).
/// </summary>
public static class CrusherPerkManager
{
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

    // Description metadata, shown in the class selection GUI - locked perks display as "???"
    private const string Intro = "Heavy blunt weapon specialists who break armor, shake crowds, and turn slow weapons into decisive hits.";
    private const string Outro = "Best for players who want both maces and two-handed hammers to receive a meaningful version of each perk tier.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Bonebreaker", Description = "+8% blunt damage with clubs, maces, and two-handed hammers." },
        new PerkInfo { RequiredLevel = 20, Name = "Colossus", Description = "Blunt weapons weigh 50% less and impose no movement speed penalties." },
        new PerkInfo { RequiredLevel = 30, Name = "Thundering Blows", Description = "One-handed mace heavy attacks create a 3m shockwave for 25% weapon damage. Two-handed hammer attacks create a 3m aftershock on direct enemy hits for 20% weapon damage." },
        new PerkInfo { RequiredLevel = 40, Name = "Cold Steel", Description = "Blunt attacks deal bonus frost damage equal to 12% of weapon damage." },
        new PerkInfo { RequiredLevel = 50, Name = "Earthshaker", Description = "+15% blunt damage. Staggering an enemy grants +10% attack speed with blunt weapons for 5s." },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Crusher) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 30 - Thundering Blows
    public const float THUNDERING_BLOWS_RADIUS = 3f;
    public const float THUNDERING_BLOWS_1H_PERCENT = 0.25f;
    public const float THUNDERING_BLOWS_2H_PERCENT = 0.20f;

    /// <summary>
    /// Lv30 - Thundering Blows: 1H mace heavy attacks and every 2H hammer hit create a 3m
    /// shockwave (percent of weapon damage varies by caller - see the two trigger sites below).
    /// </summary>
    public static void TriggerThunderingBlows(Player player, Vector3 hitPoint, float weaponDamage, float percent)
    {
        var nearbyEnemies = Physics.OverlapSphere(hitPoint, THUNDERING_BLOWS_RADIUS)
            .Select(c => c.GetComponent<Character>())
            .Where(c => c != null && c != player && !c.IsDead() && c.IsMonsterFaction(0f))
            .ToList();

        if (nearbyEnemies.Count == 0) return;

        float shockwaveDamage = weaponDamage * percent;

        foreach (var enemy in nearbyEnemies)
        {
            HitData shockwaveHit = new HitData();
            shockwaveHit.m_attacker = player.GetZDOID();
            shockwaveHit.m_damage.m_blunt = shockwaveDamage;
            shockwaveHit.m_point = enemy.transform.position;
            shockwaveHit.m_dir = (enemy.transform.position - hitPoint).normalized;
            shockwaveHit.m_skill = Skills.SkillType.Clubs;

            enemy.Damage(shockwaveHit);
        }
    }
    #endregion

    #region Level 40 - Cold Steel
    public const float COLD_STEEL_FROST_PERCENT = 0.12f;

    /// <summary>
    /// Lv40 - Cold Steel: blunt attacks deal bonus frost damage equal to 12% of weapon damage.
    /// </summary>
    public static void ApplyColdSteelFrost(ref HitData hit, float weaponDamage)
    {
        hit.m_damage.m_frost += weaponDamage * COLD_STEEL_FROST_PERCENT;
    }
    #endregion

    #region Level 50 - Earthshaker
    public const float EARTHSHAKER_ATTACK_SPEED = 1.10f;
    public const float EARTHSHAKER_DURATION = 5f;
    public const string EARTHSHAKER_AS_KEY = "Crusher_Earthshaker_AS";

    // Per-player expiry tracking for the temporary attack-speed buff (mirrors Brawler's Rage).
    private static readonly Dictionary<Player, float> earthshakerExpire = new Dictionary<Player, float>();

    public static void TriggerEarthshaker(Player player)
    {
        AnimationSpeedManager.Set(player, EARTHSHAKER_AS_KEY, EARTHSHAKER_ATTACK_SPEED);
        earthshakerExpire[player] = Time.time + EARTHSHAKER_DURATION;
    }

    public static void ProcessEarthshakerExpiry()
    {
        var toRemove = new List<Player>();
        foreach (var kvp in earthshakerExpire)
        {
            if (kvp.Key == null || Time.time >= kvp.Value)
            {
                if (kvp.Key != null) AnimationSpeedManager.Clear(kvp.Key, EARTHSHAKER_AS_KEY);
                toRemove.Add(kvp.Key);
            }
        }
        foreach (var p in toRemove) earthshakerExpire.Remove(p);
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Crusher perks with game systems
/// </summary>
[HarmonyPatch]
public static class CrusherPerkPatches
{
    // Per-player tracking of whether the current attack is a heavy/secondary attack, needed for
    // Thundering Blows' 1H-mace-heavy-only trigger. Per-player (not a shared module-level flag)
    // so this is multiplayer-safe - same pattern as ExecutionerPerkManager.pendingTwoHandedSpecialAttack.
    private static readonly Dictionary<Player, bool> pendingHeavyAttack = new Dictionary<Player, bool>();

    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    [HarmonyPrefix]
    public static void Humanoid_StartAttack_Crusher_Prefix(Humanoid __instance, bool secondaryAttack)
    {
        try
        {
            if (!(__instance is Player player)) return;

            var weapon = player.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsBluntWeapon(weapon))
            {
                pendingHeavyAttack.Remove(player);
                return;
            }

            pendingHeavyAttack[player] = secondaryAttack;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_StartAttack_Crusher_Prefix: {ex.Message}");
        }
    }

    #region Damage Patches
    /// <summary>
    /// Apply Cold Steel's instant frost bonus (Level 40) and snapshot the target's pre-hit
    /// stagger state (for Earthshaker's post-stagger detection in the postfix below). The flat
    /// Bonebreaker (L10) and Earthshaker (L50) damage bonuses live only in
    /// ClassCombatManager.GetCrusherDamageBonus - not duplicated here.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Crusher_Prefix(Character __instance, ref HitData hit, out bool __state)
    {
        __state = false;
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBluntWeapon(weapon)) return;

            if (!CrusherPerkManager.HasCrusherPerk(player, 1)) return;

            // Snapshot pre-hit stagger state so the postfix can tell whether *this* hit is what
            // pushed the target into a stagger (Earthshaker, Level 50).
            __state = __instance.IsStaggering();

            if (CrusherPerkManager.HasCrusherPerk(player, 40))
            {
                CrusherPerkManager.ApplyColdSteelFrost(ref hit, hit.GetTotalDamage());
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Crusher_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Trigger Thundering Blows (Level 30, 1H-heavy vs. every-2H-hit) and Earthshaker's
    /// post-stagger attack-speed buff (Level 50) after the hit lands.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Crusher_Postfix(Character __instance, HitData hit, bool __state)
    {
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
            if (hit.GetTotalDamage() <= 0) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsBluntWeapon(weapon)) return;

            if (CrusherPerkManager.HasCrusherPerk(player, 30))
            {
                if (ClassCombatManager.IsTwoHandedWeapon(weapon))
                {
                    CrusherPerkManager.TriggerThunderingBlows(player, hit.m_point, hit.GetTotalDamage(), CrusherPerkManager.THUNDERING_BLOWS_2H_PERCENT);
                }
                else if (pendingHeavyAttack.TryGetValue(player, out var wasHeavy) && wasHeavy)
                {
                    CrusherPerkManager.TriggerThunderingBlows(player, hit.m_point, hit.GetTotalDamage(), CrusherPerkManager.THUNDERING_BLOWS_1H_PERCENT);
                }
            }

            // Earthshaker (Level 50): this hit pushed the target from not-staggering into staggering.
            if (CrusherPerkManager.HasCrusherPerk(player, 50) && !__state && __instance.IsStaggering())
            {
                CrusherPerkManager.TriggerEarthshaker(player);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Crusher_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Movement Speed / Weight Patches
    /// <summary>
    /// Colossus (Level 20): cancels an equipped blunt weapon's own movement-speed penalty
    /// without touching the item's shared data - only offsets this player's own equipment
    /// movement modifier, mirroring ExecutionerPerkManager's Woodsman's Carry.
    /// </summary>
    [HarmonyPatch(typeof(Player), "GetEquipmentMovementModifier")]
    [HarmonyPostfix]
    public static void Player_GetEquipmentMovementModifier_Crusher_Postfix(Player __instance, ref float __result)
    {
        try
        {
            if (!CrusherPerkManager.HasCrusherPerk(__instance, 20)) return;

            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsBluntWeapon(weapon)) return;

            float weaponPenalty = weapon.m_shared.m_movementModifier;
            if (weaponPenalty < 0f) __result -= weaponPenalty;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_GetEquipmentMovementModifier_Crusher_Postfix: {ex.Message}");
        }
    }

    /// <summary>
    /// Colossus (Level 20): halves a blunt weapon's carry weight for the local player only
    /// (encumbrance is evaluated client-side).
    /// </summary>
    [HarmonyPatch(typeof(ItemDrop.ItemData), "GetWeight")]
    [HarmonyPostfix]
    public static void ItemData_GetWeight_Crusher_Postfix(ItemDrop.ItemData __instance, ref float __result)
    {
        try
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || !CrusherPerkManager.HasCrusherPerk(localPlayer, 20)) return;
            if (!ClassCombatManager.IsBluntWeapon(__instance)) return;

            __result *= 0.5f;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in ItemData_GetWeight_Crusher_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Cleanup
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Crusher_Postfix()
    {
        try
        {
            CrusherPerkManager.ProcessEarthshakerExpiry();
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Crusher_Postfix: {ex.Message}");
        }
    }
    #endregion
}
