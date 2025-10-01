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
    public const float POISON_DURATION = 10f;
    public const float ASSASSINATION_SPEED_DURATION = 5f;
    public const float ASSASSINATION_SPEED_BONUS = 1.00f; // 100% attack speed
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
        public float endTime = 0f;
        public float lastTickTime = 0f;
        public float damagePerTick = 0f;
        public float totalDamage = 0f;
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

    #region Level 20 - Venom Coating
    /// <summary>
    /// Lv20 – Venom Coating: Knife hits apply a stacking Poison (up to 3 stacks). 
    /// Poison damage per stack is your vanilla Knife skill level over 5 seconds
    /// </summary>
    public static void ApplyLv20_VenomCoating(Player player, Character target, HitData hit)
    {
        if (!HasAssassinPerk(player, 20) || target == null || target.IsDead()) return;

        // Get knife skill level for poison damage calculation
        float knifeSkillLevel = player.GetSkillLevel(Skills.SkillType.Knives);
        float poisonDamagePerStack = knifeSkillLevel; // Total damage over 10 seconds
        float damagePerTick = poisonDamagePerStack / POISON_DURATION; // 1 tick per second

        // Get or create poison data for this target
        if (!poisonTracking.ContainsKey(target))
        {
            poisonTracking[target] = new PoisonData();
        }

        var poisonData = poisonTracking[target];

        // Add a stack (up to max)
        if (poisonData.stacks < MAX_POISON_STACKS)
        {
            poisonData.stacks++;

            // Add visual status effect
            AddPoisonStatusEffect(target, poisonData.stacks);

            // Show message
            player.Message(MessageHud.MessageType.TopLeft, $"Venom Applied! {poisonData.stacks}/{MAX_POISON_STACKS} stacks");
        }

        // Reset or set poison timer
        poisonData.endTime = Time.time + POISON_DURATION;
        poisonData.damagePerTick = damagePerTick * poisonData.stacks;
        poisonData.source = player;
        poisonData.totalDamage = poisonDamagePerStack * poisonData.stacks;

        StatusEffect currentPoison = target.GetSEMan().GetStatusEffect("Poison".GetStableHashCode());

        //Logger.LogInfo($"[POISON TRACKING] Venom applied: {poisonData.totalDamage}.");
        //Logger.LogInfo($"Applied Venom Coating to {target.name}: {poisonData.stacks} stacks, {poisonData.damagePerTick:F1} DPS");

        // Calculate slow multiplier
        var debuffMult = 1 - (poisonData.stacks * ENVENOMOUS_SLOW_PER_STACK);
        // Apply Movement speed slow.
        poisonData.originalSpeed = target.m_speed;
        target.m_speed *= debuffMult;
        // Apply run speed slow.
        poisonData.originalRunSpeed = target.m_runSpeed;
        target.m_runSpeed *= debuffMult;
    }

    private static void AddPoisonStatusEffect(Character target, int stacks)
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

            statusEffect.m_ttl = POISON_DURATION;

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
            Logger.LogInfo($"Buff Applied: {statusEffect.m_speedModifier}");
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding assassination status effect: {ex.Message}");
        }
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Process poison damage ticks
    /// </summary>
    public static void ProcessPoisonDamage()
    {
        float currentTime = Time.time;
        var toRemove = new List<Character>();

        foreach (var kvp in poisonTracking)
        {
            var target = kvp.Key;
            var poisonData = kvp.Value;

            // Check if target is dead or poison expired
            if (target == null || target.IsDead() || currentTime > poisonData.endTime)
            {
                ClearSpeedDebuff(target, poisonData.originalSpeed, poisonData.originalRunSpeed);
                toRemove.Add(target);
                continue;
            }

            // Apply poison tick damage (once per second)
            if (currentTime - poisonData.lastTickTime >= 1f)
            {
                poisonData.lastTickTime = currentTime;

                // Create poison damage
                HitData poisonHit = new HitData();
                poisonHit.m_damage.m_poison = poisonData.damagePerTick;
                poisonHit.m_attacker = poisonData.source?.GetZDOID() ?? ZDOID.None;
                poisonHit.m_point = target.transform.position;

                target.Damage(poisonHit);

                // Visual feedback
                // DamageText.instance.ShowText(DamageText.TextType.Normal, target.GetCenterPoint(), poisonData.damagePerTick);
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
        Logger.LogInfo("Checking for stealth hit");
        // Check if player is sneaking
        //if (!attacker.IsSneaking())
        //{
        //    Logger.LogInfo("Player was not sneaking.");
        //    return false;
        //}

        // Check if target is unaware (not alerted)
        var baseAI = target.GetBaseAI();
        if (baseAI == null)
        {
            Logger.LogInfo("Target base ai was null.");
            return false;
        }

        if (baseAI.IsAlerted())
        {
            Logger.LogInfo("Target was already alerted.");
            return false;
        }

        if (baseAI.HaveTarget())
        {
            Logger.LogInfo("Base AI had a target");
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
            // Skip applying buffs if it's just poison ticking.
            if (hit.m_damage.m_poison > 0) return;
            // If Player is being damaged check for and apply damage reduction from poison.
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player)
            {
                // Get Attack and check if they are poisoned.
                var attacker = hit.GetAttacker();
                var characterIsPoisoned = poisonTracking.ContainsKey(attacker);

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

                var playerData = PlayerClassManager.GetPlayerData(player);
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
                    Logger.LogInfo("Stealth hit triggered!");
                    multiplier += ASSASSIN_STEALTH_BONUS;
                    ApplyAssassinationSpeedBuff(__instance, player);
                }

                // Apply Twist the Knife damage bonus to poisoned targets (Level 50)
                if (HasAssassinPerk(player, 50)) multiplier += TWIST_KNIFE_DAMAGE_BONUS;

                var originalSlash = hit.m_damage.m_slash;
                var originalPierce = hit.m_damage.m_pierce;
                if (DAMAGE_MODS_ON) hit = ModDamage(hit, multiplier+1f);

                Logger.LogInfo($"Assassin damage mods applied slash: {originalSlash} increased to {hit.m_damage.m_slash}");
                Logger.LogInfo($"Assassin damage mods applied pierce: {originalPierce} increased to {hit.m_damage.m_pierce}");
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
            // Process poison damage every frame (handles its own timing)
            ProcessPoisonDamage();

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