using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace ValheimClassObelisk
{
    [HarmonyPatch]
    public static class WizardPerkManager
    {
        // Initialize Constants
        private static float ICY_HOT = 0.25f;
        private static float ESSENCE_LEECH = 0.05f;
        private static float AURA_DURATION = 30.0f;
        private static float FROST_ARMOR = 0.25f;
        private static float EITR_WEAVE_PASSIVE = 0.50f; // 50% eitr regen bonus for level 10

        // Initialize trackers
        private static float _frostDamage = 0f;
        private static float _fireDamage = 0f;

        // Icon Resources
        private const string FROST_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.frost_armor_128.rgba";
        private const string FIRE_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.immolation_aura_128.rgba";
        private const string EITR_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.eitrweave_fist.rgba";

        // Sprites
        private static Sprite _cachedFrostIcon;
        private static Sprite _cachedFireIcon;
        private static Sprite _cachedEitrIcon;

        #region Wizard Service Classes
        public static bool HasWizardPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Wizard)) return false;

            return playerData.GetClassLevel(PlayerClass.Wizard) >= requiredLevel;
        }

        public static HitData ApplyIcyHot(HitData hit)
        {
            float mult = 1f + ICY_HOT;
            // Multiplies all damage types by 'mod'
            if (hit == null) return hit;
            hit.m_damage.m_damage *= mult;
            hit.m_damage.m_fire *= mult;
            hit.m_damage.m_frost *= mult;
            return hit;
        }

        private static Sprite GetFrostIcon()
        {
            if (_cachedFrostIcon != null) return _cachedFrostIcon;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(FROST_ICON_RESOURCE))
                {
                    if (s == null)
                    {
                        Jotunn.Logger.LogWarning($"[Wizard] Embedded icon not found: {FROST_ICON_RESOURCE}");
                        return null;
                    }

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning("[Wizard] Frost icon header corrupt.");
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
                        Jotunn.Logger.LogWarning($"[Wizard] Frost icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    // Create Texture2D and upload raw data (no ImageConversion needed)
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    // Create UI sprite
                    _cachedFrostIcon = Sprite.Create(
                        tex,
                        new Rect(0, 0, width, height),
                        new Vector2(0.5f, 0.5f),
                        100f // pixels per unit; fine for inventory/status icons
                    );
                    return _cachedFrostIcon;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Wizard] Failed to load Frost icon: {ex}");
                return null;
            }
        }

        private static Sprite GetFireIcon()
        {
            if (_cachedFireIcon != null) return _cachedFireIcon;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(FIRE_ICON_RESOURCE))
                {
                    if (s == null)
                    {
                        Jotunn.Logger.LogWarning($"[Wizard] Embedded icon not found: {FIRE_ICON_RESOURCE}");
                        return null;
                    }

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning("[Wizard] Fire icon header corrupt.");
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
                        Jotunn.Logger.LogWarning($"[Wizard] Fire icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    // Create Texture2D and upload raw data (no ImageConversion needed)
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    // Create UI sprite
                    _cachedFireIcon = Sprite.Create(
                        tex,
                        new Rect(0, 0, width, height),
                        new Vector2(0.5f, 0.5f),
                        100f // pixels per unit; fine for inventory/status icons
                    );
                    return _cachedFireIcon;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Wizard] Failed to load Fire icon: {ex}");
                return null;
            }
        }

        private static Sprite GetEitrIcon()
        {
            if (_cachedEitrIcon != null) return _cachedEitrIcon;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(EITR_ICON_RESOURCE))
                {
                    if (s == null)
                    {
                        Jotunn.Logger.LogWarning($"[Wizard] Embedded icon not found: {EITR_ICON_RESOURCE}");
                        return null;
                    }

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning("[Wizard] Eitr icon header corrupt.");
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
                        Jotunn.Logger.LogWarning($"[Wizard] Eitr icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    // Create Texture2D and upload raw data (no ImageConversion needed)
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    // Create UI sprite
                    _cachedEitrIcon = Sprite.Create(
                        tex,
                        new Rect(0, 0, width, height),
                        new Vector2(0.5f, 0.5f),
                        100f // pixels per unit; fine for inventory/status icons
                    );
                    return _cachedEitrIcon;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Wizard] Failed to load Eitr icon: {ex}");
                return null;
            }
        }

        public static void ApplyFrostArmor(Player player)
        {
            var seman = player.GetSEMan();

            if (seman == null) return;

            // Remove existing effect to refresh
            seman.RemoveStatusEffect("SE_FrostArmor".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_FrostArmor";
            statusEffect.m_name = "Frost Armor";
            statusEffect.m_tooltip = "+25% Armor and Fire Immunity";
            statusEffect.m_icon = GetFrostIcon();
            statusEffect.m_ttl = AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
            Logger.LogInfo($"Buff Applied: {statusEffect.m_speedModifier}");
            _frostDamage = 0;
        }

        public static void ApplyImmolationAura(Player player)
        {
            var seman = player.GetSEMan();

            if (seman == null) return;

            // Remove existing effect to refresh
            seman.RemoveStatusEffect("SE_ImmolationAura".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_ImmolationAura>();
            statusEffect.name = "SE_ImmolationAura";
            statusEffect.m_name = "Immolation Aura";
            statusEffect.m_tooltip = "+25% Speed, Damage nearby enemies with fire";
            statusEffect.m_icon = GetFireIcon();
            statusEffect.m_ttl = AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
            Logger.LogInfo($"Immolation Aura Applied");
            _fireDamage = 0;
        }

        public static HitData ApplyDamageMod(HitData hit, float mod)
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

        private static void ApplyEssenceLeech(Player player, HitData hit)
        {
            // Check if player and hitData are valid
            if (player == null || hit == null)
            {
                return;
            }

            // Calculate 5% of the total damage dealt
            float totalDamage = hit.GetTotalDamage();
            float eitrReward = totalDamage * ESSENCE_LEECH;

            // Only award eitr if there's actual damage and reward > 0
            if (eitrReward > 0f)
            {
                // Add the eitr to the player
                player.AddEitr(eitrReward);
            }

        }

        // Apply Eitr Weave Passive buff for level 10+ wizards
        public static void ApplyEitrWeavePassive(Player player)
        {
            if (player == null) return;

            var seman = player.GetSEMan();
            if (seman == null) return;

            string passiveBuffName = "SE_EitrWeavePassive";
            bool hasPassiveBuff = seman.HaveStatusEffect(passiveBuffName.GetStableHashCode());

            // Check if player is level 10+ wizard
            if (HasWizardPerk(player, 10))
            {
                // Apply the passive buff if not already present
                if (!hasPassiveBuff)
                {
                    var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
                    statusEffect.name = passiveBuffName;
                    statusEffect.m_name = "Eitr Weave";
                    statusEffect.m_tooltip = "+50% Eitr Regeneration";
                    statusEffect.m_eitrRegenMultiplier = 1f + EITR_WEAVE_PASSIVE;
                    statusEffect.m_icon = GetEitrIcon();
                    statusEffect.m_ttl = 0f; // Permanent while conditions are met

                    seman.AddStatusEffect(statusEffect, resetTime: false);
                    Logger.LogInfo($"Applied Eitr Weave passive to {player.GetPlayerName()} (Level 10+ Wizard)");
                }
            }
            else
            {
                // Remove the buff if player no longer meets requirements
                if (hasPassiveBuff)
                {
                    seman.RemoveStatusEffect(passiveBuffName.GetStableHashCode(), quiet: true);
                    Logger.LogInfo($"Removed Eitr Weave passive from {player.GetPlayerName()} (No longer Level 10+ Wizard)");
                }
            }
        }
        #endregion

        #region Wizard Patch Classes
        // Summary
        // Apply Damage mod and OnHit effects in here.
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Wizard_Damage_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                Character attacker = hit.GetAttacker();
                Character target = __instance;
                float damageMult = 1f;

                bool isPlayer = attacker is Player;
                Player player = attacker as Player;

                if (isPlayer)
                {
                    if (HasWizardPerk(player, 20)) hit = ApplyIcyHot(hit);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply Post damage patches.
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Wizard_Damage_Postfix(Character __instance, ref HitData hit)
        {
            try
            {
                Character attacker = hit.GetAttacker();
                Character target = __instance;
                float damageMult = 1f;

                bool isPlayer = attacker is Player;
                Player player = attacker as Player;

                if (isPlayer && ClassCombatManager.IsMagicWeapon(player.GetCurrentWeapon()))
                {
                    if (HasWizardPerk(player, 30)) ApplyEssenceLeech(player, hit);
                    if (HasWizardPerk(player, 40)) _frostDamage += hit.m_damage.m_frost;
                    if (HasWizardPerk(player, 50)) _fireDamage += hit.m_damage.m_fire;
                }

                if (_frostDamage >= 300)
                {
                    ApplyFrostArmor(player);
                }

                if (_fireDamage >= 500)
                {
                    ApplyImmolationAura(player);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Assassin_Prefix: {ex.Message}");
            }
        }

        // Summary
        // Apply Frost Armor Bonus
        [HarmonyPatch(typeof(Player), "GetBodyArmor")]
        [HarmonyPostfix]
        public static void Wizard_FrostArmor_Postfix(Player __instance, ref float __result)
        {
            try
            {
                // Check If player has buff.
                SEMan seman = __instance.GetSEMan();
                string statusName = "SE_FrostArmor";
                if (seman.HaveStatusEffect(statusName.GetStableHashCode()))
                {
                    // player has the buff
                    // Multiply total armor by (1 + Percent).
                    // Example: base armor 60, Percent = 0.25 => 60 * 1.25 = 75
                    var before = __result;
                    __result *= (1f + FROST_ARMOR);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error when trying to apply frost armor bonus: {ex.Message}");
            }
        }

        // Summary
        // Apply wizard buffs when player levels up - patch the XP system's level up logic
        [HarmonyPatch(typeof(ClassXPManager), "AwardDamageXP")]
        [HarmonyPostfix]
        public static void Wizard_LevelUp_Postfix(Player player, ItemDrop.ItemData weapon, float damageDealt, Character target)
        {
            try
            {
                // Apply all wizard passive buffs after potential level up
                ApplyAllWizardPassiveBuffs(player);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Wizard_LevelUp_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Apply wizard buffs when player spawns
        [HarmonyPatch(typeof(Player), "OnSpawned")]
        [HarmonyPostfix]
        public static void Wizard_PlayerSpawn_Postfix(Player __instance)
        {
            try
            {
                // Apply all wizard passive buffs on spawn
                ApplyAllWizardPassiveBuffs(__instance);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Wizard_PlayerSpawn_Postfix: {ex.Message}");
            }
        }

        // Summary
        // Method to apply all wizard passive buffs based on current level
        private static void ApplyAllWizardPassiveBuffs(Player player)
        {
            if (player == null) return;

            // Apply Eitr Weave passive (level 10+)
            ApplyEitrWeavePassive(player);

            // Future passive buffs can be added here for other levels
            // For example:
            // if (HasWizardPerk(player, 20)) ApplyAnotherPassiveBuff(player);
        }
        #endregion

        public class SE_ImmolationAura : SE_Stats
        {
            private float lastDamageTime = 0f;
            private float damageInterval = 1f; // Damage every second
            private float AURA_DAMAGE = 15f;
            private float IMMOLATION_SPEED = 0.25f;

            public override void Setup(Character character)
            {
                base.Setup(character);

                // Set the movement speed modifier
                m_speedModifier = IMMOLATION_SPEED;
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);

                if (Time.time - lastDamageTime >= damageInterval)
                {
                    ApplyAuraDamage();
                    lastDamageTime = Time.time;
                }
            }

            private void ApplyAuraDamage()
            {
                if (!(m_character is Player player)) return;

                // Find enemies within 3m
                var enemies = new List<Character>();
                Character.GetCharactersInRange(player.transform.position, 3f, enemies);

                foreach (var enemy in enemies)
                {
                    if (enemy == player || enemy.IsDead()) continue;
                    if (enemy.IsPlayer()) continue; // Don't damage other players

                    // Create fire damage
                    var hitData = new HitData();
                    hitData.m_damage.m_fire = AURA_DAMAGE;
                    hitData.m_point = enemy.GetCenterPoint();
                    hitData.m_dir = (enemy.transform.position - player.transform.position).normalized;
                    hitData.SetAttacker(player);

                    enemy.Damage(hitData);
                }
            }
        }
    }
}