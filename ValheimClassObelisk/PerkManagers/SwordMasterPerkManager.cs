using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Logger = Jotunn.Logger;
using ValheimClassObelisk;

/// <summary>
/// Sword Master class perk system - focused on parrying, speed, and precision, split between
/// 1H swords and 2H greatswords (ClassCombatManager.IsTwoHandedWeapon).
/// </summary>
public static class SwordMasterPerkManager
{
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

    // Description metadata, shown in the class selection GUI - locked perks display as "???"
    private const string Intro = "Blade specialists who reward clean timing, fast footwork, and precise melee pressure.";
    private const string Outro = "Best for players who want swords and greatswords to feel precise, reactive, and rewarding.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Blade Training", Description = "+7% sword damage." },
        new PerkInfo { RequiredLevel = 20, Name = "Duelist's Balance", Description = "Sword stamina costs are reduced by 10%, and swords impose no movement speed penalties." },
        new PerkInfo { RequiredLevel = 30, Name = "Riposte Training", Description = "One-handed sword hits within 2s after a parry deal +30% damage. Greatsword special attacks within 2s after a parry deal +30% damage and +20% stagger." },
        new PerkInfo { RequiredLevel = 40, Name = "Searing Edge", Description = "Sword attacks deal bonus fire damage equal to 12% of weapon damage." },
        new PerkInfo { RequiredLevel = 50, Name = "Dancing Steel", Description = "+15% sword attack speed. Parrying grants an additional +10% sword damage for 5s." },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.SwordMaster) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 30 - Riposte Training
    public const float RIPOSTE_DURATION = 2f;
    public const float RIPOSTE_1H_DAMAGE = 0.30f;
    public const float RIPOSTE_2H_DAMAGE = 0.30f;
    public const float RIPOSTE_2H_STAGGER = 0.20f;

    private class RiposteData
    {
        public float expireTime;
        public bool isTwoHanded;
    }

    // Per-player (matches the established pattern for temporary player buffs across the mod).
    private static readonly Dictionary<Player, RiposteData> riposteBuffs = new Dictionary<Player, RiposteData>();

    /// <summary>
    /// Trigger the Riposte buff when the player successfully parries. isTwoHanded records
    /// whether this parry was made while wielding a greatsword, so the correct bonus (and
    /// whether it needs a greatsword *special* attack to consume) applies when it's spent.
    /// </summary>
    public static void TriggerRiposteBuff(Player player, bool isTwoHanded)
    {
        riposteBuffs[player] = new RiposteData { expireTime = Time.time + RIPOSTE_DURATION, isTwoHanded = isTwoHanded };
        AddRiposteStatusEffect(player);
    }

    /// <summary>
    /// Consume the Riposte buff if this hit qualifies: any 1H sword hit, or a greatsword special
    /// attack. Returns the stagger multiplier bonus to apply (0 if not consumed or not 2H).
    /// </summary>
    public static float ConsumeRiposteBuff(Player player, bool hitIsTwoHanded, bool hitIsSpecialAttack, ref HitData hit)
    {
        if (!riposteBuffs.TryGetValue(player, out var data) || Time.time >= data.expireTime) return 0f;

        // A greatsword's stored buff only pays out on a special attack; a 1H buff pays out on any hit.
        if (data.isTwoHanded && !hitIsSpecialAttack) return 0f;

        riposteBuffs.Remove(player);
        RemoveRiposteStatusEffect(player);

        float damageBonus = data.isTwoHanded ? RIPOSTE_2H_DAMAGE : RIPOSTE_1H_DAMAGE;
        float multiplier = 1f + damageBonus;
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

        player.Message(MessageHud.MessageType.TopLeft, $"Riposte! +{damageBonus * 100f:F0}% damage");

        return data.isTwoHanded ? RIPOSTE_2H_STAGGER : 0f;
    }

    private static void AddRiposteStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            RemoveRiposteStatusEffect(player);

            var weapon = player.GetCurrentWeapon();
            Sprite weaponIcon = weapon?.GetIcon() ?? GetDefaultSwordIcon();

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_RiposteReady";
            statusEffect.m_name = "Riposte Ready";
            statusEffect.m_tooltip = "Next qualifying sword attack deals bonus damage";
            statusEffect.m_icon = weaponIcon;
            statusEffect.m_ttl = RIPOSTE_DURATION;
            statusEffect.m_startMessage = "";
            statusEffect.m_startMessageType = MessageHud.MessageType.Center;
            statusEffect.m_stopMessage = "";
            statusEffect.m_stopMessageType = MessageHud.MessageType.Center;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding riposte status effect: {ex.Message}");
        }
    }

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

    private static Sprite GetDefaultSwordIcon()
    {
        try
        {
            string[] swordNames = { "SwordBronze", "SwordIron", "SwordSilver", "SwordBlackmetal", "Knife" };
            foreach (string swordName in swordNames)
            {
                var prefab = ObjectDB.instance?.GetItemPrefab(swordName);
                var icon = prefab?.GetComponent<ItemDrop>()?.m_itemData?.GetIcon();
                if (icon != null) return icon;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Level 40 - Searing Edge
    public const float SEARING_EDGE_FIRE_PERCENT = 0.12f;

    public static void ApplySearingEdgeFire(ref HitData hit, float weaponDamage)
    {
        hit.m_damage.m_fire += weaponDamage * SEARING_EDGE_FIRE_PERCENT;
    }
    #endregion

    #region Level 50 - Dancing Steel
    public const float DANCING_STEEL_ATTACK_SPEED = 1.15f;
    public const string DANCING_STEEL_AS_KEY = "SwordMaster_DancingSteel_AS";
    public const float DANCING_STEEL_PARRY_DAMAGE = 0.10f;
    public const float DANCING_STEEL_PARRY_DURATION = 5f;

    private static readonly Dictionary<Player, float> dancingSteelParryExpire = new Dictionary<Player, float>();

    /// <summary>
    /// Keeps the persistent attack-speed multiplier registered/cleared based on whether the
    /// player currently has a sword equipped and is Level 50 - called every tick from the
    /// periodic update rather than only on equip/unequip, since attack-speed sources need to
    /// react to level-ups and class deactivation too, not just gear changes.
    /// </summary>
    public static void RefreshDancingSteelAttackSpeed(Player player)
    {
        if (player == null) return;

        var weapon = player.GetCurrentWeapon();
        bool shouldApply = HasSwordMasterPerk(player, 50) && ClassCombatManager.IsSwordWeapon(weapon);

        if (shouldApply) AnimationSpeedManager.Set(player, DANCING_STEEL_AS_KEY, DANCING_STEEL_ATTACK_SPEED);
        else AnimationSpeedManager.Clear(player, DANCING_STEEL_AS_KEY);
    }

    public static void TriggerDancingSteelParryBonus(Player player)
    {
        dancingSteelParryExpire[player] = Time.time + DANCING_STEEL_PARRY_DURATION;
    }

    /// <summary>
    /// Applies Dancing Steel's flat +10% directly to the hit if the parry-window buff is active -
    /// mutates in place (like ConsumeRiposteBuff) rather than returning a new total, since this
    /// can run after Riposte Training has already modified the same hit and a ratio computed
    /// against a stale pre-Riposte baseline would be wrong.
    /// </summary>
    public static void ApplyDancingSteelParryDamage(Player player, ref HitData hit)
    {
        if (!dancingSteelParryExpire.TryGetValue(player, out var expire) || Time.time >= expire) return;

        float multiplier = 1f + DANCING_STEEL_PARRY_DAMAGE;
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
    #endregion

    #region Utility Methods
    /// <summary>
    /// Clean up expired buffs. Reflection targets (GetCurrentBlocker/m_blockTimer) are cached
    /// once here - matching BulwarkPerkManager's approach - rather than re-resolved per call.
    /// </summary>
    private static MethodInfo _getCurrentBlockerMethod;
    private static FieldInfo _blockTimerField;

    public static ItemDrop.ItemData GetCurrentBlocker(Humanoid humanoid)
    {
        if (humanoid == null) return null;

        try
        {
            if (_getCurrentBlockerMethod == null)
            {
                _getCurrentBlockerMethod = typeof(Humanoid).GetMethod("GetCurrentBlocker", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_getCurrentBlockerMethod == null)
                {
                    Logger.LogError("[Sword Master] Could not find GetCurrentBlocker method via reflection");
                    return null;
                }
            }

            return (ItemDrop.ItemData)_getCurrentBlockerMethod.Invoke(humanoid, null);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error calling GetCurrentBlocker via reflection: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// A block counts as "perfect"/timed (a parry) when the blocker has a timed-block bonus and
    /// the block landed within the first ~0.25s of the hit, matching vanilla's own parry window.
    /// </summary>
    public static bool WasTimedBlock(Humanoid humanoid, ItemDrop.ItemData blocker)
    {
        if (blocker == null) return false;

        if (_blockTimerField == null)
        {
            _blockTimerField = typeof(Humanoid).GetField("m_blockTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            if (_blockTimerField == null)
            {
                Logger.LogWarning("[Sword Master] Could not access m_blockTimer field - Riposte Training's timed-block detection is disabled");
                return false;
            }
        }

        float blockTimer = (float)_blockTimerField.GetValue(humanoid);
        return blocker.m_shared.m_timedBlockBonus > 1f && blockTimer != -1f && blockTimer < 0.25f;
    }

    public static void UpdateBuffs()
    {
        float currentTime = Time.time;

        var expiredRiposte = riposteBuffs.Where(kvp => kvp.Key == null || currentTime >= kvp.Value.expireTime).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredRiposte) riposteBuffs.Remove(player);

        var expiredParry = dancingSteelParryExpire.Where(kvp => kvp.Key == null || currentTime >= kvp.Value).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredParry) dancingSteelParryExpire.Remove(player);
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Sword Master perks with game systems
/// </summary>
[HarmonyPatch]
public static class SwordMasterPerkPatches
{
    // Per-player tracking of whether the current attack is a special/secondary attack, needed
    // for Riposte Training's greatsword-special-only consumption rule.
    private static readonly Dictionary<Player, bool> pendingSpecialAttack = new Dictionary<Player, bool>();

    [HarmonyPatch(typeof(Humanoid), "StartAttack")]
    [HarmonyPrefix]
    public static void Humanoid_StartAttack_SwordMaster_Prefix(Humanoid __instance, bool secondaryAttack)
    {
        try
        {
            if (!(__instance is Player player)) return;

            var weapon = player.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsSwordWeapon(weapon))
            {
                pendingSpecialAttack.Remove(player);
                return;
            }

            pendingSpecialAttack[player] = secondaryAttack;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_StartAttack_SwordMaster_Prefix: {ex.Message}");
        }
    }

    #region Damage Patches
    /// <summary>
    /// Apply Riposte Training's consumable bonus (Level 30), Dancing Steel's parry-window bonus
    /// (Level 50), and Searing Edge's fire damage (Level 40). Blade Training's flat +7% (Level
    /// 10) lives only in ClassCombatManager.GetSwordMasterDamageBonus - not duplicated here.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_SwordMaster_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            if (hit.m_skill == Skills.SkillType.None) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;
            if (!SwordMasterPerkManager.HasSwordMasterPerk(player, 1)) return;

            float originalDamage = hit.GetTotalDamage();

            if (SwordMasterPerkManager.HasSwordMasterPerk(player, 30))
            {
                bool isTwoHanded = ClassCombatManager.IsTwoHandedWeapon(weapon);
                bool isSpecial = pendingSpecialAttack.TryGetValue(player, out var special) && special;
                float staggerBonus = SwordMasterPerkManager.ConsumeRiposteBuff(player, isTwoHanded, isSpecial, ref hit);
                if (staggerBonus > 0f) hit.m_staggerMultiplier *= 1f + staggerBonus;
            }

            if (SwordMasterPerkManager.HasSwordMasterPerk(player, 50))
            {
                SwordMasterPerkManager.ApplyDancingSteelParryDamage(player, ref hit);
            }

            if (SwordMasterPerkManager.HasSwordMasterPerk(player, 40))
            {
                SwordMasterPerkManager.ApplySearingEdgeFire(ref hit, originalDamage);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_SwordMaster_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Parry Patches
    /// <summary>
    /// Trigger Riposte Training's buff when the player successfully parries (not just blocks).
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    [HarmonyPostfix]
    public static void Humanoid_BlockAttack_SwordMaster_Postfix(Humanoid __instance, bool __result, HitData hit, Character attacker)
    {
        try
        {
            if (!__result || !(__instance is Player player)) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;
            if (!SwordMasterPerkManager.HasSwordMasterPerk(player, 30) && !SwordMasterPerkManager.HasSwordMasterPerk(player, 50)) return;

            var blocker = SwordMasterPerkManager.GetCurrentBlocker(__instance);
            if (!SwordMasterPerkManager.WasTimedBlock(__instance, blocker)) return;

            if (SwordMasterPerkManager.HasSwordMasterPerk(player, 30))
            {
                SwordMasterPerkManager.TriggerRiposteBuff(player, ClassCombatManager.IsTwoHandedWeapon(weapon));
            }

            if (SwordMasterPerkManager.HasSwordMasterPerk(player, 50))
            {
                SwordMasterPerkManager.TriggerDancingSteelParryBonus(player);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Humanoid_BlockAttack_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Stamina Patches
    /// <summary>
    /// Apply Duelist's Balance stamina reduction (Level 20) when using stamina for sword attacks.
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_SwordMaster_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (__instance == null) return;
            if (!SwordMasterPerkManager.HasSwordMasterPerk(__instance, 20)) return;

            var weapon = __instance.GetCurrentWeapon();
            if (!ClassCombatManager.IsSwordWeapon(weapon)) return;

            v *= 0.90f; // 10% stamina reduction
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_SwordMaster_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Movement Speed Patches
    /// <summary>
    /// Duelist's Balance (Level 20): cancels an equipped sword's own movement-speed penalty
    /// without touching the item's shared data, mirroring ExecutionerPerkManager's Woodsman's Carry.
    /// </summary>
    [HarmonyPatch(typeof(Player), "GetEquipmentMovementModifier")]
    [HarmonyPostfix]
    public static void Player_GetEquipmentMovementModifier_SwordMaster_Postfix(Player __instance, ref float __result)
    {
        try
        {
            if (!SwordMasterPerkManager.HasSwordMasterPerk(__instance, 20)) return;

            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null || !ClassCombatManager.IsSwordWeapon(weapon)) return;

            float weaponPenalty = weapon.m_shared.m_movementModifier;
            if (weaponPenalty < 0f) __result -= weaponPenalty;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_GetEquipmentMovementModifier_SwordMaster_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Cleanup
    /// <summary>
    /// Clean up expired buffs and refresh Dancing Steel's persistent attack-speed state every tick.
    /// </summary>
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_SwordMaster_Postfix()
    {
        try
        {
            var player = Player.m_localPlayer;
            if (player != null) SwordMasterPerkManager.RefreshDancingSteelAttackSpeed(player);

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
