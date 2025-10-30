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
    public static class LancerPerkManager
    {
        // Initialize Constants
        private static float REACH_ADVANTAGE_DAMAGE = 0.15f; // 15% pierce damage bonus
        private static float REACH_ADVANTAGE_STAMINA = 0.15f; // 15% stamina reduction
        private static float SPEAR_STORM_DAMAGE_PER_STACK = 0.05f; // 5% damage per stack
        private static float SPEAR_STORM_DURATION = 5.0f; // 5 seconds
        private static int SPEAR_STORM_MAX_STACKS = 5; // Maximum 5 stacks
        private static float DISRUPTIVE_STRIKES_CHANCE = 0.5f; // 50% chance
        private static float DISRUPTIVE_STRIKES_DURATION = 3.0f; // 3 seconds disable
        private static float IMPRESSIVE_THROW_MAX_DISTANCE = 100f; // 100 meters
        private static float IMPRESSIVE_THROW_MAX_MULTIPLIER = 3.0f; // 300% damage

        // Initialize trackers
        private static Dictionary<Player, float> _lastThrownSpearTime = new Dictionary<Player, float>();
        private static Dictionary<Player, Vector3> _lastThrownSpearPosition = new Dictionary<Player, Vector3>();

        // Icon Resources (you'll need to add these to your project)
        //private const string REACH_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.reach_advantage_128.rgba";
        private const string STORM_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.SpearStorm.rgba";
        //private const string DISRUPT_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.disruptive_strikes_128.rgba";
        //private const string TELEPORT_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.spear_teleport_128.rgba";

        // Sprites
        //private static Sprite _cachedReachIcon;
        private static Sprite _cachedStormIcon;
        //private static Sprite _cachedDisruptIcon;
        //private static Sprite _cachedTeleportIcon;

        #region Lancer Service Classes
        public static bool HasLancerPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Lancer)) return false;

            return playerData.GetClassLevel(PlayerClass.Lancer) >= requiredLevel;
        }

        public static HitData ApplyReachAdvantage(HitData hit)
        {
            if (hit == null) return hit;

            // Apply 15% pierce damage bonus
            float mult = 1f + REACH_ADVANTAGE_DAMAGE;
            hit.m_damage.m_pierce *= mult;

            return hit;
        }

        public static float ApplyReachAdvantageStamina(float staminaCost)
        {
            // Reduce stamina cost by 15%
            return staminaCost * (1f - REACH_ADVANTAGE_STAMINA);
        }

        public static void ApplySpearStorm(Player player, Character target)
        {
            if (player == null) return;

            SEMan seman = player.GetSEMan();
            string statusName = "SE_SpearStorm";

            // Check if player already has the buff
            var existingEffect = seman.GetStatusEffect(statusName.GetStableHashCode());
            if (existingEffect != null)
            {
                // Refresh duration and add stack
                var spearStormEffect = existingEffect as SE_SpearStorm;
                if (spearStormEffect != null)
                {
                    spearStormEffect.AddStack();
                    spearStormEffect.RefreshDuration();
                }
            }
            else
            {
                // Create new spear storm effect
                var statusEffect = ScriptableObject.CreateInstance<SE_SpearStorm>();
                statusEffect.name = statusName;
                statusEffect.m_name = "Spear Storm";
                statusEffect.m_tooltip = "Damage increased by successful hits";
                statusEffect.m_ttl = SPEAR_STORM_DURATION;
                statusEffect.m_icon = GetStormIcon();

                seman.AddStatusEffect(statusEffect, resetTime: false);
            }
        }

        public static void ApplyDisruptiveStrikes(Character target)
        {
            if (target == null || target.IsDead()) return;

            // Apply movement disable effect
            SEMan seman = target.GetSEMan();
            string statusName = "SE_MovementDisabled";

            var statusEffect = ScriptableObject.CreateInstance<SE_MovementDisabled>();
            statusEffect.name = statusName;
            statusEffect.m_name = "Movement Disabled";
            statusEffect.m_tooltip = "Cannot move due to disruptive strike";
            statusEffect.m_ttl = DISRUPTIVE_STRIKES_DURATION;
            //statusEffect.m_icon = GetDisruptIcon();

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static float CalculateImpressiveThrowDamage(Vector3 throwerPosition, Vector3 targetPosition)
        {
            float distance = Vector3.Distance(throwerPosition, targetPosition);
            float distanceRatio = Mathf.Clamp01(distance / IMPRESSIVE_THROW_MAX_DISTANCE);

            // Linear scaling from 1x to 3x damage based on distance
            float result = 1f + (distanceRatio * (IMPRESSIVE_THROW_MAX_MULTIPLIER - 1f));

            return result;
        }

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

        public static void HandleSpearOfRelocation(Player player, Character target)
        {
            if (player == null || target == null) return;

            // Teleport player to target location
            Vector3 targetPosition = target.transform.position;
            Vector3 teleportPosition = targetPosition + Vector3.back * 2f; // Teleport slightly behind target

            player.transform.position = teleportPosition;
            player.GetComponent<Rigidbody>().velocity = Vector3.zero;
        }
        #endregion

        #region Icon Loading Methods

        private static Sprite GetStormIcon()
        {
            if (_cachedStormIcon != null) return _cachedStormIcon;
            return LoadIconFromResource(STORM_ICON_RESOURCE, "Storm", ref _cachedStormIcon);
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
                        Jotunn.Logger.LogWarning($"[Lancer] Embedded icon not found: {resourceName}");
                        return null;
                    }

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning($"[Lancer] {iconType} icon header corrupt.");
                        return null;
                    }

                    // little-endian UInt32 width/height
                    int width = BitConverter.ToInt32(header, 0);
                    int height = BitConverter.ToInt32(header, 4);
                    int expectedBytes = width * height * 4;

                    // Read raw RGBA32 pixels
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
                        Jotunn.Logger.LogWarning($"[Lancer] {iconType} icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    // Create Texture2D and upload raw data
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    // Create UI sprite
                    cachedSprite = Sprite.Create(
                        tex,
                        new Rect(0, 0, width, height),
                        new Vector2(0.5f, 0.5f),
                        100f
                    );
                    return cachedSprite;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Lancer] Failed to load {iconType} icon: {ex}");
                return null;
            }
        }
        #endregion

        #region Lancer Patch Classes
        // Summary
        // Apply damage modifications and on-hit effects for Lancer perks
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Lancer_Damage_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                Character attacker = hit.GetAttacker();
                Character target = __instance;

                bool isPlayer = attacker is Player;
                Player player = attacker as Player;

                if (isPlayer && ClassCombatManager.IsSpearWeapon(player.GetCurrentWeapon()))
                {
                    // Level 10 - Reach Advantage: +15% pierce damage
                    if (HasLancerPerk(player, 10))
                    {
                        hit = ApplyReachAdvantage(hit);
                    }

                    // Level 20 - Spear Storm: +5% Lightning Damage per stack
                    if (HasLancerPerk(player, 20))
                    {
                        var seman = player.GetSEMan();
                        var statusName = "SE_SpearStorm".GetStableHashCode();
                        if (seman != null && seman.HaveStatusEffect(statusName))
                        {
                            SE_SpearStorm spearStorm = (SE_SpearStorm) seman.GetStatusEffect(statusName);
                            var stormStacks = spearStorm ? spearStorm.currentStacks : 0f;
                            float lightningMod = stormStacks * SPEAR_STORM_DAMAGE_PER_STACK;
                            hit.m_damage.m_lightning = hit.m_damage.GetTotalDamage() * lightningMod;
                        }
                    }

                    // Level 40 - Impressive Throw: Distance-based damage (for thrown weapons)
                    if (HasLancerPerk(player, 40) && hit.m_skill == Skills.SkillType.Spears)
                    {
                        // Check if this is a thrown attack (you may need to adjust this condition)
                        float distanceMultiplier = CalculateImpressiveThrowDamage(player.transform.position, target.transform.position);
                        hit.m_damage.Modify(distanceMultiplier);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Lancer_Damage_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply post-damage effects for Lancer perks
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPostfix]
        public static void Lancer_Damage_Postfix(Character __instance, ref HitData hit)
        {
            try
            {
                Character attacker = hit.GetAttacker();
                Character target = __instance;

                bool isPlayer = attacker is Player;
                Player player = attacker as Player;

                if (isPlayer && ClassCombatManager.IsSpearWeapon(player.GetCurrentWeapon()) && hit.GetTotalDamage() > 0)
                {
                    // Level 20 - Spear Storm: Stacking damage buff on successful hits
                    if (HasLancerPerk(player, 20))
                    {
                        ApplySpearStorm(player, target);
                    }

                    // Level 30 - Disruptive Strikes: 50% chance to disable movement
                    if (HasLancerPerk(player, 30))
                    {
                        if (UnityEngine.Random.Range(0f, 1f) <= DISRUPTIVE_STRIKES_CHANCE)
                        {
                            ApplyDisruptiveStrikes(target);
                        }
                    }

                    // Level 50 - Spear of Relocation: Teleport on thrown spear hit
                    //if (HasLancerPerk(player, 50) && hit.m_skill == Skills.SkillType.Spears)
                    //{
                    //    // Check if this was a thrown attack (you may need to adjust this condition)
                    //    HandleSpearOfRelocation(player, target);
                    //}
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Lancer_Damage_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Apply Relocation Effect if Spear Hits an enemy
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
                var thrower = FindProjectileOwner(__instance);
                if (thrower == null) return;

                // Only trigger for players with Archer class active
                var playerData = PlayerClassManager.GetPlayerData(thrower);
                if (playerData == null || !playerData.IsClassActive(PlayerClass.Lancer) || !HasLancerPerk(thrower, 50)) return;

                // TODO: Check Projectile type is spear.
                var isSpear = __instance.m_skill == Skills.SkillType.Spears;

                // TODO: handleRelocation
                if (isSpear)
                {
                    HandleSpearOfRelocation(thrower, hitCharacter);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Projectile_OnHit_Prefix (Lancer): {ex.Message}");
            }
        }

        // Summary
        // Apply stamina cost reduction for Reach Advantage
        [HarmonyPatch(typeof(Player), "UseStamina")]
        [HarmonyPrefix]
        public static void Lancer_UseStamina_Prefix(Player __instance, ref float v)
        {
            try
            {
                if (HasLancerPerk(__instance, 10) && ClassCombatManager.IsSpearWeapon(__instance.GetCurrentWeapon()))
                {
                    // Apply stamina reduction for spear attacks
                    v = ApplyReachAdvantageStamina(v);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Lancer_UseStamina_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply lancer buffs when player levels up
        [HarmonyPatch(typeof(ClassXPManager), "AwardDamageXP")]
        [HarmonyPostfix]
        public static void Lancer_LevelUp_Postfix(Player player, ItemDrop.ItemData weapon, float damageDealt, Character target)
        {
            try
            {
                // Apply all lancer passive buffs after potential level up
                ApplyAllLancerPassiveBuffs(player);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Lancer_LevelUp_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Apply lancer buffs when player spawns
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        public static void Lancer_PlayerSpawn_Postfix(Player __instance)
        {
            try
            {
                // Apply all lancer passive buffs on spawn
                ApplyAllLancerPassiveBuffs(__instance);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Lancer_PlayerSpawn_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Method to apply all lancer passive buffs based on current level
        private static void ApplyAllLancerPassiveBuffs(Player player)
        {
            if (player == null) return;

            // Currently no persistent passive buffs for Lancer, but this is where you'd add them
            // For example, if you had a passive speed boost or armor bonus

            // Future passive buffs can be added here for other levels
            // if (HasLancerPerk(player, 10)) ApplyReachAdvantagePassive(player);
        }
        #endregion

        #region Status Effects
        public class SE_SpearStorm : SE_Stats
        {
            public int currentStacks = 1;

            public override void Setup(Character character)
            {
                base.Setup(character);
                m_ttl = SPEAR_STORM_DURATION;
            }

            public void AddStack()
            {
                if (currentStacks < SPEAR_STORM_MAX_STACKS)
                {
                    currentStacks++;
                    UpdateTooltip();
                }
            }

            public void RefreshDuration()
            {
                m_ttl = SPEAR_STORM_DURATION;
                m_time = 0f;
            }

            public float GetCurrentDamageBonus()
            {
                return currentStacks * SPEAR_STORM_DAMAGE_PER_STACK;
            }

            private void UpdateTooltip()
            {
                m_tooltip = $"Damage increased by {(GetCurrentDamageBonus() * 100):F0}% ({currentStacks}/{SPEAR_STORM_MAX_STACKS} stacks)";
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                UpdateTooltip();
            }
        }

        public class SE_MovementDisabled : SE_Stats
        {
            public override void Setup(Character character)
            {
                base.Setup(character);

                // Disable movement by setting speed modifier to 0
                m_speedModifier = -1f; // This should make speed 0
                m_ttl = DISRUPTIVE_STRIKES_DURATION;
            }

            //public override void UpdateStatusEffect(float dt)
            //{
            //    base.UpdateStatusEffect(dt);

            //    // Ensure character cannot move
            //    if (m_character != null)
            //    {
            //        var rigidbody = m_character.GetComponent<Rigidbody>();
            //        if (rigidbody != null)
            //        {
            //            // Stop any movement
            //            rigidbody.velocity = new Vector3(0, rigidbody.velocity.y, 0);
            //        }
            //    }
            //}
        }
        #endregion
    }
}