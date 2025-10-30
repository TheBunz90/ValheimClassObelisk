using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace ValheimClassObelisk
{
    [HarmonyPatch]
    public static class BulwarkPerkManager
    {
        // Initialize Constants
        private static float SHIELD_WALL_BLOCK_POWER = 0.15f; // 15% block power bonus
        private static float SHIELD_WALL_STAMINA_REDUCTION = 0.15f; // 15% stamina reduction
        private static float PERFECT_GUARD_STAMINA_RESTORE = 5f; // 5 stamina restored
        private static float PERFECT_GUARD_STAMINA_REDUCTION = 0.5f; // 50% block stamina reduction
        private static float TOWERING_PRESENCE_TOWER_BONUS = 0.25f; // Additional 25% block power for tower shields
        private static float THORNS_DAMAGE_RETURN = 0.5f; // 50% damage returned
        private static float THORNS_STAGGER_MULTIPLIER = 1.2f; // 20% increased stagger damage
        private static float REVERB_DAMAGE_THRESHOLD = 200f; // 200 damage threshold
        private static float REVERB_SHOCKWAVE_DAMAGE = 200f; // 200 blunt damage
        private static float REVERB_RANGE = 5f; // 5 meter range
        private static float REVERB_COOLDOWN = 10f; // 10 second cooldown

        // Initialize trackers
        private static Dictionary<Player, float> _accumulatedBlockDamage = new Dictionary<Player, float>();
        private static Dictionary<Player, float> _lastReverbTime = new Dictionary<Player, float>();


        private static MethodInfo _getCurrentBlockerMethod;

        // Icon Resources (you'll need to add these to your project)
        //private const string SHIELD_WALL_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.ShieldWall.rgba";
        //private const string PERFECT_GUARD_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.PerfectGuard.rgba";
        //private const string TOWERING_PRESENCE_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.ToweringPresence.rgba";
        //private const string THORNS_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.Thorns.rgba";
        private const string REVERB_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.Reverb_sprite.rgba";

        // Sprites
        //private static Sprite _cachedShieldWallIcon;
        //private static Sprite _cachedPerfectGuardIcon;
        //private static Sprite _cachedToweringPresenceIcon;
        //private static Sprite _cachedThornsIcon;
        private static Sprite _cachedReverbIcon;

        #region Load Resource Classes
        private static Sprite GetReverbIcon()
        {
            if (_cachedReverbIcon != null) return _cachedReverbIcon;
            return LoadIconFromResource(REVERB_ICON_RESOURCE, "Reverb", ref _cachedReverbIcon);
        }

        private static Sprite LoadIconFromResource(string resourceName, string iconType, ref Sprite cachedSprite)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s == null)
                    {
                        Jotunn.Logger.LogWarning($"[Bulwark] Embedded icon not found: {resourceName}");
                        return null;
                    }

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogError($"[Bulwark] Failed to read icon header for {iconType}");
                        return null;
                    }

                    int width = BitConverter.ToInt32(header, 0);
                    int height = BitConverter.ToInt32(header, 4);

                    // Read RGBA data
                    byte[] rgbaData = new byte[width * height * 4];
                    read = s.Read(rgbaData, 0, rgbaData.Length);
                    if (read != rgbaData.Length)
                    {
                        Jotunn.Logger.LogError($"[Bulwark] Failed to read icon data for {iconType}");
                        return null;
                    }

                    // Create texture
                    Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.LoadRawTextureData(rgbaData);
                    texture.Apply();

                    // Create sprite
                    cachedSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
                    Jotunn.Logger.LogInfo($"[Bulwark] Successfully loaded {iconType} icon ({width}x{height})");

                    return cachedSprite;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Bulwark] Error loading {iconType} icon: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region Bulwark Service Classes
        public static bool HasBulwarkPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Bulwark)) return false;

            return playerData.GetClassLevel(PlayerClass.Bulwark) >= requiredLevel;
        }

        public static float ApplyShieldWallBlockPower(float originalBlockPower, ItemDrop.ItemData shield)
        {
            // Apply 15% block power bonus
            float mult = 1f + SHIELD_WALL_BLOCK_POWER;
            return originalBlockPower * mult;
        }

        public static float ApplyShieldWallStamina(float staminaCost)
        {
            // Reduce stamina cost by 15%
            return staminaCost * (1f - SHIELD_WALL_STAMINA_REDUCTION);
        }

        public static float ApplyPerfectGuardStamina(float staminaCost)
        {
            // Reduce block stamina cost by 50%
            return staminaCost * (1f - PERFECT_GUARD_STAMINA_REDUCTION);
        }

        public static void RestorePerfectGuardStamina(Player player)
        {
            if (player == null) return;
            player.AddStamina(5);
        }

        public static float ApplyToweringPresenceBlockPower(float originalBlockPower, ItemDrop.ItemData shield)
        {
            if (shield == null) return originalBlockPower;

            // Check if it's a tower shield (you may need to adjust this condition based on your shield naming)
            bool isTowerShield = shield.m_shared.m_name.ToLower().Contains("tower") ||
                                shield.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield &&
                                shield.m_shared.m_blockPower >= 50; // Assuming tower shields have high block power

            if (isTowerShield)
            {
                float mult = 1f + TOWERING_PRESENCE_TOWER_BONUS;
                return originalBlockPower * mult;
            }

            return originalBlockPower;
        }

        public static void ApplyThornsReflection(Player player, Character attacker, float originalDamage)
        {
            if (player == null || attacker == null || attacker == player) return;

            // Calculate reflected damage
            float reflectedDamage = originalDamage * THORNS_DAMAGE_RETURN;

            // Create damage data for reflection
            HitData thornsDamage = new HitData();
            thornsDamage.m_damage.m_blunt = reflectedDamage;
            thornsDamage.m_point = attacker.GetCenterPoint();
            thornsDamage.m_dir = (attacker.transform.position - player.transform.position).normalized;
            thornsDamage.m_staggerMultiplier = THORNS_STAGGER_MULTIPLIER;
            thornsDamage.m_attacker = player.GetZDOID();
            thornsDamage.m_skill = Skills.SkillType.Blocking;

            // Apply reflected damage
            attacker.Damage(thornsDamage);

            Logger.LogInfo($"[Bulwark] {player.GetPlayerName()} reflected {reflectedDamage:F1} damage to {attacker.GetHoverName()}");
        }

        public static void AccumulateReverbDamage(Player player, float blockedDamage)
        {
            if (player == null) return;

            var seman = player.GetSEMan();

            if (seman == null) return;

            var reverbEffect = "SE_ReverbCharged".GetStableHashCode();

            if (seman.HaveStatusEffect(reverbEffect))
            {
                TriggerReverbShockwave(player);
            }
            else
            {

                if (!_accumulatedBlockDamage.ContainsKey(player))
                {
                    _accumulatedBlockDamage[player] = 0f;
                }

                _accumulatedBlockDamage[player] += blockedDamage;

                // Check if threshold is reached and cooldown is available
                if (_accumulatedBlockDamage[player] >= REVERB_DAMAGE_THRESHOLD && IsReverbAvailable(player))
                {
                    ApplyReverbChargedEffect(player);
                }
            }
        }

        private static bool IsReverbAvailable(Player player)
        {
            if (!_lastReverbTime.ContainsKey(player)) return true;

            return Time.time >= _lastReverbTime[player] + REVERB_COOLDOWN;
        }

        public static void TriggerReverbShockwave(Player player)
        {
            if (player == null) return;

            Vector3 playerPosition = player.transform.position;

            // Find all creatures within 5 meter range, excluding players and structures
            var nearbyCharacters = Character.GetAllCharacters()
                .Where(c => c != null && c != player && !c.IsDead() &&
                           !(c is Player) && // Exclude other players  
                           (c.GetComponent<MonsterAI>() != null || c.GetComponent<AnimalAI>() != null) && // Only creatures
                           Vector3.Distance(c.transform.position, playerPosition) <= REVERB_RANGE)
                .ToList();

            foreach (var target in nearbyCharacters)
            {
                // Create shockwave damage
                HitData shockwaveDamage = new HitData();
                shockwaveDamage.m_damage.m_blunt = REVERB_SHOCKWAVE_DAMAGE;
                shockwaveDamage.m_point = target.GetCenterPoint();
                shockwaveDamage.m_dir = (target.transform.position - playerPosition).normalized;
                shockwaveDamage.m_pushForce = 30f; // Increased knockback for shockwave effect
                shockwaveDamage.m_attacker = player.GetZDOID();
                shockwaveDamage.m_skill = Skills.SkillType.Blocking;

                target.Damage(shockwaveDamage);
            }
            _accumulatedBlockDamage[player] = 0f; // Reset accumulator
            _lastReverbTime[player] = Time.time; // Set cooldown

            var seman = player.GetSEMan();

            var statusEffect = "SE_ReverbCharged".GetStableHashCode();
            if (seman != null) seman.RemoveStatusEffect(statusEffect);

            Logger.LogInfo($"[Bulwark] {player.GetPlayerName()} triggered Reverb shockwave, hitting {nearbyCharacters.Count} enemies within 5m");
        }

        public static void ApplyReverbChargedEffect(Player player)
        {
            if (player == null) return;

            SEMan seman = player.GetSEMan();
            string statusName = "SE_ReverbCharged";

            var statusEffect = ScriptableObject.CreateInstance<SE_ReverbCharged>();
            statusEffect.name = statusName;
            statusEffect.m_name = "Reverb Charged";
            statusEffect.m_tooltip = "Ready to unleash shockwave energy";
            statusEffect.m_ttl = 0f; // Brief visual effect
            statusEffect.m_icon = GetReverbIcon();

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static ItemDrop.ItemData GetCurrentBlocker(Player player)
        {
            if (player == null) return null;

            try
            {
                // Cache the reflection method if not already cached
                if (_getCurrentBlockerMethod == null)
                {
                    _getCurrentBlockerMethod = typeof(Humanoid).GetMethod("GetCurrentBlocker",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (_getCurrentBlockerMethod == null)
                    {
                        Logger.LogError("[Bulwark] Could not find GetCurrentBlocker method via reflection");
                        return null;
                    }
                }

                // Call the private method via reflection
                return _getCurrentBlockerMethod.Invoke(player, null) as ItemDrop.ItemData;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[Bulwark] Error calling GetCurrentBlocker via reflection: {ex.Message}");
                return null;
            }
        }

        public static bool IsShieldEquipped(Player player)
        {
            if (player == null) return false;

            var blocker = GetCurrentBlocker(player);
            return blocker != null && blocker.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
        }
        #endregion

        #region Harmony Patches

        // Summary
        // Apply damage modifications and trigger effects when player takes damage while blocking
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Bulwark_Damage_Prefix(Character __instance, HitData hit)
        {
            try
            {
                if (__instance == null || hit == null || !(__instance is Player player)) return;

                // Only apply if player has shield equipped and is blocking
                if (!IsShieldEquipped(player) || !player.IsBlocking()) return;

                var playerData = PlayerClassManager.GetPlayerData(player);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Bulwark)) return;

                // Get the attacker
                Character attacker = null;
                if (hit.m_attacker != ZDOID.None)
                {
                    var attackerObj = ZNetScene.instance.FindInstance(hit.m_attacker);
                    if (attackerObj != null)
                        attacker = attackerObj.GetComponent<Character>();
                }

                // Level 20 - Perfect Guard: Restore stamina on successful block
                if (HasBulwarkPerk(player, 20))
                {
                    RestorePerfectGuardStamina(player);
                }

                // Level 40 - Thorns: Reflect damage back to attacker
                if (HasBulwarkPerk(player, 40) && attacker != null)
                {
                    float totalDamage = hit.GetTotalDamage();
                    ApplyThornsReflection(player, attacker, totalDamage);
                }

                // Level 50 - Reverb: Accumulate blocked damage
                if (HasBulwarkPerk(player, 50))
                {
                    float blockedDamage = hit.GetTotalDamage();
                    AccumulateReverbDamage(player, blockedDamage);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_Damage_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply block power bonuses
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetBlockPower", new Type[] { typeof(float) })]
        [HarmonyPostfix]
        public static void Bulwark_BlockPower_Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            try
            {
                if (__instance == null || __instance.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) return;

                var player = Player.m_localPlayer;
                if (player == null) return;

                var playerData = PlayerClassManager.GetPlayerData(player);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Bulwark)) return;

                // Level 10 - Shield Wall: +15% Block Power
                if (HasBulwarkPerk(player, 10))
                {
                    __result = ApplyShieldWallBlockPower(__result, __instance);
                }

                // Level 30 - Towering Presence: Additional +10% Block Power for tower shields
                if (HasBulwarkPerk(player, 30))
                {
                    __result = ApplyToweringPresenceBlockPower(__result, __instance);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_BlockPower_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Apply stamina cost reduction for blocking
        [HarmonyPatch(typeof(Player), "UseStamina")]
        [HarmonyPrefix]
        public static void Bulwark_UseStamina_Prefix(Player __instance, ref float v)
        {
            try
            {
                if (!IsShieldEquipped(__instance)) return;

                // Check if this stamina usage is from blocking (you may need to adjust this condition)
                if (__instance.IsBlocking())
                {
                    // Level 10 - Shield Wall: -15% block stamina cost
                    if (HasBulwarkPerk(__instance, 10))
                    {
                        v = ApplyShieldWallStamina(v);
                    }

                    // Level 20 - Perfect Guard: Additional -50% block stamina cost
                    if (HasBulwarkPerk(__instance, 20))
                    {
                        v = ApplyPerfectGuardStamina(v);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_UseStamina_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply bulwark buffs when player levels up
        [HarmonyPatch(typeof(ClassXPManager), "AwardDamageXP")]
        [HarmonyPostfix]
        public static void Bulwark_LevelUp_Postfix(Player player, ItemDrop.ItemData weapon, float damageDealt, Character target)
        {
            try
            {
                // Apply all bulwark passive buffs after potential level up
                ApplyAllBulwarkPassiveBuffs(player);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_LevelUp_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Apply bulwark buffs when player spawns
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        public static void Bulwark_PlayerSpawn_Postfix(Player __instance)
        {
            try
            {
                // Apply all bulwark passive buffs on spawn
                ApplyAllBulwarkPassiveBuffs(__instance);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_PlayerSpawn_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Method to apply all bulwark passive buffs based on current level
        private static void ApplyAllBulwarkPassiveBuffs(Player player)
        {
            if (player == null) return;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Bulwark)) return;

            // Initialize damage accumulator if needed
            if (!_accumulatedBlockDamage.ContainsKey(player))
            {
                _accumulatedBlockDamage[player] = 0f;
            }

            // Future passive buffs can be added here for other levels
            // Currently most Bulwark perks are reactive/situational rather than persistent buffs
        }
        #endregion

        #region Status Effects
        public class SE_ReverbCharged : SE_Stats
        {
            public override void Setup(Character character)
            {
                base.Setup(character);
                m_ttl = 0f;
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);

                // Add visual effects here if desired
                // For example, particle effects around the player
            }
        }

        public class SE_PerfectGuard : SE_Stats
        {
            public override void Setup(Character character)
            {
                base.Setup(character);
                m_ttl = 1.0f; // Brief status effect
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                // Add visual feedback for perfect guard
            }
        }
        #endregion
    }
}