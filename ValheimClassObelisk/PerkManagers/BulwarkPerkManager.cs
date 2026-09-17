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
        // Configuration
        private const float SHIELD_WALL_BLOCK_POWER = 0.08f;
        private const float SHIELD_WALL_STAMINA_REDUCTION = 0.08f;
        private const float PERFECT_GUARD_STAMINA_RESTORE = 8f;
        private const float PERFECT_GUARD_EMPOWER_WINDOW = 4f;
        private const float PERFECT_GUARD_STAGGER_BONUS = 0.20f;
        private const float THORNS_PIERCE_PERCENT = 0.20f;
        private const float REVERB_DAMAGE_THRESHOLD = 200f;
        private const float REVERB_SHOCKWAVE_DAMAGE = 200f;
        private const float REVERB_RANGE = 5f;
        private const float REVERB_COOLDOWN = 10f;
        private const float REVERB_STAGGER_MULTIPLIER = 2.5f; // "high stagger"

        // Per-player trackers
        private static readonly Dictionary<Player, float> _accumulatedBlockDamage = new Dictionary<Player, float>();
        private static readonly Dictionary<Player, float> _lastReverbTime = new Dictionary<Player, float>();
        // Perfect Guard's empower window: the next outgoing hit within this time gets bonus stagger.
        private static readonly Dictionary<Player, float> _perfectGuardEmpowerUntil = new Dictionary<Player, float>();

        private static MethodInfo _getCurrentBlockerMethod;
        private static FieldInfo _blockTimerField;

        private const string REVERB_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.Reverb_sprite.rgba";
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

                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogError($"[Bulwark] Failed to read icon header for {iconType}");
                        return null;
                    }

                    int width = BitConverter.ToInt32(header, 0);
                    int height = BitConverter.ToInt32(header, 4);

                    byte[] rgbaData = new byte[width * height * 4];
                    read = s.Read(rgbaData, 0, rgbaData.Length);
                    if (read != rgbaData.Length)
                    {
                        Jotunn.Logger.LogError($"[Bulwark] Failed to read icon data for {iconType}");
                        return null;
                    }

                    Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    texture.LoadRawTextureData(rgbaData);
                    texture.Apply();

                    cachedSprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
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

        // Description metadata, shown in the class selection GUI - locked perks display as "???"
        private const string Intro = "Defensive specialists who control combat through shields, blocks, and retaliatory force.";
        private const string Outro = "Best for players who want shield progression to feel defensive first, then increasingly forceful and reactive.";

        public static readonly List<PerkInfo> Perks = new List<PerkInfo>
        {
            new PerkInfo { RequiredLevel = 10, Name = "Shield Wall", Description = "+8% block power and -8% block stamina cost." },
            new PerkInfo { RequiredLevel = 20, Name = "Shield Bearer", Description = "Shields impose no movement speed penalties." },
            new PerkInfo { RequiredLevel = 30, Name = "Perfect Guard", Description = "Blocking within the first moment of an incoming hit restores 8 stamina and empowers your next attack or shield bash within 4s for +20% stagger." },
            new PerkInfo { RequiredLevel = 40, Name = "Thorns", Description = "Blocked attacks return pierce damage equal to 20% of the original blocked damage." },
            new PerkInfo { RequiredLevel = 50, Name = "Reverb!", Description = "After blocking 200 damage, release a 5m shockwave dealing 200 blunt damage and high stagger. 10s cooldown." },
        };

        public static string GetClassDescription(Player player)
        {
            int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Bulwark) ?? 0;
            return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
        }

        public static float ApplyShieldWallBlockPower(float originalBlockPower)
        {
            return originalBlockPower * (1f + SHIELD_WALL_BLOCK_POWER);
        }

        public static float ApplyShieldWallStamina(float staminaCost)
        {
            return staminaCost * (1f - SHIELD_WALL_STAMINA_REDUCTION);
        }

        public static void ApplyThornsReflection(Player player, Character attacker, float originalDamage)
        {
            if (player == null || attacker == null || attacker == player) return;

            float reflectedDamage = originalDamage * THORNS_PIERCE_PERCENT;

            HitData thornsDamage = new HitData();
            thornsDamage.m_damage.m_pierce = reflectedDamage;
            thornsDamage.m_point = attacker.GetCenterPoint();
            thornsDamage.m_dir = (attacker.transform.position - player.transform.position).normalized;
            thornsDamage.m_attacker = player.GetZDOID();
            thornsDamage.m_skill = Skills.SkillType.Blocking;

            attacker.Damage(thornsDamage);
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
                return;
            }

            if (!_accumulatedBlockDamage.ContainsKey(player))
            {
                _accumulatedBlockDamage[player] = 0f;
            }

            _accumulatedBlockDamage[player] += blockedDamage;

            if (_accumulatedBlockDamage[player] >= REVERB_DAMAGE_THRESHOLD && IsReverbAvailable(player))
            {
                ApplyReverbChargedEffect(player);
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

            var nearbyCharacters = Character.GetAllCharacters()
                .Where(c => c != null && c != player && !c.IsDead() &&
                           !(c is Player) &&
                           (c.GetComponent<MonsterAI>() != null || c.GetComponent<AnimalAI>() != null) &&
                           Vector3.Distance(c.transform.position, playerPosition) <= REVERB_RANGE)
                .ToList();

            foreach (var target in nearbyCharacters)
            {
                HitData shockwaveDamage = new HitData();
                shockwaveDamage.m_damage.m_blunt = REVERB_SHOCKWAVE_DAMAGE;
                shockwaveDamage.m_point = target.GetCenterPoint();
                shockwaveDamage.m_dir = (target.transform.position - playerPosition).normalized;
                shockwaveDamage.m_pushForce = 30f;
                shockwaveDamage.m_attacker = player.GetZDOID();
                shockwaveDamage.m_skill = Skills.SkillType.Blocking;
                shockwaveDamage.m_staggerMultiplier = REVERB_STAGGER_MULTIPLIER;

                target.Damage(shockwaveDamage);
            }

            _accumulatedBlockDamage[player] = 0f;
            _lastReverbTime[player] = Time.time;

            var seman = player.GetSEMan();
            seman?.RemoveStatusEffect("SE_ReverbCharged".GetStableHashCode());
        }

        public static void ApplyReverbChargedEffect(Player player)
        {
            if (player == null) return;

            SEMan seman = player.GetSEMan();
            if (seman == null) return;

            var statusEffect = ScriptableObject.CreateInstance<SE_ReverbCharged>();
            statusEffect.name = "SE_ReverbCharged";
            statusEffect.m_name = "Reverb Charged";
            statusEffect.m_tooltip = "Ready to unleash shockwave energy";
            statusEffect.m_ttl = 0f;
            statusEffect.m_icon = GetReverbIcon();

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static ItemDrop.ItemData GetCurrentBlocker(Humanoid humanoid)
        {
            if (humanoid == null) return null;

            try
            {
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

                return _getCurrentBlockerMethod.Invoke(humanoid, null) as ItemDrop.ItemData;
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

        /// <summary>
        /// Same timed-block (parry) detection already established in SwordMasterPerkManager -
        /// a block counts as "perfect"/timed when the blocker has a timed-block bonus and the
        /// block landed within the first ~0.25s of the hit, matching vanilla's own parry window.
        /// </summary>
        private static bool WasTimedBlock(Humanoid humanoid, ItemDrop.ItemData blocker)
        {
            if (blocker == null) return false;

            if (_blockTimerField == null)
            {
                _blockTimerField = typeof(Humanoid).GetField("m_blockTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_blockTimerField == null)
                {
                    Logger.LogWarning("[Bulwark] Could not access m_blockTimer field - Perfect Guard's timed-block detection is disabled");
                    return false;
                }
            }

            float blockTimer = (float)_blockTimerField.GetValue(humanoid);
            return blocker.m_shared.m_timedBlockBonus > 1f && blockTimer != -1f && blockTimer < 0.25f;
        }
        #endregion

        #region Harmony Patches

        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Bulwark_Damage_Prefix(Character __instance, HitData hit)
        {
            try
            {
                if (__instance == null || hit == null || !(__instance is Player player)) return;

                if (!IsShieldEquipped(player) || !player.IsBlocking()) return;

                var playerData = PlayerClassManager.GetPlayerData(player);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Bulwark)) return;

                Character attacker = null;
                if (hit.m_attacker != ZDOID.None)
                {
                    var attackerObj = ZNetScene.instance.FindInstance(hit.m_attacker);
                    if (attackerObj != null) attacker = attackerObj.GetComponent<Character>();
                }

                // Level 40 - Thorns
                if (HasBulwarkPerk(player, 40) && attacker != null)
                {
                    ApplyThornsReflection(player, attacker, hit.GetTotalDamage());
                }

                // Level 50 - Reverb
                if (HasBulwarkPerk(player, 50))
                {
                    AccumulateReverbDamage(player, hit.GetTotalDamage());
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_Damage_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies Perfect Guard's empower window (Level 30) to the wielder's next outgoing
        /// attack or shield bash - consumed on use, not a flat ongoing buff.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Bulwark_PerfectGuardEmpower_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
                if (!HasBulwarkPerk(player, 30)) return;

                if (_perfectGuardEmpowerUntil.TryGetValue(player, out var until) && Time.time < until)
                {
                    hit.m_staggerMultiplier *= (1f + PERFECT_GUARD_STAGGER_BONUS);
                    _perfectGuardEmpowerUntil.Remove(player); // single use
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_PerfectGuardEmpower_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Perfect Guard's trigger (Level 30): a successful timed block/parry restores stamina
        /// and opens the empower window consumed above.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyPostfix]
        public static void Humanoid_BlockAttack_Bulwark_Postfix(Humanoid __instance, bool __result, HitData hit, Character attacker)
        {
            try
            {
                if (!__result || !(__instance is Player player)) return;
                if (!IsShieldEquipped(player)) return;
                if (!HasBulwarkPerk(player, 30)) return;

                var blocker = GetCurrentBlocker(__instance);
                if (!WasTimedBlock(__instance, blocker)) return;

                player.AddStamina(PERFECT_GUARD_STAMINA_RESTORE);
                _perfectGuardEmpowerUntil[player] = Time.time + PERFECT_GUARD_EMPOWER_WINDOW;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Humanoid_BlockAttack_Bulwark_Postfix: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetBlockPower", new Type[] { typeof(float) })]
        [HarmonyPostfix]
        public static void Bulwark_BlockPower_Postfix(ItemDrop.ItemData __instance, ref float __result)
        {
            try
            {
                if (__instance == null || __instance.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) return;

                var player = Player.m_localPlayer;
                if (player == null) return;

                if (HasBulwarkPerk(player, 10))
                {
                    __result = ApplyShieldWallBlockPower(__result);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_BlockPower_Postfix: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Player), "UseStamina")]
        [HarmonyPrefix]
        public static void Bulwark_UseStamina_Prefix(Player __instance, ref float v)
        {
            try
            {
                if (!IsShieldEquipped(__instance) || !__instance.IsBlocking()) return;

                if (HasBulwarkPerk(__instance, 10))
                {
                    v = ApplyShieldWallStamina(v);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Bulwark_UseStamina_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Shield Bearer (Level 20): shields impose no movement speed penalty. Offsets only the
        /// shield's own contribution to this player's equipment movement modifier, rather than
        /// mutating the shield's shared item data (which would leak the change to every player
        /// using that shield type).
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetEquipmentMovementModifier")]
        [HarmonyPostfix]
        public static void Player_GetEquipmentMovementModifier_Bulwark_Postfix(Player __instance, ref float __result)
        {
            try
            {
                if (!HasBulwarkPerk(__instance, 20)) return;

                var shield = GetCurrentBlocker(__instance);
                if (shield == null || shield.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) return;

                float shieldPenalty = shield.m_shared.m_movementModifier;
                if (shieldPenalty < 0f) __result -= shieldPenalty;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_GetEquipmentMovementModifier_Bulwark_Postfix: {ex.Message}");
            }
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
        }
        #endregion
    }
}
