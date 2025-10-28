// BrawlerPerkManager.cs (updated)
// NOTE: Be sure your PNG is added as an Embedded Resource at: Resources/Icons/rage_icon.png
//       And confirm the resource name below matches your project’s root namespace + folders.

using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using System.IO;
using System.Reflection;

namespace ValheimClassObelisk
{
    [HarmonyPatch]
    public static class BrawlerPerkManager
    {
        // ==============================
        // Brawler Modifier values
        // ==============================
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
        private static float RAGE_COOLDOWN_TIME = 30.0f;

        // ==============================
        // Brawler Attributes
        // ==============================
        private static int conPunches = 0;
        private static float rageEndTime;
        private static float rageInternalCD;
        private static bool rageIsActive = false;
        private static bool ironFistIsActive = false;

        // ==============================
        // Constant strings / keys
        // ==============================
        private static string RAGE_AS_KEY = "Brawler_RageFist_AS";
        private static string IRON_FIST_AS_KEY = "Brawler_IronFist_AS";

        // Embedded resource name for the Rage icon.
        // IMPORTANT: Replace "ValheimClassObelisk" below if your project root namespace differs.
        // Example folder structure: Resources/Icons/rage_icon.png
        // => "ValheimClassObelisk.Resources.Icons.rage_icon.png"
        private const string RAGE_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.rage_viking_128.rgba";

        [ThreadStatic] private static bool _inEquipHooks;

        // Cache the loaded Rage icon so we don't recreate it repeatedly
        private static Sprite _cachedRageIcon;

        #region Brawler Services
        public static bool HasBrawlerPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Brawler)) return false;

            return playerData.GetClassLevel(PlayerClass.Brawler) >= requiredLevel;
        }

        // ------------------------------------------------------------
        // Loads and returns the Rage icon Sprite from the embedded PNG
        // ------------------------------------------------------------
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

                    // Read header (width, height)
                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning("[Brawler] Rage icon header corrupt.");
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
                        Jotunn.Logger.LogWarning($"[Brawler] Rage icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    // Create Texture2D and upload raw data (no ImageConversion needed)
                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    // Create UI sprite
                    _cachedRageIcon = Sprite.Create(
                        tex,
                        new Rect(0, 0, width, height),
                        new Vector2(0.5f, 0.5f),
                        100f // pixels per unit; fine for inventory/status icons
                    );
                    return _cachedRageIcon;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Brawler] Failed to load Rage icon: {ex}");
                return null;
            }
        }

        public static float IronFistDamage(Player player)
        {
            // Calculate bonus damage from the player's max HP
            float damage = 0f;
            if (player == null) return damage;

            float maxHealth = player.GetMaxHealth();
            damage = maxHealth * IRON_FIST_DAMAGE;

            return damage;
        }

        public static HitData ModDamage(HitData hit, float mod)
        {
            // Multiplies all damage types by 'mod'
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
            // Multiplies physical damage types by 'mod' only
            if (hit == null || mod == 0f) return hit;
            hit.m_damage.m_damage *= mod;
            hit.m_damage.m_slash *= mod;
            hit.m_damage.m_pierce *= mod;
            hit.m_damage.m_blunt *= mod;
            return hit;
        }

        public static void ApplyRageAttackSpeed(Player player)
        {
            // Activate Rage state and set the AnimationSpeed modifier
            rageIsActive = true;
            rageEndTime = Time.time + RAGE_DURATION;
            rageInternalCD = Time.time + RAGE_COOLDOWN_TIME;
            AnimationSpeedManager.Set(player, RAGE_AS_KEY, RAGE_ATTACK_SPEED);

            // Also apply a visible status effect with the embedded icon
            ApplyRageBuffIcon(player);
        }

        // ------------------------------------------------------------------
        // Creates and applies a temporary SE_Stats "Rage" with our custom icon
        // ------------------------------------------------------------------
        private static void ApplyRageBuffIcon(Player player)
        {
            try
            {
                var seman = player.GetSEMan();
                if (seman == null) return;

                seman.RemoveStatusEffect("SE_Rage".GetStableHashCode(), true);

                var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
                statusEffect.name = "SE_Rage";
                statusEffect.m_name = "Rage";
                statusEffect.m_tooltip = "+50% Attack Speed, +50% Physical Resist, +25% Damage.";
                statusEffect.m_ttl = RAGE_DURATION;

                // Use the embedded .rgba sprite
                statusEffect.m_icon = GetRageIcon();

                seman.AddStatusEffect(statusEffect, true);
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Brawler] Error adding Rage status effect: {ex}");
            }
        }

        public static bool ShouldApplyIronFist(Player player)
        {
            if (player == null) return false;
            var current = player.GetCurrentWeapon();
            bool hasPerk = HasBrawlerPerk(player, 30);
            bool isFistWeapon = ClassCombatManager.IsUnarmedAttack(current);
            return hasPerk && isFistWeapon;
        }

        public static void CleanupBuffs()
        {
            // Reconcile expiring state; clear the Rage attack speed when TTL passes
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
                    // One-Two-Combo perk: every 3rd punch increases damage and gives stamina
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

                    // Iron Fist perk: add bonus damage based on max HP
                    if (HasBrawlerPerk(player, 30)) additionalDamage += IronFistDamage(player);

                    // Rage perk: live damage buff + start Rage state
                    if (HasBrawlerPerk(player, 50))
                    {
                        damageMult += rageIsActive ? RAGE_DAMAGE_BUFF : 0f;
                    }
                }

                if (target == Player.m_localPlayer)
                {
                    // Iron Skin
                    if (HasBrawlerPerk(player, 40)) reduceMult -= IRON_SKIN_ARMOR;

                    // Rage: reduce incoming physical damage while active
                    if (HasBrawlerPerk(player, 50) && rageIsActive)
                    {
                        var currentTime = Time.time;
                        if (rageIsActive) reduceMult -= RAGE_DAMAGE_REDUCTION;
                        else if (currentTime > rageInternalCD) ApplyRageAttackSpeed(player);
                    }

                    ModPhysicalDamage(hit, reduceMult);
                }

                // Apply additive bonus (e.g., Iron Fist) AFTER multipliers
                if (additionalDamage > 0f)
                {
                    hit.m_damage.m_blunt += additionalDamage; // fists are blunt in Valheim
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

                var player = Player.m_localPlayer;
                if (player == null || __instance != player) return;

                bool wantIronFist = ShouldApplyIronFist(player);
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

                bool wantIronFist = ShouldApplyIronFist(player);
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
                // Periodically clean up expiring buffs (once a second)
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
        // TODO: Add testing commands if needed.
        #endregion
    }
}
