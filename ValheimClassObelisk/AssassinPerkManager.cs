using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using System.Runtime.CompilerServices;

/// <summary>
/// Assassin class perk system - focused on knives, stealth, poison, and burst damage
/// </summary>
public static class AssassinPerkManager
{
    // Poison tracking - tracks poison stacks per creature
    private static Dictionary<Character, PoisonData> poisonTracking = new Dictionary<Character, PoisonData>();

    // Assassination buff tracking (attack speed after stealth hit)
    private static Dictionary<long, float> assassinationBuffs = new Dictionary<long, float>(); // playerID -> buff end time

    // Configuration
    public const int   MAX_POISON_STACKS = 3;
    public const float POISON_DURATION = 10f;
    public const float ASSASSINATION_SPEED_DURATION = 5f;
    public const float ASSASSINATION_SPEED_BONUS = 0.25f; // 25% attack speed
    public const float ENVENOMOUS_SLOW_PER_STACK = 0.15f; // 15% slow per stack
    public const float TWIST_KNIFE_DAMAGE_BONUS = 0.25f; // 25% more damage to poisoned
    public const float TWIST_KNIFE_DAMAGE_REDUCTION = 0.10f; // poisoned enemies deal 10% less

    private class PoisonData
    {
        public int stacks = 0;
        public float endTime = 0f;
        public float lastTickTime = 0f;
        public float damagePerTick = 0f;
        public float totalDamage = 0f;
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

    #region Level 10 - Cutthroat
    /// <summary>
    /// Lv10 – Cutthroat: +15% knife damage; +30% backstab multiplier with knives
    /// </summary>
    public static float ApplyLv10_CutthroatDamage(Player player, float baseDamage)
    {
        if (!HasAssassinPerk(player, 10)) return baseDamage;

        return baseDamage * 1.15f; // 15% increased knife damage
    }

    public static float ApplyLv10_CutthroatBackstab(Player player, float baseBackstabBonus)
    {
        if (!HasAssassinPerk(player, 10)) return baseBackstabBonus;

        return baseBackstabBonus * 1.30f; // 30% increased backstab multiplier
    }
    #endregion

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
        float poisonDamagePerStack = 10; // Total damage over 5 seconds
        float damagePerTick = 10 / POISON_DURATION; // 1 tick per second

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

        // TODO: Actually poison the target.
        StatusEffect currentPoison = target.GetSEMan().GetStatusEffect("Poison".GetStableHashCode());

        
        //hit.m_damage.m_poison = poisonData.totalDamage;
        Logger.LogInfo($"[POISON TRACKING] Venom applied: {poisonData.totalDamage}.");
        Logger.LogInfo($"Applied Venom Coating to {target.name}: {poisonData.stacks} stacks, {poisonData.damagePerTick:F1} DPS");
        //return hit;
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
    /// <summary>
    /// Lv40 – Assassination: First knife hit from stealth deals +100% damage and grants +25% attack speed for 5 seconds
    /// </summary>
    public static float ApplyLv40_AssassinationDamage(Player player, float baseDamage)
    {
        if (!HasAssassinPerk(player, 40)) return baseDamage;

        // Double damage from stealth
        float bonusDamage = baseDamage * 2f;

        // Trigger attack speed buff
        TriggerAssassinationSpeedBuff(player);

        player.Message(MessageHud.MessageType.Center, "Assassination! +100% damage");

        return bonusDamage;
    }

    private static void TriggerAssassinationSpeedBuff(Player player)
    {
        long playerID = player.GetPlayerID();
        float buffEndTime = Time.time + ASSASSINATION_SPEED_DURATION;

        assassinationBuffs[playerID] = buffEndTime;
        // TODO: Set the players attack speed factor.

        // Add visual status effect
        AddAssassinationStatusEffect(player);

        Logger.LogInfo($"Triggered Assassination speed buff for {player.GetPlayerName()}");
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

            // Apply attack speed modifier
            statusEffect.m_speedModifier = 1f + ASSASSINATION_SPEED_BONUS;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding assassination status effect: {ex.Message}");
        }
    }

    public static float ApplyLv40_AssassinationAttackSpeed(Player player, float baseSpeed)
    {
        if (!HasAssassinPerk(player, 40)) return baseSpeed;

        long playerID = player.GetPlayerID();
        if (assassinationBuffs.ContainsKey(playerID) && Time.time < assassinationBuffs[playerID])
        {
            return baseSpeed * (1f + ASSASSINATION_SPEED_BONUS);
        }

        return baseSpeed;
    }
    #endregion

    #region Level 50 - Twist the Knife
    /// <summary>
    /// Lv50 – Twist the Knife: +25% damage to poisoned targets and poisoned targets deal -10% damage
    /// </summary>
    public static float ApplyLv50_TwistKnifeDamageBonus(Player player, Character target, float baseDamage)
    {
        if (!HasAssassinPerk(player, 50)) return baseDamage;

        // Check if target is poisoned
        if (poisonTracking.ContainsKey(target) && poisonTracking[target].stacks > 0)
        {
            float bonusDamage = baseDamage * (1f + TWIST_KNIFE_DAMAGE_BONUS);

            // Show message occasionally
            if (Random.Range(0f, 1f) < 0.2f)
            {
                player.Message(MessageHud.MessageType.TopLeft, $"Twist the Knife! +{TWIST_KNIFE_DAMAGE_BONUS * 100f}% damage");
            }

            return bonusDamage;
        }

        return baseDamage;
    }

    public static float ApplyLv50_TwistKnifeDamageReduction(Character attacker, float damage)
    {
        // Check if attacker is poisoned
        if (attacker != null && poisonTracking.ContainsKey(attacker))
        {
            var poisonData = poisonTracking[attacker];
            if (poisonData.stacks > 0 && poisonData.source != null && HasAssassinPerk(poisonData.source, 50))
            {
                return damage * (1f - TWIST_KNIFE_DAMAGE_REDUCTION);
            }
        }

        return damage;
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

    /// <summary>
    /// Check if a hit is from stealth (enemy was unaware)
    /// </summary>
    public static bool IsStealthHit(Player attacker, Character target)
    {
        if (attacker == null || target == null) return false;

        // Check if player is sneaking
        if (!attacker.IsSneaking()) return false;

        // Check if target is unaware (not alerted)
        var baseAI = target.GetBaseAI();
        if (baseAI == null) return false;

        // Check alert status
        return !baseAI.IsAlerted() && !baseAI.HaveTarget();
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

    /// <summary>
    /// Clean up expired buffs
    /// </summary>
    public static void CleanupBuffs()
    {
        float currentTime = Time.time;

        // Clean up assassination speed buffs
        var expiredBuffs = assassinationBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredBuffs)
        {
            assassinationBuffs.Remove(playerID);

            // Remove visual effect
            var player = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == playerID);
            if (player != null)
            {
                player.GetSEMan()?.RemoveStatusEffect("SE_AssassinationSpeed".GetStableHashCode(), quiet: true);
            }
        }
    }
    #endregion
}

/// <summary>
/// Harmony patches to integrate Assassin perks with game systems
/// </summary>
[HarmonyPatch]
public static class AssassinPerkPatches
{
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
            if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

            // Only apply to knife damage
            var weapon = player.GetCurrentWeapon();
            if (!ClassCombatManager.IsKnifeWeapon(weapon)) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin)) return;

            float originalDamage = hit.GetTotalDamage();

            // Apply Cutthroat damage bonus (Level 10)
            float modifiedDamage = AssassinPerkManager.ApplyLv10_CutthroatDamage(player, originalDamage);

            // Check for backstab and apply bonus (Level 10)
            if (AssassinPerkManager.IsBackstab(hit.m_point, __instance))
            {
                float backstabBonus = hit.m_backstabBonus;
                backstabBonus = AssassinPerkManager.ApplyLv10_CutthroatBackstab(player, backstabBonus);
                modifiedDamage *= backstabBonus;

                // Visual feedback
                player.Message(MessageHud.MessageType.TopLeft, "Backstab!");
            }

            // Check for stealth hit and apply Assassination (Level 40)
            bool isStealthHit = AssassinPerkManager.IsStealthHit(player, __instance);
            if (isStealthHit)
            {
                modifiedDamage = AssassinPerkManager.ApplyLv40_AssassinationDamage(player, modifiedDamage);
            }

            // Apply Twist the Knife damage bonus to poisoned targets (Level 50)
            modifiedDamage = AssassinPerkManager.ApplyLv50_TwistKnifeDamageBonus(player, __instance, modifiedDamage);

            // Apply the modified damage
            if (modifiedDamage != originalDamage)
            {
                float multiplier = modifiedDamage / originalDamage;
                hit.m_damage.m_damage *= multiplier;
                hit.m_damage.m_slash *= multiplier;
                hit.m_damage.m_pierce *= multiplier;
            }

            // Apply Twist the Knife damage reduction from poisoned enemies (Level 50)
            // TODO: This section is not right.
            //float incomingDamage = hit.GetTotalDamage();
            //incomingDamage = AssassinPerkManager.ApplyLv50_TwistKnifeDamageReduction(__instance, incomingDamage);
            //if (incomingDamage != hit.GetTotalDamage())
            //{
            //    float reductionMultiplier = incomingDamage / hit.GetTotalDamage();
            //    hit.m_damage.m_damage *= reductionMultiplier;
            //    hit.m_damage.m_slash *= reductionMultiplier;
            //    hit.m_damage.m_pierce *= reductionMultiplier;
            //    hit.m_damage.m_blunt *= reductionMultiplier;
            //}

            // TODO: Add poison damage to hit.
            // hit = AssassinPerkManager.ApplyLv20_VenomCoating(player, __instance, hit);
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

            Logger.LogInfo($"[POISON LOGGING] SlashDamage: {hit.m_damage.m_slash}");
            Logger.LogInfo($"[POISON LOGGING] PierceDamage: {hit.m_damage.m_pierce}");
            Logger.LogInfo($"[POISON LOGGING] BluntDamage: {hit.m_damage.m_blunt}");
            AssassinPerkManager.ApplyLv20_VenomCoating(player, __instance, hit);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Assassin_Postfix: {ex.Message}");
        }
    }

    //[HarmonyPatch(typeof(HitData), "OnHit")]
    //[HarmonyPostfix]
    //public static void HitData_OnHit_Postfix()
    //{
    //    try
    //    {
    //        //
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogError($"Error occured in HitData_OnHit_Postfix: {ex.Message}");
    //    }
    //}
    #endregion

    #region Movement Speed Patches
    /// <summary>
    /// Apply Envenomous movement slow to poisoned enemies
    /// </summary>
    //[HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
    //[HarmonyPostfix]
    //public static void Character_GetRunSpeedFactor_Assassin_Postfix(Character __instance, ref float __result)
    //{
    //    try
    //    {
    //        if (__instance == null || __instance is Player) return;

    //        __result = AssassinPerkManager.ApplyLv30_EnvenomousMovementSlow(__instance, __result);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogError($"Error in Character_GetRunSpeedFactor_Assassin_Postfix: {ex.Message}");
    //    }
    //}
    #endregion

    #region Attack Speed Patches
    /// <summary>
    /// Apply Assassination attack speed bonus
    /// </summary>
    //[HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
    //[HarmonyPostfix]
    //public static void Attack_DoMeleeAttack_Prefix(Attack attack)
    //{
    //    try
    //    {
    //        var player = Player.m_localPlayer;
    //        if (player == null) return;

    //        // TODO: Get Attack Base Speed.

    //        //TODO: Update the speed based with AssassinationAttackSpeed.
    //        var speedBuff = AssassinPerkManager.ApplyLv40_AssassinationAttackSpeed(player, baseSpeed);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogError($"Error in Player_GetAttackSpeedFactorMovement_Assassin_Postfix: {ex.Message}");
    //    }
    //}
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
            AssassinPerkManager.ProcessPoisonDamage();

            // Clean up buffs every second
            if (Time.time % 1f < Time.deltaTime)
            {
                AssassinPerkManager.CleanupBuffs();
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Assassin_Postfix: {ex.Message}");
        }
    }
    #endregion
}

/// <summary>
/// Console commands for testing Assassin perks
/// </summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class AssassinPerkCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testassassinperk", "Test specific assassin perk",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Assassin))
                {
                    args.Context.AddString("Assassin class not active!");
                    return;
                }

                args.Context.AddString("Assassin perks are applied automatically during combat");
                args.Context.AddString("Try attacking with a knife to test poison and other effects");
            }
        );

        new Terminal.ConsoleCommand("assassinstatus", "Show current assassin perk status",
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

                int assassinLevel = playerData.GetClassLevel(PlayerClass.Assassin);
                bool isActive = playerData.IsClassActive(PlayerClass.Assassin);

                args.Context.AddString($"=== Assassin Status ===");
                args.Context.AddString($"Class Active: {isActive}");
                args.Context.AddString($"Assassin Level: {assassinLevel}");
                args.Context.AddString($"Knife Skill: {Player.m_localPlayer.GetSkillLevel(Skills.SkillType.Knives):F1}");
                args.Context.AddString("");

                args.Context.AddString("Available Perks:");
                if (assassinLevel >= 10) args.Context.AddString("✓ Lv10 - Cutthroat: +15% knife damage, +30% backstab");
                else args.Context.AddString("✗ Lv10 - Cutthroat: Not unlocked");

                if (assassinLevel >= 20) args.Context.AddString("✓ Lv20 - Venom Coating: Apply stacking poison");
                else args.Context.AddString("✗ Lv20 - Venom Coating: Not unlocked");

                if (assassinLevel >= 30) args.Context.AddString("✓ Lv30 - Envenomous: Poison slows movement");
                else args.Context.AddString("✗ Lv30 - Envenomous: Not unlocked");

                if (assassinLevel >= 40) args.Context.AddString("✓ Lv40 - Assassination: +100% stealth damage, +25% speed");
                else args.Context.AddString("✗ Lv40 - Assassination: Not unlocked");

                if (assassinLevel >= 50) args.Context.AddString("✓ Lv50 - Twist the Knife: +25% vs poisoned, -10% their damage");
                else args.Context.AddString("✗ Lv50 - Twist the Knife: Not unlocked");

                // Show current weapon compatibility
                var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                args.Context.AddString("");
                if (currentWeapon != null)
                {
                    bool isKnife = ClassCombatManager.IsKnifeWeapon(currentWeapon);
                    args.Context.AddString($"Current weapon: {currentWeapon.m_shared.m_name}");
                    args.Context.AddString($"Weapon compatible: {(isKnife ? "Yes (Knife)" : "No (Not a knife)")}");
                }
                else
                {
                    args.Context.AddString("No weapon equipped");
                }

                args.Context.AddString("");
                args.Context.AddString($"Sneaking: {Player.m_localPlayer.IsSneaking()}");
            }
        );
    }
}