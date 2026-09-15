using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using System.Runtime.CompilerServices;
using ValheimClassObelisk;
using System.Reflection;

/// <summary>
/// Assassin class perk system - focused on knives, stealth, poison, and burst damage
/// </summary>
[HarmonyPatch]
public static class AssassinPerkManager
{
    // Poison tracking - tracks poison stacks per creature
    public static Dictionary<Character, PoisonData> poisonTracking = new Dictionary<Character, PoisonData>();

    // Assassination buff tracking (attack speed after stealth hit)
    private static Dictionary<Player, float> assassinationBuffs = new Dictionary<Player, float>(); // playerID -> buff end time

    // Configuration
    public const int   MAX_POISON_STACKS = 3;
    public const float ASSASSINATION_SPEED_DURATION = 5f;
    public const float ASSASSINATION_SPEED_BONUS = 2.00f; // 100% attack speed
    public const float ENVENOMOUS_SLOW_PER_STACK = 0.20f; // 20% slow per stack
    public const float TWIST_KNIFE_DAMAGE_BONUS = 0.25f; // 25% more damage to poisoned
    public const float TWIST_KNIFE_DAMAGE_REDUCTION = 0.15f; // poisoned enemies deal 15% less
    public const float ASSASSIN_BACKSTAB_BONUS = 0.3f;
    public const float ASSASSIN_CUT_THROAT_BONUS = 0.15f;
    public const float ASSASSIN_STEALTH_BONUS = 1.0f;
    public static bool DAMAGE_MODS_ON = true;

    public class PoisonData
    {
        public int stacks = 0;
        // Estimate of when vanilla's own SE_Poison will consider this application expired
        // (see EstimateVanillaPoisonTTL) - used only for our own stack-reset/cleanup/Twist the
        // Knife bookkeeping. Vanilla manages the real timing itself.
        public float expireTime = 0f;
        // The stack-scaled total damage that still needs to be handed to vanilla's SE_Poison
        // (via ProcessPoisonExpiry). Set by ApplyLv20_VenomCoating, applied once, then cleared.
        public float pendingTotalDamage = 0f;
        public bool damagePending = false;
        public Player source = null;
        public float originalSpeed = 0f;
        public float originalRunSpeed = 0f;
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
    private const string Intro = "Stealthy fighters who strike from the shadows with deadly precision.";
    private const string Outro = "Great for players who prefer tactical, stealthy gameplay and damage over time.";

    public static readonly List<PerkInfo> Perks = new List<PerkInfo>
    {
        new PerkInfo { RequiredLevel = 10, Name = "Cutthroat", Description = "+15% knife damage; +30% backstab multiplier" },
        new PerkInfo { RequiredLevel = 20, Name = "Venom Coating", Description = "Knife hits apply stacking Poison (up to 3 stacks) based on skill level" },
        new PerkInfo { RequiredLevel = 30, Name = "Envenomous", Description = "Poisons apply 15% movement speed slow per stack" },
        new PerkInfo { RequiredLevel = 40, Name = "Assassination", Description = "First knife hit from stealth deals +100% damage" },
        new PerkInfo { RequiredLevel = 50, Name = "Twist the Knife", Description = "+25% damage to poisoned targets; poisoned enemies deal -10% damage" },
    };

    public static string GetClassDescription(Player player)
    {
        int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Assassin) ?? 0;
        return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
    }

    #region Level 20 - Venom Coating
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
    /// Lv20 – Venom Coating: Knife hits apply a stacking Poison (up to 3 stacks), scaled by
    /// knife skill level. The actual damage-over-time is handled entirely by vanilla's own
    /// SE_Poison (see ProcessPoisonExpiry) - this just tracks stacks/expiry and computes the
    /// new stack-scaled total to hand off.
    /// </summary>
    public static void ApplyLv20_VenomCoating(Player player, Character target, HitData hit)
    {
        if (!HasAssassinPerk(player, 20) || target == null || target.IsDead()) return;

        float knifeSkillLevel = player.GetSkillLevel(Skills.SkillType.Knives);

        // Start a fresh stack count if our previous application has fully worn off; otherwise
        // add a stack to the still-active one.
        if (!poisonTracking.TryGetValue(target, out var poisonData) || Time.time >= poisonData.expireTime)
        {
            poisonData = new PoisonData();
            poisonTracking[target] = poisonData;

            // Capture the true baseline speed only once, when there's no active poison to
            // stack onto - recapturing it on every stacking hit would bake the previous
            // stack's already-slowed speed in as the new "original", compounding the slow.
            poisonData.originalSpeed = target.m_speed;
            poisonData.originalRunSpeed = target.m_runSpeed;
        }

        if (poisonData.stacks < MAX_POISON_STACKS)
        {
            poisonData.stacks++;
        }

        // Total damage for the current stack count - handed to vanilla's own SE_Poison once
        // (via ProcessPoisonExpiry) instead of manually ticking it ourselves every second,
        // which was fighting vanilla's own concurrent SE_Poison tick and roughly
        // double-applying damage.
        float totalPoisonDamage = knifeSkillLevel * poisonData.stacks;
        float estimatedTTL = EstimateVanillaPoisonTTL(totalPoisonDamage);

        poisonData.pendingTotalDamage = totalPoisonDamage;
        poisonData.damagePending = true;
        poisonData.expireTime = Time.time + estimatedTTL;
        poisonData.source = player;

        AddPoisonStatusEffect(target, poisonData.stacks, estimatedTTL);

        // Envenomous (Level 30): movement speed slow, scaled by stacks
        if (HasAssassinPerk(player, 30))
        {
            var debuffMult = 1f - (poisonData.stacks * ENVENOMOUS_SLOW_PER_STACK);
            target.m_speed = poisonData.originalSpeed * debuffMult;
            target.m_runSpeed = poisonData.originalRunSpeed * debuffMult;
        }
    }

    private static void AddPoisonStatusEffect(Character target, int stacks, float ttl)
    {
        try
        {
            var seman = target.GetSEMan();
            if (seman == null) return;

            // Remove existing poison effect to refresh it
            seman.RemoveStatusEffect("SE_AssassinPoison".GetStableHashCode(), quiet: true);

            // Create poison status effect
            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_AssassinPoison";
            statusEffect.m_name = $"Venom ({stacks} stacks)";
            statusEffect.m_tooltip = $"Taking poison damage over time. {stacks}/{MAX_POISON_STACKS} stacks";

            // Try to get a poison icon
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
            // Try to find poison-related status effect icons
            var poisoned = ObjectDB.instance?.GetStatusEffect("Poison".GetStableHashCode());
            if (poisoned != null)
            {
                return poisoned.m_icon;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Level 30 - Envenomous
    /// <summary>
    /// Lv30 – Envenomous: Your poisons now apply a 15% movement speed slow per stack
    /// </summary>
    public static float ApplyLv30_EnvenomousMovementSlow(Character target, float baseSpeed)
    {
        if (target == null || !poisonTracking.ContainsKey(target)) return baseSpeed;

        var poisonData = poisonTracking[target];
        if (poisonData.source == null || !HasAssassinPerk(poisonData.source, 30)) return baseSpeed;

        // Apply slow based on poison stacks
        float slowMultiplier = 1f - (poisonData.stacks * ENVENOMOUS_SLOW_PER_STACK);
        slowMultiplier = Mathf.Max(slowMultiplier, 0.25f); // Cap at 75% slow max

        return baseSpeed * slowMultiplier;
    }
    #endregion

    #region Level 40 - Assassination
    private static void ApplyAssassinationSpeedBuff(Character c, Player player)
    {
        long playerID = player.GetPlayerID();
        float buffEndTime = Time.time + ASSASSINATION_SPEED_DURATION;

        assassinationBuffs[player] = buffEndTime;

        // Set the players attack speed factor.
        AnimationSpeedManager.Set(player, "Assassin_Knife_AS", ASSASSINATION_SPEED_BONUS);

        // Add visual status effect
        AddAssassinationStatusEffect(player);
    }

    private static void AddAssassinationStatusEffect(Player player)
    {
        try
        {
            var seman = player.GetSEMan();

            if (seman == null) return;

            // Remove existing effect to refresh
            seman.RemoveStatusEffect("SE_AssassinationSpeed".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_AssassinationSpeed";
            statusEffect.m_name = "Assassination Speed";
            statusEffect.m_tooltip = "+25% attack speed";
            statusEffect.m_icon = player.GetCurrentWeapon()?.GetIcon();
            statusEffect.m_ttl = ASSASSINATION_SPEED_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding assassination status effect: {ex.Message}");
        }
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Applies any newly-stacked poison total to vanilla's own SE_Poison (once per stack
    /// change, not ticked ourselves - see ApplyLv20_VenomCoating) and cleans up tracking once
    /// our estimated expiry has passed or the target has died.
    /// </summary>
    public static void ProcessPoisonExpiry()
    {
        float currentTime = Time.time;
        var toRemove = new List<Character>();

        foreach (var kvp in poisonTracking)
        {
            var target = kvp.Key;
            var poisonData = kvp.Value;

            // Check if target is dead or our estimated expiry has passed
            if (target == null || target.IsDead() || currentTime > poisonData.expireTime)
            {
                ClearSpeedDebuff(target, poisonData.originalSpeed, poisonData.originalRunSpeed);
                toRemove.Add(target);
                continue;
            }

            // Hand the current stack-scaled total to vanilla's SE_Poison exactly once - it
            // owns all the actual tick timing/damage/visuals/sound from here.
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

        // Clean up expired/dead targets
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

    public static void ClearSpeedDebuff(Character target, float originalSpeed, float originalRunSpeed)
    {
        if (!target.IsDead())
        {
            target.m_speed = originalSpeed;
            target.m_runSpeed = originalRunSpeed;
        }
    }

    /// <summary>
    /// Check if a hit is from stealth (enemy was unaware)
    /// </summary>
    public static bool IsStealthHit(Player attacker, Character target)
    {
        if (attacker == null || target == null) return false;

        // Check if target is unaware (not alerted)
        var baseAI = target.GetBaseAI();
        if (baseAI == null)
        {
            return false;
        }

        if (baseAI.IsAlerted())
        {
            return false;
        }

        if (baseAI.HaveTarget())
        {
            return false;
        }

        // Check alert status
        return true;
    }

    /// <summary>
    /// Check if a hit is a backstab
    /// </summary>
    public static bool IsBackstab(Vector3 hitPoint, Character target)
    {
        if (target == null) return false;

        Vector3 toHit = (hitPoint - target.transform.position).normalized;
        float angle = Vector3.Angle(target.transform.forward, toHit);

        // Backstab if hit from behind (more than 120 degrees from front)
        return angle > 120f;
    }

    public static HitData ModDamage(HitData hit, float mod)
    {
        if (hit == null || mod == null) return hit;
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

    /// <summary>
    /// Clean up expired buffs
    /// </summary>
    public static void CleanupBuffs()
    {
        float currentTime = Time.time;

        // Clean up assassination speed buffs
        var expiredBuffs = assassinationBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var player in expiredBuffs)
        {
            // remove the buff from the player.
            assassinationBuffs.Remove(player);

            if (player != null)
            {
                player.GetSEMan()?.RemoveStatusEffect("SE_AssassinationSpeed".GetStableHashCode(), quiet: true);
                // clear the animation speed modifier.
                AnimationSpeedManager.Clear(player, "Assassin_Knife_AS");
            }
        }
    }
    #endregion

    #region Damage Patches
    /// <summary>
    /// Apply Assassin damage bonuses when using knives
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Assassin_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // TODO: We need to patch this so it doesn't throw errors for eating puke berries.
            Player localPlayer = Player.m_localPlayer;
            var playerData = PlayerClassManager.GetPlayerData(localPlayer);
            if (!playerData.IsClassActive("Assassin")) return;

            // Skip applying buffs if it's just poison ticking.
            if (hit.m_damage.m_poison > 0) return;

            // If Player is being damaged check for and apply damage reduction from poison.
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player)
            {
                // Get Attack and check if they are poisoned (and not just a stale,
                // not-yet-cleaned-up tracking entry).
                var attacker = hit.GetAttacker();
                var characterIsPoisoned = poisonTracking.TryGetValue(attacker, out var attackerPoisonData)
                    && Time.time < attackerPoisonData.expireTime;

                if (characterIsPoisoned)
                {
                    // Apply Twist the Knife damage reduction from poisoned enemies (Level 50)
                    var reductionMultiplier = TWIST_KNIFE_DAMAGE_REDUCTION;
                    hit = ModDamage(hit, reductionMultiplier);
                }
            }
            else
            {
                // Apply damage boosts from Assassin skills.
                var weapon = player.GetCurrentWeapon();
                if (!ClassCombatManager.IsKnifeWeapon(weapon)) return;

                if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return;

                float multiplier = 0f;

                // Apply Cutthroat damage bonus (Level 10)
                if (HasAssassinPerk(player, 10)) multiplier += ASSASSIN_CUT_THROAT_BONUS;

                // Check for backstab and apply bonus (Level 10)
                if (IsBackstab(hit.m_point, __instance) && HasAssassinPerk(player, 10)) multiplier += ASSASSIN_BACKSTAB_BONUS;

                // Check for stealth hit and apply Assassination (Level 40)
                bool isStealthHit = IsStealthHit(player, __instance);
                if (isStealthHit && HasAssassinPerk(player, 40))
                {
                    multiplier += ASSASSIN_STEALTH_BONUS;
                    ApplyAssassinationSpeedBuff(__instance, player);
                }

                // Apply Twist the Knife damage bonus to poisoned targets (Level 50)
                if (HasAssassinPerk(player, 50)) multiplier += TWIST_KNIFE_DAMAGE_BONUS;

                var originalSlash = hit.m_damage.m_slash;
                var originalPierce = hit.m_damage.m_pierce;
                if (DAMAGE_MODS_ON) hit = ModDamage(hit, multiplier+1f);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Apply poison after successful knife hit
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPostfix]
    public static void Character_Damage_Assassin_Postfix(Character __instance, HitData hit)
    {
        try
        {
            // Skip poison ticks.
            if (hit.m_damage.m_poison > 0) return;

            // Skip this section if it's not a player.
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only apply to knife damage that actually dealt damage
            if (hit.GetTotalDamage() <= 0) return;

            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsKnifeWeapon(weapon)) return;

            // Make sure damage type is physical. (prevents poison from re-applying itself).
            if (hit.m_damage.m_blunt == 0f && hit.m_damage.m_slash == 0f && hit.m_damage.m_pierce == 0f) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return;

            // Apply Venom Coating poison (Level 20)
            ApplyLv20_VenomCoating(player, __instance, hit);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Assassin_Postfix: {ex.Message}");
        }
    }
    #endregion

    #region Periodic Updates
    /// <summary>
    /// Process poison damage and cleanup
    /// </summary>
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Assassin_Postfix()
    {
        try
        {
            // Apply any newly-stacked poison and process expiry every frame
            ProcessPoisonExpiry();

            // Clean up buffs every second
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