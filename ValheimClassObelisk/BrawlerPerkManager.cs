using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;

namespace ValheimClassObelisk
{
    [HarmonyPatch]
    public static class BrawlerPerkManager
    {
        // Set Brawler Modifier values
        private static float ONE_TWO_COMBO_DAMAGE = 1.0f;
        private static float ONE_TWO_COMBO_STAMINA = 0.05f;
        private static float BREAK_GUARD_DAMAGE = 0.5f;
        private static float IRON_FIST_DAMAGE = 0.1f;
        private static float IRON_FIST_SPEED = 0.20f;
        private static float IRON_SKIN_ARMOR = 0.25f;
        private static float RAGE_DURATION = 5.0f;
        private static float RAGE_DAMAGE_REDUCTION = 0.5f;
        private static float RAGE_ATTACK_SPEED = 0.5f;
        private static float RAGE_DAMAGE_BUFF = 0.25f;
        private static float RAGE_COOLDOWN_TIME = 15.0f;

        // Set Brawler Attributes
        private static int conPunches = 0;
        private static Time rageEndTime;
        private static bool rageIsActive = false;


        #region Brawler Service
        public static bool HasBrawlerPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Brawler)) return false;

            return playerData.GetClassLevel(PlayerClass.Brawler) >= requiredLevel;
        }

        public static float IronFistDamage(Player player)
        {
            float damage = 0f;
            if (player == null) return damage;

            float maxHealth = player.GetMaxHealth();
            damage = maxHealth * IRON_FIST_DAMAGE;

            return damage;
        }

        public static HitData ModDamage(HitData hit, float mod)
        {
            if (hit == null || mod == 0f) return hit;
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

        #endregion

        #region Harmony Patches
        // Summary
        // Apply Damage mod and OnHit effects in here.
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Brawler_Damage_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                Character attacker = hit.GetAttacker();
                Character target = __instance;
                float damageMult = 1f;
                float reduceMult = 1f;

                float additionalDamage = 0f;

                if (hit.GetAttacker() is Player player)
                {
                    // Apply Damage Mods.
                    if (HasBrawlerPerk(player, 10) && conPunches == 2)
                    {
                        if (conPunches == 2)
                        {
                            damageMult += ONE_TWO_COMBO_DAMAGE;
                        }
                        else
                        {
                            conPunches++;
                        }
                    }
                    if (HasBrawlerPerk(player, 30)) additionalDamage += IronFistDamage(player);
                    if (HasBrawlerPerk(player, 50)) damageMult += rageIsActive ? RAGE_DAMAGE_BUFF : 0f;
                }
                
                if (target == Player.m_localPlayer)
                {
                    // Apply Damage Reductions.
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
            }
        }

        #endregion

        #region Terminal Commands

        #endregion


    }



}
