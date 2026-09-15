using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System.Collections.Generic;

namespace ValheimClassObelisk
{
    /// <summary>
    /// Lancer class perk system - focused on spears and polearms, split between 1H thrown/thrust
    /// spears and 2H sweeping polearms (ClassCombatManager.IsPolearmWeapon / IsTwoHandedWeapon).
    /// </summary>
    [HarmonyPatch]
    public static class LancerPerkManager
    {
        public static bool HasLancerPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Lancer)) return false;

            return playerData.GetClassLevel(PlayerClass.Lancer) >= requiredLevel;
        }

        // Description metadata, shown in the class selection GUI - locked perks display as "???"
        private const string Intro = "Spear and polearm specialists who win through reach, spacing, throws, and sweeping control.";
        private const string Outro = "Best for players who want spears and polearms to share a class identity while preserving their different combat rhythms.";

        public static readonly List<PerkInfo> Perks = new List<PerkInfo>
        {
            new PerkInfo { RequiredLevel = 10, Name = "Reach Advantage", Description = "+8% pierce damage with spears and polearms." },
            new PerkInfo { RequiredLevel = 20, Name = "Balanced Grip", Description = "Thrown spears automatically return to the player after reaching their target, and polearms impose no movement penalties." },
            new PerkInfo { RequiredLevel = 30, Name = "Spear Storm", Description = "Spear hits build up to 5 stacks; each stack grants +3% attack speed for 5s. Polearm hits build up to 5 stacks; each stack grants +3% stagger damage for 5s." },
            new PerkInfo { RequiredLevel = 40, Name = "Stormpoint", Description = "Spear and polearm attacks deal bonus lightning damage equal to 12% of weapon damage." },
            new PerkInfo { RequiredLevel = 50, Name = "Impaling Momentum", Description = "+15% spear and polearm damage. Thrown spear hits beyond 15m and polearm special attacks gain an additional +10% damage." },
        };

        public static string GetClassDescription(Player player)
        {
            int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Lancer) ?? 0;
            return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
        }

        #region Level 20 - Balanced Grip (Spear Recall)
        public const float SPEAR_RECALL_TIMEOUT = 10f;

        private class PendingRecall
        {
            public Player owner;
            public ItemDrop.ItemData item;
            public float deadline;
        }

        // Keyed by the actual Projectile instance so multiple spears in flight at once (e.g. two
        // Lancers, or one player throwing again before the first lands) are tracked independently.
        // A destroyed Projectile remains a valid dictionary key (Unity's "fake null" only affects
        // member access, not object identity/hashing), so lookups from SpawnOnHit still resolve.
        private static readonly Dictionary<Projectile, PendingRecall> pendingRecalls = new Dictionary<Projectile, PendingRecall>();

        /// <summary>
        /// Called from Projectile.Setup's postfix at the moment a spear is thrown - starts the
        /// 10s fallback clock immediately, independent of whatever happens to the projectile
        /// object afterward (hit, bounce, lost off a cliff, vanilla's own shorter TTL, etc.).
        /// </summary>
        public static void TrackThrownSpear(Projectile projectile, Player owner, ItemDrop.ItemData item)
        {
            pendingRecalls[projectile] = new PendingRecall { owner = owner, item = item, deadline = Time.time + SPEAR_RECALL_TIMEOUT };
        }

        /// <summary>
        /// Called from Projectile.SpawnOnHit's prefix when a tracked spear lands on anything.
        /// Returns true (and removes the tracking entry) if this projectile was being tracked.
        /// </summary>
        public static bool TryConsumeRecall(Projectile projectile, out Player owner, out ItemDrop.ItemData item)
        {
            owner = null;
            item = null;
            if (!pendingRecalls.TryGetValue(projectile, out var data)) return false;

            pendingRecalls.Remove(projectile);
            owner = data.owner;
            item = data.item;
            return true;
        }

        /// <summary>
        /// Grants the recalled spear directly back to the player's inventory (it's the exact same
        /// ItemData reference Attack.ConsumeItem removed when the spear was thrown - durability,
        /// quality, everything intact) rather than leaving a pickup on the ground. Falls back to
        /// dropping it at the player's feet only if the inventory is genuinely full.
        /// </summary>
        public static void RecallSpearToPlayer(Player player, ItemDrop.ItemData item)
        {
            if (player == null || item == null) return;

            if (!player.GetInventory().AddItem(item))
            {
                ItemDrop.DropItem(item, 1, player.transform.position, player.transform.rotation);
            }

            player.Message(MessageHud.MessageType.TopLeft, $"{item.m_shared.m_name} returns to you!");
        }

        /// <summary>
        /// Fallback for spears that never trigger SpawnOnHit at all (flew into the void, expired
        /// via vanilla's own TTL without a hit, got stuck somewhere weird) - force the recall once
        /// the 10s deadline passes, regardless of the underlying projectile's fate.
        /// </summary>
        public static void ProcessSpearRecallTimeouts()
        {
            List<Projectile> expired = null;
            foreach (var kvp in pendingRecalls)
            {
                if (Time.time >= kvp.Value.deadline)
                {
                    if (expired == null) expired = new List<Projectile>();
                    expired.Add(kvp.Key);
                }
            }

            if (expired == null) return;

            foreach (var projectile in expired)
            {
                var data = pendingRecalls[projectile];
                pendingRecalls.Remove(projectile);

                // Best-effort: if the projectile is still alive (just hasn't hit anything yet),
                // clear its own spawn-item reference too, so a later vanilla TTL-expiry SpawnOnHit
                // call (if the prefab has m_spawnOnTtl set) can't also drop a ground duplicate of
                // the spear we're about to grant directly to the player.
                if (projectile != null) projectile.m_spawnItem = null;

                RecallSpearToPlayer(data.owner, data.item);
            }
        }
        #endregion

        #region Level 30 - Spear Storm
        private const float SPEAR_STORM_PER_STACK = 0.03f;
        private const float SPEAR_STORM_DURATION = 5f;
        private const int SPEAR_STORM_MAX_STACKS = 5;
        private const string SPEAR_STORM_AS_KEY = "Lancer_SpearStorm_AS";

        private class StackData
        {
            public int stacks;
            public float expireTime;
        }

        // Separate stack tracks per weapon sub-type (spear vs polearm), matching
        // AxemasterPerkManager.rendingRhythmStacks' per-key, self-expiring dictionary pattern.
        private static readonly Dictionary<Player, StackData> spearStacks = new Dictionary<Player, StackData>();
        private static readonly Dictionary<Player, StackData> polearmStacks = new Dictionary<Player, StackData>();

        private static void AddStack(Dictionary<Player, StackData> tracker, Player player)
        {
            if (!tracker.TryGetValue(player, out var data) || Time.time >= data.expireTime)
            {
                data = new StackData();
                tracker[player] = data;
            }

            data.stacks = Mathf.Min(data.stacks + 1, SPEAR_STORM_MAX_STACKS);
            data.expireTime = Time.time + SPEAR_STORM_DURATION;
        }

        private static int GetStacks(Dictionary<Player, StackData> tracker, Player player)
        {
            if (!tracker.TryGetValue(player, out var data) || Time.time >= data.expireTime) return 0;
            return data.stacks;
        }

        /// <summary>
        /// Keeps the spear-branch attack-speed multiplier registered/cleared to match current
        /// stack count, called every tick from the periodic update (same reasoning as Sword
        /// Master's Dancing Steel refresh - stacks decaying over time need this, not just hits).
        /// </summary>
        public static void RefreshSpearStormAttackSpeed(Player player)
        {
            if (player == null) return;

            int stacks = GetStacks(spearStacks, player);
            if (stacks > 0) AnimationSpeedManager.Set(player, SPEAR_STORM_AS_KEY, 1f + stacks * SPEAR_STORM_PER_STACK);
            else AnimationSpeedManager.Clear(player, SPEAR_STORM_AS_KEY);
        }

        public static float GetPolearmStormStaggerBonus(Player player)
        {
            return GetStacks(polearmStacks, player) * SPEAR_STORM_PER_STACK;
        }

        public static void ProcessSpearStormHit(Player player, bool isPolearm)
        {
            AddStack(isPolearm ? polearmStacks : spearStacks, player);
        }
        #endregion

        #region Level 40 - Stormpoint
        public const float STORMPOINT_LIGHTNING_PERCENT = 0.12f;

        public static void ApplyStormpointLightning(ref HitData hit, float weaponDamage)
        {
            hit.m_damage.m_lightning += weaponDamage * STORMPOINT_LIGHTNING_PERCENT;
        }
        #endregion

        #region Level 50 - Impaling Momentum
        public const float IMPALING_MOMENTUM_BONUS = 0.10f;
        public const float IMPALING_MOMENTUM_THROW_DISTANCE = 15f;
        #endregion
    }

    /// <summary>
    /// Harmony patches to integrate Lancer perks with game systems
    /// </summary>
    [HarmonyPatch]
    public static class LancerPerkPatches
    {
        // Per-player tracking of whether the current attack is a special/secondary attack, needed
        // for Impaling Momentum's polearm-special-attack bonus.
        private static readonly Dictionary<Player, bool> pendingSpecialAttack = new Dictionary<Player, bool>();

        [HarmonyPatch(typeof(Humanoid), "StartAttack")]
        [HarmonyPrefix]
        public static void Humanoid_StartAttack_Lancer_Prefix(Humanoid __instance, bool secondaryAttack)
        {
            try
            {
                if (!(__instance is Player player)) return;

                var weapon = player.GetCurrentWeapon();
                if (weapon == null || !ClassCombatManager.IsSpearWeapon(weapon))
                {
                    pendingSpecialAttack.Remove(player);
                    return;
                }

                pendingSpecialAttack[player] = secondaryAttack;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Humanoid_StartAttack_Lancer_Prefix: {ex.Message}");
            }
        }

        #region Damage Patches
        /// <summary>
        /// Apply Stormpoint's instant lightning bonus (Level 40), Spear Storm's polearm stagger
        /// bonus (Level 30), and Impaling Momentum's conditional +10% (Level 50). Reach Advantage's
        /// flat +8% (Level 10) and Impaling Momentum's flat +15% (Level 50) live only in
        /// ClassCombatManager.GetLancerDamageBonus - not duplicated here.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Character_Damage_Lancer_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                if (hit.m_skill == Skills.SkillType.None) return;
                if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;

                var weapon = player.GetCurrentWeapon();
                if (!ClassCombatManager.IsSpearWeapon(weapon)) return;
                if (!LancerPerkManager.HasLancerPerk(player, 1)) return;

                bool isPolearm = ClassCombatManager.IsPolearmWeapon(weapon);
                float originalDamage = hit.GetTotalDamage();

                if (LancerPerkManager.HasLancerPerk(player, 30) && isPolearm)
                {
                    float staggerBonus = LancerPerkManager.GetPolearmStormStaggerBonus(player);
                    if (staggerBonus > 0f) hit.m_staggerMultiplier *= 1f + staggerBonus;
                }

                if (LancerPerkManager.HasLancerPerk(player, 40))
                {
                    LancerPerkManager.ApplyStormpointLightning(ref hit, originalDamage);
                }

                if (LancerPerkManager.HasLancerPerk(player, 50))
                {
                    // A melee spear thrust can't reach beyond ~2-3m, so a hit landing beyond 15m
                    // is necessarily a thrown spear - distance itself is a sufficient signal,
                    // no separate Projectile-path detection needed.
                    float distance = Vector3.Distance(player.transform.position, __instance.transform.position);
                    bool qualifies = isPolearm
                        ? (pendingSpecialAttack.TryGetValue(player, out var special) && special)
                        : distance > LancerPerkManager.IMPALING_MOMENTUM_THROW_DISTANCE;

                    if (qualifies)
                    {
                        float multiplier = 1f + LancerPerkManager.IMPALING_MOMENTUM_BONUS;
                        hit.m_damage.m_damage *= multiplier;
                        hit.m_damage.m_blunt *= multiplier;
                        hit.m_damage.m_slash *= multiplier;
                        hit.m_damage.m_pierce *= multiplier;
                        hit.m_damage.m_chop *= multiplier;
                        hit.m_damage.m_fire *= multiplier;
                        hit.m_damage.m_frost *= multiplier;
                        hit.m_damage.m_lightning *= multiplier;
                        hit.m_damage.m_poison *= multiplier;
                        hit.m_damage.m_spirit *= multiplier;
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Lancer_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Advance Spear Storm's stack count (Level 30) after a successful hit.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPostfix]
        public static void Character_Damage_Lancer_Postfix(Character __instance, HitData hit)
        {
            try
            {
                if (hit.m_skill == Skills.SkillType.None) return;
                if (!(hit.GetAttacker() is Player player) || __instance == null || __instance is Player) return;
                if (hit.GetTotalDamage() <= 0) return;

                var weapon = player.GetCurrentWeapon();
                if (!ClassCombatManager.IsSpearWeapon(weapon)) return;
                if (!LancerPerkManager.HasLancerPerk(player, 30)) return;

                LancerPerkManager.ProcessSpearStormHit(player, ClassCombatManager.IsPolearmWeapon(weapon));
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Lancer_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Spear Recall Patches
        /// <summary>
        /// Balanced Grip (Level 20): starts tracking a thrown spear the moment it's launched.
        /// Only 1H spears are thrown (polearms are melee-only, and IsSpearWeapon covers both),
        /// so this explicitly excludes polearms rather than relying on IsTwoHandedWeapon alone.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), "Setup", new System.Type[] { typeof(Character), typeof(Vector3), typeof(float), typeof(HitData), typeof(ItemDrop.ItemData), typeof(ItemDrop.ItemData) })]
        [HarmonyPostfix]
        public static void Projectile_Setup_Lancer_Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item)
        {
            try
            {
                if (!(owner is Player player) || item == null) return;
                if (!ClassCombatManager.IsSpearWeapon(item) || ClassCombatManager.IsPolearmWeapon(item)) return;
                if (!LancerPerkManager.HasLancerPerk(player, 20)) return;

                LancerPerkManager.TrackThrownSpear(__instance, player, item);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Projectile_Setup_Lancer_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Balanced Grip (Level 20): intercepts a tracked spear's landing. Vanilla's own
        /// SpawnOnHit is what turns a landed thrown spear into a ground pickup (m_spawnItem,
        /// set from Setup's item param since spears have m_respawnItemOnHit) - null it out before
        /// the original runs so that specific drop is skipped while every other spawn-on-hit
        /// effect (impact VFX, m_randomSpawnOnHit, etc.) still fires normally, then hand the item
        /// straight to the player's inventory instead.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), "SpawnOnHit")]
        [HarmonyPrefix]
        public static void Projectile_SpawnOnHit_Lancer_Prefix(Projectile __instance)
        {
            try
            {
                if (!LancerPerkManager.TryConsumeRecall(__instance, out var owner, out var item)) return;

                __instance.m_spawnItem = null;
                LancerPerkManager.RecallSpearToPlayer(owner, item);
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Projectile_SpawnOnHit_Lancer_Prefix: {ex.Message}");
            }
        }
        #endregion

        #region Movement Speed Patches
        /// <summary>
        /// Balanced Grip (Level 20): cancels an equipped polearm's own movement-speed penalty -
        /// 1H spears already have none in vanilla, so this only needs to key off polearms.
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetEquipmentMovementModifier")]
        [HarmonyPostfix]
        public static void Player_GetEquipmentMovementModifier_Lancer_Postfix(Player __instance, ref float __result)
        {
            try
            {
                if (!LancerPerkManager.HasLancerPerk(__instance, 20)) return;

                var weapon = __instance.GetCurrentWeapon();
                if (weapon == null || !ClassCombatManager.IsPolearmWeapon(weapon)) return;

                float weaponPenalty = weapon.m_shared.m_movementModifier;
                if (weaponPenalty < 0f) __result -= weaponPenalty;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_GetEquipmentMovementModifier_Lancer_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Periodic Updates
        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPostfix]
        public static void Game_Update_Lancer_Postfix()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player != null) LancerPerkManager.RefreshSpearStormAttackSpeed(player);

                LancerPerkManager.ProcessSpearRecallTimeouts();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Game_Update_Lancer_Postfix: {ex.Message}");
            }
        }
        #endregion
    }
}
