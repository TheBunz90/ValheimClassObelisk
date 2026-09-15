using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using ValheimClassObelisk;

/// <summary>
/// Assassin class perk system - knife mastery building into stealth burst and poison execution
/// </summary>
[HarmonyPatch]
public static class AssassinPerkManager
{
    // Poison tracking - tracks poison stacks per creature
    public static Dictionary<Character, PoisonData> poisonTracking = new Dictionary<Character, PoisonData>();

    // Assassination attack-speed buff tracking
    private static Dictionary<Player, float> assassinationBuffs = new Dictionary<Player, float>(); // player -> buff end time

    // Normal movement speed (m_speed - the jog/run baseline, distinct from m_walkSpeed which
    // is the toggled walk-mode speed, already close to vanilla's own crouch speed) cached in a
    // Prefix on Player.SetCrouch, before the original method runs - used instead of a live
    // re-read in the Postfix, in case anything in between changes m_speed.
    private static readonly Dictionary<Player, float> _cachedMoveSpeed = new Dictionary<Player, float>();

    // Configuration
    public const int MAX_POISON_STACKS = 3;
    public const float ASSASSINATION_SPEED_DURATION = 5f;
    public const float ASSASSINATION_SPEED_BONUS = 1.20f; // +20% attack speed
    public const float ASSASSINATION_DAMAGE_BONUS = 0.50f; // +50% damage on the stealth hit itself
    public const float SILENT_HANDS_STAMINA_REDUCTION = 0.15f;
    // Vanilla's own default for Character.m_crouchSpeed - restored when Silent Hands isn't
    // active, so a player who deactivates Assassin later doesn't stay permanently faster.
    private const float VANILLA_BASE_CROUCH_SPEED = 2f;
    public const float TWIST_KNIFE_DAMAGE_BONUS = 0.15f; // vs. poisoned targets
    public const float TWIST_KNIFE_DAMAGE_REDUCTION = 0.10f; // poisoned enemies deal less
    public static bool DAMAGE_MODS_ON = true;

    public class PoisonData
    {
        public int stacks = 0;
        // Estimate of when vanilla's own SE_Poison will consider this application expired
        // (see EstimateVanillaPoisonTTL) - used only for our own stack-reset/cleanup/Twist the
        // Knife bookkeeping. Vanilla manages the real timing itself.
        public float expireTime = 0f;
        // The stack-scaled total damage that still needs to be handed to vanilla's SE_Poison
        // (via ProcessPoisonExpiry). Set by ApplyLv40_VenomCoating, applied once, then cleared.
        public float pendingTotalDamage = 0f;
        public bool damagePending = false;
        public Player source = null;
    }

    /// <summary>
    /// Check if player has Assassin class active and at required level
    /// </summary>
    public static bool HasAssassinPerk(Player player, int requiredLevel)
    {
        if (player == null) return false;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return false;

        return playerData.GetClassLevel(PlayerClass.Assassin) >= requiredLevel;
    }

    // Description metadata, shown in the class selection GUI - locked perks display as "???"
    private const string Intro = "Stealthy fighters who use knives, positioning, and toxins to end fights quickly.";
    private const string Outro = "Best for players who want the class to build from light knife mastery into stealth burst and poison execution.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Cutthroat", Description = "+7% knife damage." },
        new PerkInfo { RequiredLevel = 20, Name = "Silent Hands", Description = "Knife attacks and stealth movement consume 15% less stamina. Additionally, you move at normal movement speed while crouched." },
        new PerkInfo { RequiredLevel = 30, Name = "Assassination", Description = "First knife hit from stealth deals +50% damage. After a stealth hit, knife attack speed is increased by 20% for 5s." },
        new PerkInfo { RequiredLevel = 40, Name = "Venom Coating", Description = "Knife hits apply stacking poison damage over time, up to 3 stacks. Poison damage scales with knife skill." },
        new PerkInfo { RequiredLevel = 50, Name = "Twist the Knife", Description = "+15% knife damage against poisoned targets. Poisoned enemies deal 10% less damage to you." },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Assassin) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 20 - Silent Hands
    /// <summary>
    /// Applies (or clears) Silent Hands' crouch movement speed - moving at normal movement
    /// speed while crouched, instead of vanilla's slower sneak pace. Uses m_speed (the
    /// jog/run baseline) rather than m_walkSpeed - m_walkSpeed is the toggled walk-mode speed,
    /// which is already close to vanilla's own crouch speed, so matching it wouldn't be a
    /// noticeable buff. Uses the speed cached by Player_SetCrouch_Assassin_Prefix (captured
    /// before the crouch toggle runs) rather than a live re-read, in case something between the
    /// Prefix and Postfix changes m_speed; falls back to a live read if there's no cached value
    /// yet (e.g. the spawn hook, before any crouch toggle has happened this session).
    /// Character.m_crouchSpeed is a plain per-instance field (unlike ItemData.m_shared,
    /// mutating it only affects this one player), read fresh every frame by the game's own
    /// movement update - so setting it once here is enough, no continuous per-frame patch
    /// needed.
    /// </summary>
    private static void RefreshSilentHandsCrouchSpeed(Player player)
    {
        if (player == null) return;

        float moveSpeed = _cachedMoveSpeed.TryGetValue(player, out var cached) ? cached : player.m_speed;

        player.m_crouchSpeed = HasAssassinPerk(player, 20) ? moveSpeed : VANILLA_BASE_CROUCH_SPEED;
    }
    #endregion

    #region Level 30 - Assassination
    private static void ApplyAssassinationSpeedBuff(Player player)
    {
        float buffEndTime = Time.time + ASSASSINATION_SPEED_DURATION;
        assassinationBuffs[player] = buffEndTime;

        AnimationSpeedManager.Set(player, "Assassin_Knife_AS", ASSASSINATION_SPEED_BONUS);
        AddAssassinationStatusEffect(player);
    }

    private static void AddAssassinationStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_AssassinationSpeed".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_AssassinationSpeed";
            statusEffect.m_name = "Assassination";
            statusEffect.m_tooltip = "+20% knife attack speed";
            statusEffect.m_icon = player.GetCurrentWeapon()?.GetIcon();
            statusEffect.m_ttl = ASSASSINATION_SPEED_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding assassination status effect: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if a hit is from stealth (enemy was unaware)
    /// </summary>
    public static bool IsStealthHit(Player attacker, Character target)
    {
        if (attacker == null || target == null) return false;

        var baseAI = target.GetBaseAI();
        if (baseAI == null) return false;
        if (baseAI.IsAlerted()) return false;
        if (baseAI.HaveTarget()) return false;

        return true;
    }
    #endregion

    #region Level 40 - Venom Coating
    /// <summary>
    /// Mirrors vanilla SE_Poison.AddDamage's own TTL formula (m_baseTTL=2, m_TTLPerDamage=2,
    /// m_TTLPower=0.5), so our own bookkeeping (stack-reset window, Twist the Knife, UI tooltip)
    /// estimates roughly the same expiry vanilla will actually use.
    /// </summary>
    private static float EstimateVanillaPoisonTTL(float totalDamage)
    {
        return 2f + Mathf.Pow(totalDamage * 2f, 0.5f);
    }

    /// <summary>
    /// Lv40 – Venom Coating: Knife hits apply a stacking Poison (up to 3 stacks), scaled by
    /// knife skill level. The actual damage-over-time is handled entirely by vanilla's own
    /// SE_Poison (see ProcessPoisonExpiry) - this just tracks stacks/expiry and computes the
    /// new stack-scaled total to hand off.
    /// </summary>
    public static void ApplyLv40_VenomCoating(Player player, Character target, HitData hit)
    {
        if (!HasAssassinPerk(player, 40) || target == null || target.IsDead()) return;

        float knifeSkillLevel = player.GetSkillLevel(Skills.SkillType.Knives);

        // Start a fresh stack count if our previous application has fully worn off; otherwise
        // add a stack to the still-active one.
        if (!poisonTracking.TryGetValue(target, out var poisonData) || Time.time >= poisonData.expireTime)
        {
            poisonData = new PoisonData();
            poisonTracking[target] = poisonData;
        }

        if (poisonData.stacks < MAX_POISON_STACKS)
        {
            poisonData.stacks++;
        }

        // Total damage for the current stack count - handed to vanilla's own SE_Poison once
        // (via ProcessPoisonExpiry) instead of manually ticking it ourselves every second.
        float totalPoisonDamage = knifeSkillLevel * poisonData.stacks;
        float estimatedTTL = EstimateVanillaPoisonTTL(totalPoisonDamage);

        poisonData.pendingTotalDamage = totalPoisonDamage;
        poisonData.damagePending = true;
        poisonData.expireTime = Time.time + estimatedTTL;
        poisonData.source = player;

        AddPoisonStatusEffect(target, poisonData.stacks, estimatedTTL);
    }

    private static void AddPoisonStatusEffect(Character target, int stacks, float ttl)
    {
        try
        {
            var seman = target.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_AssassinPoison".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_AssassinPoison";
            statusEffect.m_name = $"Venom ({stacks} stacks)";
            statusEffect.m_tooltip = $"Taking poison damage over time. {stacks}/{MAX_POISON_STACKS} stacks";

            var poisonIcon = GetPoisonIcon();
            if (poisonIcon != null)
            {
                statusEffect.m_icon = poisonIcon;
            }

            statusEffect.m_ttl = ttl;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding poison status effect: {ex.Message}");
        }
    }

    private static Sprite GetPoisonIcon()
    {
        try
        {
            var poisoned = ObjectDB.instance?.GetStatusEffect("Poison".GetStableHashCode());
            return poisoned?.m_icon;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Applies any newly-stacked poison total to vanilla's own SE_Poison (once per stack
    /// change, not ticked ourselves) and cleans up tracking once our estimated expiry has
    /// passed or the target has died.
    /// </summary>
    public static void ProcessPoisonExpiry()
    {
        float currentTime = Time.time;
        var toRemove = new List<Character>();

        foreach (var kvp in poisonTracking)
        {
            var target = kvp.Key;
            var poisonData = kvp.Value;

            if (target == null || target.IsDead() || currentTime > poisonData.expireTime)
            {
                toRemove.Add(target);
                continue;
            }

            if (poisonData.damagePending)
            {
                poisonData.damagePending = false;

                HitData poisonHit = new HitData();
                poisonHit.m_damage.m_poison = poisonData.pendingTotalDamage;
                poisonHit.m_attacker = poisonData.source?.GetZDOID() ?? ZDOID.None;
                poisonHit.m_point = target.transform.position;

                target.Damage(poisonHit);
            }
        }

        foreach (var target in toRemove)
        {
            if (target != null)
            {
                var seman = target.GetSEMan();
                seman?.RemoveStatusEffect("SE_AssassinPoison".GetStableHashCode(), quiet: true);
            }
            poisonTracking.Remove(target);
        }
    }

    private static bool IsTargetPoisoned(Character target)
    {
        return target != null
            && poisonTracking.TryGetValue(target, out var data)
            && Time.time < data.expireTime;
    }
    #endregion

    #region Utility Methods
    public static HitData ModDamage(HitData hit, float mod)
    {
        if (hit == null) return hit;
        hit.m_damage.m_damage *= mod;
        hit.m_damage.m_slash *= mod;
        hit.m_damage.m_pierce *= mod;
        hit.m_damage.m_blunt *= mod;
        hit.m_damage.m_fire *= mod;
        hit.m_damage.m_frost *= mod;
        hit.m_damage.m_spirit *= mod;
        hit.m_damage.m_poison *= mod;
        return hit;
    }

    public static void CleanupBuffs()
    {
        float currentTime = Time.time;

        var expiredBuffs = assassinationBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredBuffs)
        {
            assassinationBuffs.Remove(player);

            if (player != null)
            {
                player.GetSEMan()?.RemoveStatusEffect("SE_AssassinationSpeed".GetStableHashCode(), quiet: true);
                AnimationSpeedManager.Clear(player, "Assassin_Knife_AS");
            }
        }
    }
    #endregion

    #region Damage Patches
    /// <summary>
    /// Apply Assassin damage bonuses/reductions when using knives. Cutthroat's flat +7% lives
    /// in ClassCombatManager.GetAssassinDamageBonus - not duplicated here.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Assassin_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // Skip poison ticks re-entering Damage() via ProcessPoisonExpiry.
            if (hit.m_damage.m_poison > 0) return;

            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player)
            {
                // The local player is being damaged - check if the attacker is poisoned
                // (Twist the Knife, Level 50: poisoned enemies deal less damage).
                var attacker = hit.GetAttacker();
                var localPlayerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (localPlayerData == null || !localPlayerData.IsClassActive(PlayerClass.Assassin)) return;
                if (!HasAssassinPerk(Player.m_localPlayer, 50)) return;

                if (IsTargetPoisoned(attacker))
                {
                    hit = ModDamage(hit, 1f - TWIST_KNIFE_DAMAGE_REDUCTION);
                }
            }
            else
            {
                var weapon = player.GetCurrentWeapon();
                if (!ClassCombatManager.IsKnifeWeapon(weapon)) return;

                var playerData = PlayerClassManager.GetPlayerData(player);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return;

                float bonus = 0f;

                // Assassination (Level 30): first knife hit from stealth
                if (HasAssassinPerk(player, 30) && IsStealthHit(player, __instance))
                {
                    bonus += ASSASSINATION_DAMAGE_BONUS;
                    ApplyAssassinationSpeedBuff(player);
                }

                // Twist the Knife (Level 50): bonus damage against poisoned targets only
                if (HasAssassinPerk(player, 50) && IsTargetPoisoned(__instance))
                {
                    bonus += TWIST_KNIFE_DAMAGE_BONUS;
                }

                if (bonus > 0f && DAMAGE_MODS_ON)
                {
                    hit = ModDamage(hit, bonus + 1f);
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply poison after a successful knife hit.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Assassin_Postfix(Character __instance, HitData hit)
    {
        try
        {
            if (hit.m_damage.m_poison > 0) return;
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
            if (hit.GetTotalDamage() <= 0) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsKnifeWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return;

            // Make sure damage type is physical (prevents poison ticks from re-applying poison).
            if (hit.m_damage.m_blunt == 0f && hit.m_damage.m_slash == 0f && hit.m_damage.m_pierce == 0f) return;

            ApplyLv40_VenomCoating(player, __instance, hit);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Assassin_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Stamina Patches
    /// <summary>
    /// Silent Hands (Level 20): knife attacks and stealth (crouched) movement consume 15% less
    /// stamina. Player.UseStamina is the single funnel for stamina costs in this codebase
    /// (matches the pattern already used by Archer/Crusher/Bulwark for their own reductions).
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_Assassin_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (!HasAssassinPerk(__instance, 20)) return;

            bool isKnife = ClassCombatManager.IsKnifeWeapon(__instance.GetCurrentWeapon());
            bool isSneaking = __instance.IsCrouching();

            if (isKnife || isSneaking)
            {
                v *= (1f - SILENT_HANDS_STAMINA_REDUCTION);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_Assassin_Prefix: {ex.Message}");
        }
    }
    #endregion

    #region Spawn
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    [HarmonyPostfix]
    public static void Player_OnSpawned_Assassin_Postfix(Player __instance)
    {
        try
        {
            RefreshSilentHandsCrouchSpeed(__instance);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_OnSpawned_Assassin_Postfix: {ex.Message}");
        }
    }

    /// <summary>
    /// Caches m_speed before Player.SetCrouch (and anything else in that call) runs, for the
    /// Postfix below to use instead of a live re-read.
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetCrouch")]
    [HarmonyPrefix]
    public static void Player_SetCrouch_Assassin_Prefix(Player __instance)
    {
        try
        {
            _cachedMoveSpeed[__instance] = __instance.m_speed;
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_SetCrouch_Assassin_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Player.SetCrouch is just `m_crouchToggled = crouch;` - the actual moment the player
    /// toggles crouch on/off (both directions). Refreshing here, rather than reactively on
    /// spawn/hit, ties the speed value directly to the action that actually uses it.
    /// </summary>
    [HarmonyPatch(typeof(Player), "SetCrouch")]
    [HarmonyPostfix]
    public static void Player_SetCrouch_Assassin_Postfix(Player __instance)
    {
        try
        {
            RefreshSilentHandsCrouchSpeed(__instance);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_SetCrouch_Assassin_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Updates
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Assassin_Postfix()
    {
        try
        {
            ProcessPoisonExpiry();

            if (Time.time % 1f < Time.deltaTime)
            {
                CleanupBuffs();
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Assassin_Postfix: {ex.Message}");
        }
    }
    #endregion
}
