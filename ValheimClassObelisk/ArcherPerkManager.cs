using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

/// <summary>
/// Archer class perk implementation using direct field modification approach
/// </summary>
public static class ArcherPerkManager
{
    // Buff tracking for temporary effects
    private static Dictionary<long, float> arrowSlingerBuffs = new Dictionary<long, float>(); // playerID -> buff end time
    private static Dictionary<long, int> adrenalineRushStacks = new Dictionary<long, int>(); // playerID -> consecutive hits
    private static Dictionary<long, float> lastHitTimes = new Dictionary<long, float>(); // playerID -> last hit time

    // Configuration
    public const float ARROW_SLINGER_DURATION = 10f;
    public const float ADRENALINE_RUSH_TIMEOUT = 3f; // Reset stacks if no hit within 3 seconds

    /// <summary>
    /// Apply all archer perks to a bow weapon based on player's level
    /// </summary>
    public static void ApplyArcherPerks(Player player, ItemDrop.ItemData bowWeapon)
    {
        if (player == null || bowWeapon == null || !ClassCombatManager.IsBowWeapon(bowWeapon)) return;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || !playerData.IsClassActive("Archer")) return;

        int archerLevel = playerData.GetClassLevel("Archer");
        var attack = bowWeapon.m_shared.m_attack;
        if (attack == null) return;

        // Store original values if not already stored
        if (!originalDrawStaminaDrain.ContainsKey(bowWeapon))
        {
            originalDrawStaminaDrain[bowWeapon] = attack.m_drawStaminaDrain;
        }
        if (!originalDrawDuration.ContainsKey(bowWeapon))
        {
            originalDrawDuration[bowWeapon] = attack.m_drawDurationMin;
        }

        // Reset to original values before applying perks
        attack.m_drawStaminaDrain = originalDrawStaminaDrain[bowWeapon];
        attack.m_drawDurationMin = originalDrawDuration[bowWeapon];

        // Apply perks based on level
        ApplyLv10_SteadyDraw(player, attack, archerLevel);
        ApplyLv20_ArrowSlinger(player, attack, archerLevel);

        Debug.Log($"Applied Archer perks for {player.GetPlayerName()} (Level {archerLevel})");
    }

    // Store original weapon values to restore them
    private static Dictionary<ItemDrop.ItemData, float> originalDrawStaminaDrain = new Dictionary<ItemDrop.ItemData, float>();
    private static Dictionary<ItemDrop.ItemData, float> originalDrawDuration = new Dictionary<ItemDrop.ItemData, float>();

    #region Level 10 - Steady Draw
    /// <summary>
    /// Lv10 – Steady Draw: -15% stamina drain while drawing
    /// </summary>
    private static void ApplyLv10_SteadyDraw(Player player, Attack attack, int archerLevel)
    {
        if (archerLevel < 10) return;

        // Reduce stamina drain by 15%
        attack.m_drawStaminaDrain *= 0.85f;

        Debug.Log($"Applied Steady Draw: {attack.m_drawStaminaDrain:F1} stamina drain (was {originalDrawStaminaDrain.Values.FirstOrDefault():F1})");
    }
    #endregion

    #region Level 20 - Arrow Slinger
    /// <summary>
    /// Lv20 – Arrow Slinger: Arrows give a buff on hit that reduces draw time by 50% for 10 seconds
    /// </summary>
    private static void ApplyLv20_ArrowSlinger(Player player, Attack attack, int archerLevel)
    {
        if (archerLevel < 20) return;

        long playerID = player.GetPlayerID();

        // Check if player has active Arrow Slinger buff
        if (arrowSlingerBuffs.ContainsKey(playerID) && Time.time < arrowSlingerBuffs[playerID])
        {
            // Apply 50% faster draw speed
            attack.m_drawDurationMin *= 0.5f;

            // Show buff indicator occasionally
            if (Random.Range(0f, 1f) < 0.1f) // 10% chance per application
            {
                player.Message(MessageHud.MessageType.TopLeft, "Arrow Slinger Active!");
            }

            Debug.Log($"Applied Arrow Slinger buff: {attack.m_drawDurationMin:F2}s draw time (50% faster)");
        }
    }

    /// <summary>
    /// Trigger Arrow Slinger buff when arrow hits target
    /// </summary>
    public static void TriggerArrowSlingerBuff(Player archer)
    {
        if (archer == null) return;

        var playerData = PlayerClassManager.GetPlayerData(archer);
        if (playerData == null || !playerData.IsClassActive("Archer")) return;

        int archerLevel = playerData.GetClassLevel("Archer");
        if (archerLevel < 20) return;

        long playerID = archer.GetPlayerID();
        float buffEndTime = Time.time + ARROW_SLINGER_DURATION;

        // Extend existing buff or create new one
        if (!arrowSlingerBuffs.ContainsKey(playerID) || arrowSlingerBuffs[playerID] < buffEndTime)
        {
            arrowSlingerBuffs[playerID] = buffEndTime;
            archer.Message(MessageHud.MessageType.TopLeft, "Arrow Slinger: 50% faster draw for 10s!");
            Debug.Log($"Triggered Arrow Slinger buff for {archer.GetPlayerName()}");
        }
    }
    #endregion

    #region Level 30 - Wind Reader
    /// <summary>
    /// Lv30 – Wind Reader: +15% damage beyond 25m travel distance; -25% stamina while aiming
    /// </summary>
    public static float ApplyLv30_WindReaderDamage(Player archer, Vector3 shotOrigin, Vector3 hitPoint, float baseDamage)
    {
        if (archer == null) return baseDamage;

        var playerData = PlayerClassManager.GetPlayerData(archer);
        if (playerData == null || !playerData.IsClassActive("Archer")) return baseDamage;

        int archerLevel = playerData.GetClassLevel("Archer");
        if (archerLevel < 30) return baseDamage;

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
    /// Apply Wind Reader stamina reduction while aiming
    /// </summary>
    public static float ApplyLv30_WindReaderStamina(Player archer, float staminaDrain)
    {
        if (archer == null) return staminaDrain;

        var playerData = PlayerClassManager.GetPlayerData(archer);
        if (playerData == null || !playerData.IsClassActive("Archer")) return staminaDrain;

        int archerLevel = playerData.GetClassLevel("Archer");
        if (archerLevel < 30) return staminaDrain;

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
    /// </summary>
    public static bool ShouldConsumeArrow(Player archer)
    {
        if (archer == null) return true;

        var playerData = PlayerClassManager.GetPlayerData(archer);
        if (playerData == null || !playerData.IsClassActive("Archer")) return true;

        int archerLevel = playerData.GetClassLevel("Archer");
        if (archerLevel < 40) return true;

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
    /// Lv50 – Adrenaline Rush: Consecutive hits return 5% stamina
    /// </summary>
    public static void TriggerAdrenalineRush(Player archer)
    {
        if (archer == null) return;

        var playerData = PlayerClassManager.GetPlayerData(archer);
        if (playerData == null || !playerData.IsClassActive("Archer")) return;

        int archerLevel = playerData.GetClassLevel("Archer");
        if (archerLevel < 50) return;

        long playerID = archer.GetPlayerID();
        float currentTime = Time.time;

        // Check if this is a consecutive hit (within timeout)
        bool isConsecutive = lastHitTimes.ContainsKey(playerID) &&
                           (currentTime - lastHitTimes[playerID]) <= ADRENALINE_RUSH_TIMEOUT;

        if (isConsecutive)
        {
            // Increment stack count
            adrenalineRushStacks[playerID] = adrenalineRushStacks.ContainsKey(playerID)
                ? adrenalineRushStacks[playerID] + 1
                : 1;
        }
        else
        {
            // Reset stacks if gap too long
            adrenalineRushStacks[playerID] = 1;
        }

        // Update last hit time
        lastHitTimes[playerID] = currentTime;

        // Restore stamina (5% of max stamina per hit)
        float maxStamina = archer.GetMaxStamina();
        float staminaRestore = maxStamina * 0.05f;

        archer.AddStamina(staminaRestore);

        int stackCount = adrenalineRushStacks[playerID];

        // Show message with stack count
        archer.Message(MessageHud.MessageType.TopLeft,
            $"Adrenaline Rush! +{staminaRestore:F0} stamina (Hit #{stackCount})");

        Debug.Log($"Adrenaline Rush: Restored {staminaRestore:F1} stamina for {archer.GetPlayerName()} (hit #{stackCount})");
    }
    #endregion

    #region Update and Cleanup
    /// <summary>
    /// Clean up expired buffs and stacks
    /// </summary>
    public static void UpdateBuffs()
    {
        float currentTime = Time.time;

        // Clean up expired Arrow Slinger buffs
        var expiredBuffs = arrowSlingerBuffs.Where(kvp => kvp.Value < currentTime).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredBuffs)
        {
            arrowSlingerBuffs.Remove(playerID);
        }

        // Clean up old hit tracking for Adrenaline Rush
        var expiredHits = lastHitTimes.Where(kvp => (currentTime - kvp.Value) > ADRENALINE_RUSH_TIMEOUT).Select(kvp => kvp.Key).ToList();
        foreach (var playerID in expiredHits)
        {
            lastHitTimes.Remove(playerID);
            adrenalineRushStacks.Remove(playerID);
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
    #region Weapon Equip/Update Patches
    /// <summary>
    /// Apply archer perks when weapon is equipped or updated
    /// </summary>
    [HarmonyPatch(typeof(Player), "FixedUpdate")]
    [HarmonyPostfix]
    public static void Player_FixedUpdate_Postfix(Player __instance)
    {
        try
        {
            // Only run this occasionally to avoid performance issues
            if (Time.fixedTime % 1f < Time.fixedDeltaTime) // Roughly once per second
            {
                if (__instance != null && __instance == Player.m_localPlayer)
                {
                    var currentWeapon = __instance.GetCurrentWeapon();
                    if (currentWeapon != null && ClassCombatManager.IsBowWeapon(currentWeapon))
                    {
                        ArcherPerkManager.ApplyArcherPerks(__instance, currentWeapon);
                    }

                    // Clean up expired buffs
                    ArcherPerkManager.UpdateBuffs();
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_FixedUpdate_Postfix (Archer): {ex.Message}");
        }
    }
    #endregion

    #region Projectile Hit Patches
    /// <summary>
    /// Trigger archer perks when projectiles hit targets
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

            // Check if archer has Archer class active
            var playerData = PlayerClassManager.GetPlayerData(archer);
            if (playerData == null || !playerData.IsClassActive("Archer")) return;

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
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Prefix_ArcherPerks(Character __instance, ref HitData hit)
    {
        try
        {
            if (hit.GetAttacker() is Player archer && __instance != null && !(__instance is Player))
            {
                // Check if this is a projectile hit (arrows/bolts)
                if (IsProjectileDamage(hit))
                {
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
    /// Apply Wind Reader stamina reduction while aiming
    /// </summary>
    [HarmonyPatch(typeof(Player), "UseStamina")]
    [HarmonyPrefix]
    public static void Player_UseStamina_Prefix(Player __instance, ref float v)
    {
        try
        {
            if (__instance != null && __instance == Player.m_localPlayer)
            {
                // Apply Wind Reader stamina reduction if aiming bow
                v = ArcherPerkManager.ApplyLv30_WindReaderStamina(__instance, v);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Player_UseStamina_Prefix (Archer): {ex.Message}");
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
}

/// <summary>
/// Console commands for testing Archer perks
/// </summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class ArcherPerkCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testarcherperk", "Test specific archer perk (testarcherperk [level])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData == null || !playerData.IsClassActive("Archer"))
                {
                    args.Context.AddString("Archer class not active!");
                    return;
                }

                if (args.Length < 2 || !int.TryParse(args.Args[1], out int testLevel))
                {
                    args.Context.AddString("Usage: testarcherperk [level]");
                    args.Context.AddString("Levels: 10, 20, 30, 40, 50");
                    return;
                }

                switch (testLevel)
                {
                    case 20:
                        ArcherPerkManager.TriggerArrowSlingerBuff(Player.m_localPlayer);
                        args.Context.AddString("Triggered Arrow Slinger buff (10 seconds)");
                        break;
                    case 50:
                        ArcherPerkManager.TriggerAdrenalineRush(Player.m_localPlayer);
                        args.Context.AddString("Triggered Adrenaline Rush (+5% stamina)");
                        break;
                    default:
                        args.Context.AddString($"No direct test for level {testLevel}");
                        args.Context.AddString("Available tests: 20 (Arrow Slinger), 50 (Adrenaline Rush)");
                        break;
                }
            }
        );

        new Terminal.ConsoleCommand("archerstatus", "Show current archer perk status",
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

                int archerLevel = playerData.GetClassLevel("Archer");
                bool isActive = playerData.IsClassActive("Archer");

                args.Context.AddString($"=== Archer Status ===");
                args.Context.AddString($"Class Active: {isActive}");
                args.Context.AddString($"Archer Level: {archerLevel}");

                if (archerLevel >= 10) args.Context.AddString("✓ Lv10 - Steady Draw: -15% stamina drain");
                if (archerLevel >= 20) args.Context.AddString("✓ Lv20 - Arrow Slinger: 50% faster draw on hit");
                if (archerLevel >= 30) args.Context.AddString("✓ Lv30 - Wind Reader: +15% damage >25m, -25% aim stamina");
                if (archerLevel >= 40) args.Context.AddString("✓ Lv40 - Magic Shot: 50% chance no arrow consumed");
                if (archerLevel >= 50) args.Context.AddString("✓ Lv50 - Adrenaline Rush: +5% stamina per hit");

                var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                if (currentWeapon != null && ClassCombatManager.IsBowWeapon(currentWeapon))
                {
                    args.Context.AddString($"Current bow: {currentWeapon.m_shared.m_name}");
                    if (currentWeapon.m_shared.m_attack != null)
                    {
                        args.Context.AddString($"Draw stamina: {currentWeapon.m_shared.m_attack.m_drawStaminaDrain:F1}");
                        args.Context.AddString($"Draw time: {currentWeapon.m_shared.m_attack.m_drawDurationMin:F2}s");
                    }
                }
            }
        );
    }
}