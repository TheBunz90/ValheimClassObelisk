using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System.Collections.Generic;
using System.Linq;

namespace ValheimClassObelisk
{
    /// <summary>
    /// Warlock class perk system - Blood Magic only (split out of the old combined Wizard).
    /// Health sacrifice, summons, and earned windows of aggressive life recovery.
    /// </summary>
    [HarmonyPatch]
    public static class WarlockPerkManager
    {
        private const float BLOOD_PACT_BONUS = 0.20f;
        private const float BLOOD_PACT_DURATION = 8f;
        // "Very-high-sacrifice" tier (Bloodwell +2 instead of +1) - the design doc names Trollstav
        // as the example but gives no numeric cutoff, so this treats any cast sacrificing >=15% of
        // the caster's max health (flat + percentage cost combined) as the high tier.
        private const float HIGH_SACRIFICE_PERCENT_THRESHOLD = 0.15f;
        private const int BLOODWELL_THRESHOLD = 4;
        private const float SANGUINE_RECLAMATION_DURATION = 20f;
        private const float SANGUINE_RECLAMATION_LIFESTEAL_PERCENT = 0.05f;
        private const float SANGUINE_RECLAMATION_HEAL_CAP_PER_SEC = 20f;
        private const float BLOOD_FEAST_FOOD_HEALTH_BONUS = 0.15f;
        private const float BLOOD_FEAST_REGEN_BONUS = 0.15f;
        private const float DARK_EFFICIENCY_EITR_MULT = 0.90f;

        public static bool HasWarlockPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Warlock)) return false;

            return playerData.GetClassLevel(PlayerClass.Warlock) >= requiredLevel;
        }

        // Description metadata, shown in the class selection GUI - locked perks display as "???"
        private const string Intro = "Blood magic specialists who turn health sacrifice, forbidden summons, and recovered life into dangerous sustained power.";
        private const string Outro = "Best for players who want Blood Magic to revolve around deliberate health sacrifice, empowered summons, and earning windows of aggressive life recovery.";

        public static readonly List<PerkInfo> Perks = new List<PerkInfo>
        {
            new PerkInfo { RequiredLevel = 10, Name = "Forbidden Knowledge", Description = "+7% blood magic damage and summoned creature damage." },
            new PerkInfo { RequiredLevel = 20, Name = "Blood Feast", Description = "Health granted by food and food-based health regeneration are increased by 15%." },
            new PerkInfo { RequiredLevel = 30, Name = "Blood Pact", Description = "Sacrificing health with a blood magic weapon grants Blood Pact for 8s, increasing blood magic and summoned creature damage by 20%. Additional qualifying sacrifices refresh the duration." },
            new PerkInfo { RequiredLevel = 40, Name = "Dark Efficiency", Description = "Blood magic weapon Eitr costs are reduced by 10%. Health sacrifice costs are unchanged." },
            new PerkInfo { RequiredLevel = 50, Name = "Sanguine Reclamation", Description = "Health-sacrificing blood magic casts build Bloodwell charges. At 4 charges, gain Sanguine Reclamation for 20s: blood magic and summoned creature damage restores 5% of damage dealt as health, capped at 5 health per second." },
        };

        public static string GetClassDescription(Player player)
        {
            int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Warlock) ?? 0;
            return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
        }

        #region Summon Ownership Tracking
        // Vanilla itself has a real, live mechanism for this - Character.RaiseSkill's base
        // (non-player) implementation redirects a tamed/summoned creature's skill gains to
        // whoever it's currently following, resolved via MonsterAI.GetFollowTarget() (confirmed
        // via decompile, and directly observed in-game: a summoned skeleton's kills raise the
        // owner's Blood Magic *skill*, not just XP). ClassCombatManager.GetCommandingPlayer
        // reuses that exact same call, so this is always current rather than a spawn-time
        // snapshot - a prior best-effort heuristic (time/position-correlated tagging) has been
        // replaced entirely by this.
        public static Player GetSummonOwner(Character character)
        {
            return ClassCombatManager.GetCommandingPlayer(character);
        }

        /// <summary>
        /// Resolves a hit's attacker to the Warlock it should count toward - either the player
        /// directly, or (if the attacker is a commanded summon) whoever it's following.
        /// </summary>
        public static Player ResolveAttributedPlayer(HitData hit)
        {
            var attacker = hit.GetAttacker();
            if (attacker is Player player) return player;
            return GetSummonOwner(attacker);
        }
        #endregion

        #region Health-Sacrifice Detection
        /// <summary>
        /// Returns the raw (unscaled by skill) health cost configured on the weapon's current
        /// attack, as both a flat amount and a percentage of max health - used only to classify
        /// standard vs. high-sacrifice casts for Bloodwell charges, deliberately not the
        /// skill-adjusted Attack.GetAttackHealth() value, so Blood Magic skill level can't
        /// accidentally change how many charges a cast is worth (per the design doc).
        /// </summary>
        public static bool IsHealthSacrificeCast(ItemDrop.ItemData weapon, bool secondaryAttack, out bool isHighSacrifice, Player caster)
        {
            isHighSacrifice = false;
            if (weapon?.m_shared == null) return false;

            var attack = secondaryAttack ? weapon.m_shared.m_secondaryAttack : weapon.m_shared.m_attack;
            if (attack == null) return false;

            if (attack.m_attackHealth <= 0f && attack.m_attackHealthPercentage <= 0f) return false;

            float effectivePercent = attack.m_attackHealthPercentage / 100f;
            if (attack.m_attackHealth > 0f && caster != null)
            {
                float maxHealth = caster.GetMaxHealth();
                if (maxHealth > 0f) effectivePercent += attack.m_attackHealth / maxHealth;
            }

            isHighSacrifice = effectivePercent >= HIGH_SACRIFICE_PERCENT_THRESHOLD;
            return true;
        }
        #endregion

        #region Level 30 - Blood Pact
        private static readonly Dictionary<Player, float> bloodPactExpire = new Dictionary<Player, float>();

        public static void TriggerBloodPact(Player player)
        {
            bloodPactExpire[player] = Time.time + BLOOD_PACT_DURATION;

            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_BloodPact".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_BloodPact";
            statusEffect.m_name = "Blood Pact";
            statusEffect.m_tooltip = "+20% blood magic and summon damage";
            statusEffect.m_icon = player.GetCurrentWeapon()?.GetIcon();
            statusEffect.m_ttl = BLOOD_PACT_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static bool HasBloodPactActive(Player player)
        {
            return player != null && bloodPactExpire.TryGetValue(player, out var expire) && Time.time < expire;
        }
        #endregion

        #region Level 50 - Sanguine Reclamation
        private class BloodwellData
        {
            public int charges;
            public float activeExpire;
        }

        private static readonly Dictionary<Player, BloodwellData> bloodwell = new Dictionary<Player, BloodwellData>();

        // Per-player, per-second lifesteal healing accumulator (for the 5 HP/sec cap).
        private class HealCapData
        {
            public int second;
            public float healedThisSecond;
        }

        private static readonly Dictionary<Player, HealCapData> healCapTracking = new Dictionary<Player, HealCapData>();

        public static bool IsSanguineReclamationActive(Player player)
        {
            return player != null && bloodwell.TryGetValue(player, out var data) && Time.time < data.activeExpire;
        }

        public static void AddBloodwellCharge(Player player, bool isHighSacrifice)
        {
            if (!bloodwell.TryGetValue(player, out var data))
            {
                data = new BloodwellData();
                bloodwell[player] = data;
            }

            // Charges don't accumulate while already active - guarantees a recovery gap between
            // activations (per the design doc).
            if (Time.time < data.activeExpire)
            {
                DevLog.Log($"[Warlock] {player.GetPlayerName()}: Bloodwell charge ignored - Sanguine Reclamation already active");
                return;
            }

            data.charges += isHighSacrifice ? 2 : 1;
            DevLog.Log($"[Warlock] {player.GetPlayerName()}: Bloodwell charge added ({(isHighSacrifice ? "high" : "standard")}), now {data.charges}/{BLOODWELL_THRESHOLD}");

            if (data.charges >= BLOODWELL_THRESHOLD)
            {
                data.charges = 0;
                TriggerSanguineReclamation(player, data);
            }
        }

        /// <summary>Current Bloodwell charge count, for the SE_Bloodwell display buff.</summary>
        public static int GetBloodwellCharges(Player player)
        {
            return bloodwell.TryGetValue(player, out var data) ? data.charges : 0;
        }

        private static void TriggerSanguineReclamation(Player player, BloodwellData data)
        {
            data.activeExpire = Time.time + SANGUINE_RECLAMATION_DURATION;
            DevLog.Log($"[Warlock] {player.GetPlayerName()}: Sanguine Reclamation triggered for {SANGUINE_RECLAMATION_DURATION:F0}s");

            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_SanguineReclamation".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_SanguineReclamation";
            statusEffect.m_name = "Sanguine Reclamation";
            statusEffect.m_tooltip = "Blood magic damage heals you (capped)";
            statusEffect.m_ttl = SANGUINE_RECLAMATION_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        /// <summary>
        /// Live "current Bloodwell charge count" display buff, so the player can see how close
        /// they are to Sanguine Reclamation. Shown at 0/4 as soon as the perk is unlocked, not
        /// only once charges start accumulating.
        /// </summary>
        public static void RefreshBloodwellDisplay(Player player)
        {
            if (player == null) return;

            var seman = player.GetSEMan();
            if (seman == null) return;

            bool hasPerk = HasWarlockPerk(player, 50);
            var existing = seman.GetStatusEffect("SE_Bloodwell".GetStableHashCode()) as SE_Bloodwell;

            if (!hasPerk)
            {
                if (existing != null) seman.RemoveStatusEffect("SE_Bloodwell".GetStableHashCode(), quiet: true);
                return;
            }

            int charges = GetBloodwellCharges(player);
            if (existing != null)
            {
                existing.SetCharges(charges, BLOODWELL_THRESHOLD);
                return;
            }

            var statusEffect = ScriptableObject.CreateInstance<SE_Bloodwell>();
            statusEffect.name = "SE_Bloodwell";
            statusEffect.m_icon = player.GetCurrentWeapon()?.GetIcon();
            statusEffect.m_ttl = 0f; // permanent while the perk is unlocked
            statusEffect.SetCharges(charges, BLOODWELL_THRESHOLD);

            seman.AddStatusEffect(statusEffect, resetTime: false);
        }

        /// <summary>
        /// Applies Sanguine Reclamation's lifesteal, capped at 5 HP/sec shared across every
        /// qualifying source in the same one-second window.
        /// </summary>
        public static void ApplyLifestealHeal(Player player, float damageDealt)
        {
            if (damageDealt <= 0f) return;

            int currentSecond = Mathf.FloorToInt(Time.time);
            if (!healCapTracking.TryGetValue(player, out var cap) || cap.second != currentSecond)
            {
                cap = new HealCapData { second = currentSecond, healedThisSecond = 0f };
                healCapTracking[player] = cap;
            }

            float remainingCap = SANGUINE_RECLAMATION_HEAL_CAP_PER_SEC - cap.healedThisSecond;
            if (remainingCap <= 0f) return;

            float healAmount = Mathf.Min(damageDealt * SANGUINE_RECLAMATION_LIFESTEAL_PERCENT, remainingCap);
            cap.healedThisSecond += healAmount;
            player.Heal(healAmount);
            DevLog.Log($"[Warlock] {player.GetPlayerName()}: Sanguine Reclamation healed {healAmount:F1} ({cap.healedThisSecond:F1}/{SANGUINE_RECLAMATION_HEAL_CAP_PER_SEC:F0} this second)");
        }
        #endregion

        #region Level 20 - Blood Feast
        public static void RefreshBloodFeastRegen(Player player)
        {
            if (player == null) return;

            var seman = player.GetSEMan();
            if (seman == null) return;

            bool hasPerk = HasWarlockPerk(player, 20);
            bool hasBuff = seman.HaveStatusEffect("SE_BloodFeast".GetStableHashCode());

            if (hasPerk && !hasBuff)
            {
                var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
                statusEffect.name = "SE_BloodFeast";
                statusEffect.m_name = "Blood Feast";
                statusEffect.m_tooltip = "+15% food-based health regeneration";
                statusEffect.m_healthRegenMultiplier = 1f + BLOOD_FEAST_REGEN_BONUS;
                statusEffect.m_ttl = 0f; // permanent while the perk conditions are met

                seman.AddStatusEffect(statusEffect, resetTime: false);
            }
            else if (!hasPerk && hasBuff)
            {
                seman.RemoveStatusEffect("SE_BloodFeast".GetStableHashCode(), quiet: true);
            }
        }
        #endregion

        #region Utility
        public static void ApplyDamageMultiplier(ref HitData hit, float multiplier)
        {
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

        public static void UpdateBuffs()
        {
            float currentTime = Time.time;

            var expiredPact = bloodPactExpire.Where(kvp => kvp.Key == null || currentTime >= kvp.Value).Select(kvp => kvp.Key).ToList();
            foreach (var player in expiredPact) bloodPactExpire.Remove(player);
        }
        #endregion

        /// <summary>
        /// Live "current Bloodwell charge count" display buff - a pure tooltip/icon tracker, no
        /// gameplay effect of its own. Mirrors the dynamic-tooltip pattern already used by
        /// Lancer's old SE_SpearStorm and Wizard's SE_ElementalAffinity (update a public field,
        /// refresh m_tooltip from it).
        /// </summary>
        public class SE_Bloodwell : SE_Stats
        {
            public int charges;
            public int maxCharges;

            public void SetCharges(int charges, int maxCharges)
            {
                this.charges = charges;
                this.maxCharges = maxCharges;
                m_name = "Bloodwell";
                m_tooltip = $"{charges} / {maxCharges} Bloodwell charges";
            }

            // The HUD status-effect icon has a built-in text overlay (Hud.UpdateStatusEffects
            // reads GetIconText() into the icon's "TimeText" label) - normally a countdown, but
            // since Bloodwell has no ttl, overriding it to show the charge count instead turns it
            // into a numeric badge on the icon itself, not just in the tooltip.
            public override string GetIconText()
            {
                return charges.ToString();
            }
        }
    }

    /// <summary>
    /// Harmony patches to integrate Warlock perks with game systems
    /// </summary>
    [HarmonyPatch]
    public static class WarlockPerkPatches
    {
        #region Attack-Start Patches (Bloodwell / Blood Pact trigger)
        /// <summary>
        /// Postfix, not Prefix, and gated on __result: Humanoid.StartAttack is called on every
        /// single FixedUpdate tick while an attack is held or queued (see Player's attack-input
        /// handling), but only actually starts an attack on one of those calls - it early-returns
        /// false internally the rest of the time. A Prefix here fired on every attempt regardless,
        /// which was double/triple-counting Bloodwell charges and Blood Pact triggers per real
        /// cast (confirmed via logging - charges jumped 0->2->4 near-instantly on a single cast).
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), "StartAttack")]
        [HarmonyPostfix]
        public static void Humanoid_StartAttack_Warlock_Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
        {
            try
            {
                if (!__result) return;
                if (!(__instance is Player player)) return;

                var weapon = player.GetCurrentWeapon();
                if (!ClassCombatManager.IsBloodMagicWeapon(weapon)) return;
                if (!WarlockPerkManager.HasWarlockPerk(player, 1)) return;

                if (!WarlockPerkManager.IsHealthSacrificeCast(weapon, secondaryAttack, out bool isHighSacrifice, player)) return;

                if (WarlockPerkManager.HasWarlockPerk(player, 30))
                {
                    WarlockPerkManager.TriggerBloodPact(player);
                }

                if (WarlockPerkManager.HasWarlockPerk(player, 50))
                {
                    WarlockPerkManager.AddBloodwellCharge(player, isHighSacrifice);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Humanoid_StartAttack_Warlock_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Damage Patches
        /// <summary>
        /// Applies Blood Pact's +20% (Level 30) to direct player Blood Magic damage and to
        /// attributed summon damage. Forbidden Knowledge's flat +7% (Level 10) lives in
        /// ClassCombatManager.GetWarlockDamageBonus for direct player damage only - summons never
        /// route through that system (it only fires for Player attackers), so both the flat bonus
        /// and Blood Pact are applied manually here for the summon case.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPrefix]
        public static void Character_Damage_Warlock_Prefix(Character __instance, ref HitData hit)
        {
            try
            {
                if (hit.m_skill == Skills.SkillType.None) return;
                if (__instance == null || __instance is Player) return;

                var attacker = hit.GetAttacker();

                if (attacker is Player directPlayer)
                {
                    var weapon = directPlayer.GetCurrentWeapon();
                    if (!ClassCombatManager.IsBloodMagicWeapon(weapon)) return;
                    if (!WarlockPerkManager.HasWarlockPerk(directPlayer, 30)) return;
                    if (!WarlockPerkManager.HasBloodPactActive(directPlayer)) return;

                    WarlockPerkManager.ApplyDamageMultiplier(ref hit, 1f + 0.20f);
                }
                else
                {
                    var owner = WarlockPerkManager.GetSummonOwner(attacker);
                    if (owner == null) return;

                    DevLog.Log($"[Warlock] Attributed damage from summon '{attacker?.name}' to player {owner.GetPlayerName()} (hasWarlock10={WarlockPerkManager.HasWarlockPerk(owner, 10)})");

                    if (!WarlockPerkManager.HasWarlockPerk(owner, 10)) return;

                    float multiplier = 1.07f; // Forbidden Knowledge's flat +7%, applied manually for summons
                    if (WarlockPerkManager.HasWarlockPerk(owner, 30) && WarlockPerkManager.HasBloodPactActive(owner))
                    {
                        multiplier += 0.20f;
                    }

                    WarlockPerkManager.ApplyDamageMultiplier(ref hit, multiplier);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Warlock_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Sanguine Reclamation's lifesteal (Level 50) - applies to both direct and
        /// attributed-summon Blood Magic damage while active.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPostfix]
        public static void Character_Damage_Warlock_Postfix(Character __instance, HitData hit)
        {
            try
            {
                if (hit.m_skill == Skills.SkillType.None) return;
                if (__instance == null || __instance is Player) return;
                if (hit.GetTotalDamage() <= 0) return;

                var owner = WarlockPerkManager.ResolveAttributedPlayer(hit);
                if (owner == null) return;
                if (!WarlockPerkManager.HasWarlockPerk(owner, 50)) return;
                if (!WarlockPerkManager.IsSanguineReclamationActive(owner)) return;

                WarlockPerkManager.ApplyLifestealHeal(owner, hit.GetTotalDamage());
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Warlock_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Eitr Cost Patches
        /// <summary>
        /// Dark Efficiency (Level 40): -10% Eitr cost on top of the vanilla skill-based discount.
        /// Deliberately does not touch GetAttackHealth() - health sacrifice cost is unchanged by
        /// design, since it's central to the Warlock's class loop.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "GetAttackEitr", new System.Type[] { })]
        [HarmonyPostfix]
        public static void Attack_GetAttackEitr_Warlock_Postfix(ref float __result, Character ___m_character, ItemDrop.ItemData ___m_weapon)
        {
            try
            {
                if (__result <= 0f) return;
                if (!(___m_character is Player player)) return;
                if (!ClassCombatManager.IsBloodMagicWeapon(___m_weapon)) return;
                if (!WarlockPerkManager.HasWarlockPerk(player, 40)) return;

                __result *= 0.90f;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Attack_GetAttackEitr_Warlock_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Food Patches
        /// <summary>
        /// Blood Feast (Level 20): +15% health granted by food. GetTotalFoodValue's `hp` out-param
        /// is m_baseHP plus the sum of active food's health contribution, so the base HP needs to
        /// be isolated first (via field injection) to scale only the food-derived portion.
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        [HarmonyPostfix]
        public static void Player_GetTotalFoodValue_Warlock_Postfix(Player __instance, ref float hp, float ___m_baseHP)
        {
            try
            {
                if (!WarlockPerkManager.HasWarlockPerk(__instance, 20)) return;

                float foodContribution = hp - ___m_baseHP;
                if (foodContribution <= 0f) return;

                hp = ___m_baseHP + foodContribution * 1.15f;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_GetTotalFoodValue_Warlock_Postfix: {ex.Message}");
            }
        }
        #endregion

        #region Periodic Updates
        [HarmonyPatch(typeof(Game), "Update")]
        [HarmonyPostfix]
        public static void Game_Update_Warlock_Postfix()
        {
            try
            {
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    WarlockPerkManager.RefreshBloodFeastRegen(player);
                    WarlockPerkManager.RefreshBloodwellDisplay(player);
                }

                WarlockPerkManager.UpdateBuffs();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Game_Update_Warlock_Postfix: {ex.Message}");
            }
        }
        #endregion
    }
}
