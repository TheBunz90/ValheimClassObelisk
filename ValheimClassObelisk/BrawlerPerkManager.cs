using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using System;

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
        private static float IRON_FIST_SPEED = 2.50f;
        private static float IRON_SKIN_ARMOR = 0.25f;
        private static float RAGE_DURATION = 5.0f;
        private static float RAGE_DAMAGE_REDUCTION = 0.5f;
        private static float RAGE_ATTACK_SPEED = 1.5f;
        private static float RAGE_DAMAGE_BUFF = 0.25f;
        private static float RAGE_COOLDOWN_TIME = 15.0f;

        // Set Brawler Attributes
        private static int conPunches = 0;
        private static float rageEndTime;
        private static bool rageIsActive = false;
        private static bool ironFistIsActive = false;

        // Set constant strings
        private static string RAGE_AS_KEY = "Brawler_RageFist_AS";
        private static string IRON_FIST_AS_KEY = "Brawler_IronFist_AS";

        [ThreadStatic] private static bool _inEquipHooks;


        #region Brawler Services
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

        public static HitData ModPhysicalDamage(HitData hit, float mod)
        {
            if (hit == null || mod == 0f) return hit;
            hit.m_damage.m_damage *= mod;
            hit.m_damage.m_slash *= mod;
            hit.m_damage.m_pierce *= mod;
            hit.m_damage.m_blunt *= mod;
            return hit;
        }

        public static void ApplyRageAttackSpeed(Player player)
        {
            rageIsActive = true;
            rageEndTime = Time.time + RAGE_DURATION;
            AnimationSpeedManager.Set(player, RAGE_AS_KEY, RAGE_ATTACK_SPEED);
        }

        private static void ApplyRageBuffIcon(Player player)
        {
            try
            {
                var seman = player.GetSEMan();

                if (seman == null) return;

                // Remove existing effect to refresh
                seman.RemoveStatusEffect("SE_Rage".GetStableHashCode(), quiet: true);

                var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
                statusEffect.name = "SE_Rage";
                statusEffect.m_name = "Rage";
                statusEffect.m_tooltip = "+50% Attack Speed, +50% Physical Resist, +25% Damage.";
                //statusEffect.m_icon = ;
                statusEffect.m_ttl = RAGE_DURATION;

                seman.AddStatusEffect(statusEffect, resetTime: true);
                //Logger.LogInfo($"Buff Applied: {statusEffect.m_speedModifier}");
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error adding rage status effect: {ex.Message}");
            }
        }

        public static bool ShouldApplyIronFist(Player player)
        {
            if (player == null) return false;
            //Logger.LogInfo("[ShouldApplyIronFist] Player Not Null");
            // Only when the *current* attack is unarmed/fists by your own definition
            var current = player.GetCurrentWeapon();
            bool hasPerk = HasBrawlerPerk(player, 30);
            bool isFistWeapon = ClassCombatManager.IsUnarmedAttack(current);
            //Logger.LogInfo($"[ShouldApplyIronFist] hasPerk: {hasPerk} / isFistWeapon: {isFistWeapon}");
            //Logger.LogInfo($"[ShouldApplyIronFist] current: {current.m_shared.m_name}");
            return hasPerk && isFistWeapon;
        }

        public static void CleanupBuffs()
        {
            float currentTime = Time.time;
            Player player = Player.m_localPlayer;
            if (rageIsActive && rageEndTime < currentTime)
            {
                rageIsActive = false;
                AnimationSpeedManager.Clear(player, RAGE_AS_KEY);
            }
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
                bool isPlayer = attacker is Player;
                Player player = attacker as Player;
                if (isPlayer)
                {
                    // Apply OneTwoCombo Effects.
                    if (HasBrawlerPerk(player, 10))
                    {
                        if (conPunches == 2)
                        {
                            conPunches = 0;
                            damageMult += ONE_TWO_COMBO_DAMAGE;
                            float maxStamina = player.GetMaxStamina();
                            float extraStamina = maxStamina * ONE_TWO_COMBO_STAMINA;
                            player.AddStamina(extraStamina);
                        }
                        else
                        {
                            conPunches++;
                        }
                    }
                    // Apply Iron Fist Effects.
                    if (HasBrawlerPerk(player, 30)) additionalDamage += IronFistDamage(player);
                    // Apply Rage Effects.
                    if (HasBrawlerPerk(player, 50))
                    {
                        damageMult += rageIsActive ? RAGE_DAMAGE_BUFF : 0f;
                        ApplyRageAttackSpeed(player);
                    }
                }
                
                if (target == Player.m_localPlayer)
                {
                    // Iron Skin
                    if (HasBrawlerPerk(player, 40)) reduceMult -= IRON_SKIN_ARMOR;
                    // Rage
                    if (HasBrawlerPerk(player, 50)) reduceMult -= RAGE_DAMAGE_REDUCTION;
                    ModPhysicalDamage(hit, reduceMult);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
            }
        }

        [HarmonyPatch(typeof(Humanoid), "EquipItem")]
        [HarmonyPostfix]
        public static void Humanoid_EquipItem_Postfix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects = true)
        {
            try
            {
                if (_inEquipHooks) return;
                _inEquipHooks = true;

                // Check is player has colossus perk.
                var player = Player.m_localPlayer;
                if(player == null || __instance != player) return;
                //Logger.LogInfo("[Equip Patch] EquipItem");
                bool wantIronFist = ShouldApplyIronFist(player);
                //Logger.LogInfo($"[Equip Patch] wantIronFist: {wantIronFist} / ironFistIsActive: {ironFistIsActive}");
                if (!wantIronFist && ironFistIsActive)
                {
                    ironFistIsActive = false;
                    AnimationSpeedManager.Clear(player, IRON_FIST_AS_KEY);
                    Logger.LogInfo("[Brawler] Iron Fist AS: DISABLED.");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_EquipItem_Brawler_Patch_Prefix: {ex.Message}");
            }
            finally
            {
                _inEquipHooks = false;
            }
        }

        [HarmonyPatch(typeof(Humanoid), "UnequipItem")]
        [HarmonyPostfix]
        public static void Humanoid_UnequipItem_Postfix(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects = true)
        {
            try
            {
                if (_inEquipHooks) return;
                _inEquipHooks = true;

                var player = Player.m_localPlayer;
                if (player == null || __instance != player) return;
                //Logger.LogInfo("[UnEquip Patch] UnEquipItem");

                // Minimal work; let EquipItem reconcile after the game picks a new "current" weapon.
                // (Optionally force a single reconciliation here using the same ShouldApplyIronFistAS)
                bool wantIronFist = ShouldApplyIronFist(player);
                //Logger.LogInfo($"[UnEquip Patch] wantIronFist: {wantIronFist} / ironFistIsActive: {ironFistIsActive}");
                if (wantIronFist && !ironFistIsActive)
                {
                    ironFistIsActive = true;
                    AnimationSpeedManager.Set(player, IRON_FIST_AS_KEY, IRON_FIST_SPEED);
                    Logger.LogInfo("[Brawler] Iron Fist AS: ENABLED.");
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"[Brawler] UnequipItem postfix error: {ex}");
            }
            finally
            {
                _inEquipHooks = false;
            }
        }

        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPostfix]
        public static void Game_Update_Assassin_Postfix()
        {
            try
            {
                // Clean up buffs every second
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

        #region Terminal Commands
        // TODO: Add testing commands in needed.
        #endregion
    }



}
