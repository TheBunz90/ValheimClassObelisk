// BrawlerPerkManager.cs
// NOTE: Be sure your PNG is added as an Embedded Resource at: Resources/Icons/rage_icon.png
//       And confirm the resource name below matches your project's root namespace + folders.

using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace ValheimClassObelisk
{
    [HarmonyPatch]
    public static class BrawlerPerkManager
    {
        // ==============================
        // Brawler configuration
        // ==============================
        private const float LIGHT_ON_FEET_STAMINA_REDUCTION = 0.10f;
        private const float ONE_TWO_COMBO_DAMAGE_BONUS = 0.50f;
        private const float ONE_TWO_COMBO_STAMINA_RESTORE = 0.05f;
        private const float ONE_TWO_COMBO_RESET_WINDOW = 4f;
        private const float IRON_FIST_SPIRIT_PERCENT = 0.10f;
        private const float RAGE_DURATION = 5f;
        private const float RAGE_DAMAGE_BONUS = 0.10f;
        private const float RAGE_ATTACK_SPEED = 1.20f; // +20% attack speed
        private const float RAGE_COOLDOWN = 20f;

        private const string RAGE_AS_KEY = "Brawler_RageFist_AS";

        // Embedded resource name for the Rage icon.
        private const string RAGE_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.rage_viking_128.rgba";
        private static Sprite _cachedRageIcon;

        // Per-player state (module-level statics here would leak between players in multiplayer)
        private class ComboData
        {
            public int hits = 0;
            public float lastHitTime = 0f;
        }
        private static readonly Dictionary<Player, ComboData> comboTracking = new Dictionary<Player, ComboData>();

        private class RageData
        {
            public float endTime = 0f;
            public float cooldownEndTime = 0f;
        }
        private static readonly Dictionary<Player, RageData> rageTracking = new Dictionary<Player, RageData>();

        #region Brawler Services
        public static bool HasBrawlerPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Brawler)) return false;

            return playerData.GetClassLevel(PlayerClass.Brawler) >= requiredLevel;
        }

        // Description metadata, shown in the class selection GUI - locked perks display as "???"
        private const string Intro = "Bare-knuckle fighters who turn momentum, toughness, and close-range pressure into damage.";
        private const string Outro = "Best for players who want fists to start modestly, become combo-driven, and finish with a clear mastery burst.";

        public static readonly List<PerkInfo> Perks = new List<PerkInfo>
        {
            new PerkInfo { RequiredLevel = 10, Name = "Bare-Knuckle Training", Description = "+8% unarmed damage." },
            new PerkInfo { RequiredLevel = 20, Name = "Light on Your Feet", Description = "Unarmed attacks consume 10% less stamina and jumping while unarmed consumes 10% less stamina." },
            new PerkInfo { RequiredLevel = 30, Name = "One-Two Combo", Description = "Every 3rd consecutive unarmed hit deals +50% damage and restores 5% stamina. Combo resets after 4s without an unarmed hit." },
            new PerkInfo { RequiredLevel = 40, Name = "Iron Fist", Description = "Unarmed attacks deal bonus spirit damage equal to 10% of weapon damage equivalent." },
            new PerkInfo { RequiredLevel = 50, Name = "Rage", Description = "After taking damage, enter Rage for 5s: +10% unarmed damage and +20% attack speed. 20s cooldown." },
        };

        public static string GetClassDescription(Player player)
        {
            int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Brawler) ?? 0;
            return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
        }

        private static Sprite GetRageIcon()
        {
            if (_cachedRageIcon != null) return _cachedRageIcon;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(RAGE_ICON_RESOURCE))
                {
                    if (s == null)
                    {
                        Jotunn.Logger.LogWarning($"[Brawler] Embedded icon not found: {RAGE_ICON_RESOURCE}");
                        return null;
                    }

                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning("[Brawler] Rage icon header corrupt.");
                        return null;
                    }

                    int width = BitConverter.ToInt32(header, 0);
                    int height = BitConverter.ToInt32(header, 4);
                    int expectedBytes = width * height * 4;

                    byte[] pixels = new byte[expectedBytes];
                    int off = 0;
                    while (off < expectedBytes)
                    {
                        int n = s.Read(pixels, off, expectedBytes - off);
                        if (n <= 0) break;
                        off += n;
                    }
                    if (off != expectedBytes)
                    {
                        Jotunn.Logger.LogWarning($"[Brawler] Rage icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    _cachedRageIcon = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
                    return _cachedRageIcon;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Brawler] Failed to load Rage icon: {ex}");
                return null;
            }
        }

        public static HitData ModPhysicalDamage(HitData hit, float mod)
        {
            if (hit == null) return hit;
            hit.m_damage.m_damage *= mod;
            hit.m_damage.m_slash *= mod;
            hit.m_damage.m_pierce *= mod;
            hit.m_damage.m_blunt *= mod;
            return hit;
        }
        #endregion

        #region Level 30 - One-Two Combo
        /// <summary>
        /// Returns the damage bonus (0 or ONE_TWO_COMBO_DAMAGE_BONUS) for this hit and advances
        /// the combo counter. Per-player, with a 4s reset window (the old version never reset,
        /// so a slow, interrupted trickle of hits could still "combo").
        /// </summary>
        private static float AdvanceOneTwoCombo(Player player)
        {
            if (!comboTracking.TryGetValue(player, out var combo) || Time.time - combo.lastHitTime > ONE_TWO_COMBO_RESET_WINDOW)
            {
                combo = new ComboData();
                comboTracking[player] = combo;
            }

            combo.lastHitTime = Time.time;
            combo.hits++;

            if (combo.hits >= 3)
            {
                combo.hits = 0;
                float maxStamina = player.GetMaxStamina();
                player.AddStamina(maxStamina * ONE_TWO_COMBO_STAMINA_RESTORE);
                return ONE_TWO_COMBO_DAMAGE_BONUS;
            }

            return 0f;
        }
        #endregion

        #region Level 50 - Rage
        private static bool IsRageActive(Player player)
        {
            return rageTracking.TryGetValue(player, out var data) && Time.time < data.endTime;
        }

        private static void TriggerRage(Player player)
        {
            if (!rageTracking.TryGetValue(player, out var data))
            {
                data = new RageData();
                rageTracking[player] = data;
            }

            data.endTime = Time.time + RAGE_DURATION;
            data.cooldownEndTime = Time.time + RAGE_COOLDOWN;

            AnimationSpeedManager.Set(player, RAGE_AS_KEY, RAGE_ATTACK_SPEED);
            ApplyRageBuffIcon(player);
        }

        private static void ApplyRageBuffIcon(Player player)
        {
            try
            {
                var seman = player.GetSEMan();
                if (seman == null) return;

                seman.RemoveStatusEffect("SE_Rage".GetStableHashCode(), quiet: true);

                var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
                statusEffect.name = "SE_Rage";
                statusEffect.m_name = "Rage";
                statusEffect.m_tooltip = "+10% unarmed damage, +20% attack speed.";
                statusEffect.m_ttl = RAGE_DURATION;
                statusEffect.m_icon = GetRageIcon();

                seman.AddStatusEffect(statusEffect, resetTime: true);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Brawler] Error adding Rage status effect: {ex}");
            }
        }

        public static void CleanupBuffs()
        {
            float currentTime = Time.time;
            var expired = rageTracking.Where(kvp => kvp.Value.endTime < currentTime && kvp.Value.endTime > 0f).Select(kvp => kvp.Key).ToList();
            foreach (var player in expired)
            {
                rageTracking[player].endTime = 0f; // mark handled without losing the cooldown timer
                if (player != null)
                {
                    AnimationSpeedManager.Clear(player, RAGE_AS_KEY);
                }
            }
        }
        #endregion

        #region Harmony Patches
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Brawler_Damage_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                // Offensive bonuses: the attacker's own Brawler level/unarmed status.
                if (hit.GetAttacker() is Player attacker && __instance != null && !(__instance is Player))
                {
                    var weapon = attacker.GetCurrentWeapon();
                    if (ClassCombatManager.IsUnarmedAttack(weapon))
                    {
                        var playerData = PlayerClassManager.GetPlayerData(attacker);
                        if (playerData != null && playerData.IsClassActive(PlayerClass.Brawler))
                        {
                            float bonus = 0f;

                            // One-Two Combo (Level 30)
                            if (HasBrawlerPerk(attacker, 30))
                            {
                                bonus += AdvanceOneTwoCombo(attacker);
                            }

                            // Rage (Level 50): live damage buff while active
                            if (HasBrawlerPerk(attacker, 50) && IsRageActive(attacker))
                            {
                                bonus += RAGE_DAMAGE_BONUS;
                            }

                            if (bonus > 0f)
                            {
                                ModPhysicalDamage(hit, 1f + bonus);
                            }

                            // Iron Fist (Level 40): bonus spirit damage from the current physical total
                            if (HasBrawlerPerk(attacker, 40))
                            {
                                float physicalDamage = hit.m_damage.GetTotalPhysicalDamage();
                                hit.m_damage.m_spirit += physicalDamage * IRON_FIST_SPIRIT_PERCENT;
                            }
                        }
                    }
                }

                // Defensive trigger: Rage activates whenever the player takes damage (Level 50),
                // regardless of whether they were blocking.
                if (__instance is Player targetPlayer && hit.GetTotalDamage() > 0)
                {
                    if (HasBrawlerPerk(targetPlayer, 50) && !IsRageActive(targetPlayer))
                    {
                        bool onCooldown = rageTracking.TryGetValue(targetPlayer, out var data) && Time.time < data.cooldownEndTime;
                        if (!onCooldown)
                        {
                            TriggerRage(targetPlayer);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Brawler_Damage_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Light on Your Feet (Level 20). Player.UseStamina is the single funnel for stamina
        /// costs in this codebase - both unarmed attacks and jumping (Player.OnJump computes
        /// jump stamina inline and passes it straight to UseStamina, there's no separate
        /// jump-stamina method to patch) go through here, so one check on "currently unarmed"
        /// covers both cases the perk description calls out.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UseStamina")]
        [HarmonyPrefix]
        public static void Player_UseStamina_Brawler_Prefix(Player __instance, ref float v)
        {
            try
            {
                if (!HasBrawlerPerk(__instance, 20)) return;
                if (!ClassCombatManager.IsUnarmedAttack(__instance.GetCurrentWeapon())) return;

                v *= (1f - LIGHT_ON_FEET_STAMINA_REDUCTION);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_UseStamina_Brawler_Prefix: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPostfix]
        public static void Game_Update_Brawler_Postfix()
        {
            try
            {
                if (Time.time % 1f < Time.deltaTime)
                {
                    CleanupBuffs();
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Game_Update_Brawler_Postfix: {ex.Message}");
            }
        }
        #endregion
    }
}
