using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;

namespace ValheimClassObelisk
{
    /// <summary>
    /// Wizard class perk system - Elemental Magic only (Blood Magic split out into Warlock).
    /// Sustained casting, Eitr management, and four affinity-based Archmage auras (Fire/Frost/
    /// Lightning/Poison).
    /// </summary>
    [HarmonyPatch]
    public static class WizardPerkManager
    {
        private const float ARCANE_SURGE_REGEN_BONUS = 0.25f;
        private const float ARCANE_SURGE_DURATION = 6f;
        private const float ARCANE_EFFICIENCY_EITR_MULT = 0.90f;
        private const float ARCHMAGE_THRESHOLD = 500f;
        private const float ARCHMAGE_AURA_DURATION = 20f;
        private const float STORMSTRIDE_SPEED_BONUS = 0.20f;
        private const float STORMSTRIDE_JUMP_BONUS = 0.20f;
        private const float FROST_ARMOR_BONUS = 0.25f;
        private const float IMMOLATION_AURA_RADIUS = 5f;
        private const float IMMOLATION_AURA_DAMAGE = 15f;
        private const float VERDANT_AURA_RADIUS = 8f;
        private const float VERDANT_AURA_HEAL_PER_SEC = 2f;

        // Icon Resources
        private const string FROST_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.frost_armor_128.rgba";
        private const string FIRE_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.immolation_aura_128.rgba";
        private const string EITR_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.eitrweave_fist.rgba";
        private const string STORMSTRIDER_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.stormstrider_128.rgba";
        private const string VERDANT_AURA_ICON_RESOURCE = "ValheimClassObelisk.Resources.Icons.verdant_aura_128.rgba";

        private static Sprite _cachedFrostIcon;
        private static Sprite _cachedFireIcon;
        private static Sprite _cachedEitrIcon;
        private static Sprite _cachedStormstriderIcon;
        private static Sprite _cachedVerdantAuraIcon;

        public static bool HasWizardPerk(Player player, int requiredLevel)
        {
            if (player == null) return false;

            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null || !playerData.IsClassActive(PlayerClass.Wizard)) return false;

            return playerData.GetClassLevel(PlayerClass.Wizard) >= requiredLevel;
        }

        // Description metadata, shown in the class selection GUI - locked perks display as "???"
        private const string Intro = "Elemental magic specialists who turn Eitr, spell momentum, and elemental affinities into sustained magical pressure.";
        private const string Outro = "Best for players who want Elemental Magic to grow from efficient casting into sustained spell pressure and affinity-based Archmage auras.";

        public static readonly List<PerkInfo> Perks = new List<PerkInfo>
        {
            new PerkInfo { RequiredLevel = 10, Name = "Eitr Weave", Description = "+7% elemental magic damage." },
            new PerkInfo { RequiredLevel = 20, Name = "Arcane Nourishment", Description = "Eitr granted by food is increased by 15%." },
            new PerkInfo { RequiredLevel = 30, Name = "Arcane Surge", Description = "Dealing elemental magic damage grants +25% Eitr regeneration for 6s. Additional elemental magic damage refreshes the duration." },
            new PerkInfo { RequiredLevel = 40, Name = "Arcane Efficiency", Description = "Elemental magic weapon Eitr costs are reduced by 10%." },
            new PerkInfo { RequiredLevel = 50, Name = "Archmage", Description = "Dealing 500 damage of a single elemental affinity triggers its matching Archmage aura for 20s. Only one Archmage aura may be active at a time." },
        };

        public static string GetClassDescription(Player player)
        {
            int level = PlayerClassManager.GetPlayerData(player)?.GetClassLevel(PlayerClass.Wizard) ?? 0;
            return PerkDescriptionBuilder.Build(Intro, Perks, Outro, level);
        }

        #region Level 30 - Arcane Surge
        public static void TriggerArcaneSurge(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            seman.RemoveStatusEffect("SE_ArcaneSurge".GetStableHashCode(), quiet: true);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_ArcaneSurge";
            statusEffect.m_name = "Arcane Surge";
            statusEffect.m_tooltip = "+25% Eitr regeneration";
            statusEffect.m_icon = GetEitrIcon();
            statusEffect.m_eitrRegenMultiplier = 1f + ARCANE_SURGE_REGEN_BONUS;
            statusEffect.m_ttl = ARCANE_SURGE_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        #endregion

        #region Level 50 - Archmage
        public enum Affinity { Fire, Frost, Lightning, Poison }

        private class AffinityTracker
        {
            public Affinity? current;
            public float damage;
        }

        // Per-player, single rolling tracker (not module statics - the old combined Wizard had
        // that exact bug, fixed in Phase 4 and kept fixed here). Per feedback: only one affinity
        // is tracked at a time - dealing damage of a different element clears progress and starts
        // tracking the new one instead of building four meters in parallel.
        private static readonly Dictionary<Player, AffinityTracker> archmageTracker = new Dictionary<Player, AffinityTracker>();

        // Every aura name Archmage can activate - used to enforce "only one active at a time" by
        // clearing all of them before applying a newly-triggered one, and to check whether any is
        // currently active (damage dealt during an active aura isn't tracked at all).
        private static readonly string[] ArchmageAuraNames = { "SE_FrostArmor", "SE_ImmolationAura", "SE_Stormstride", "SE_VerdantAura" };

        private static bool IsAnyArchmageAuraActive(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return false;

            foreach (var auraName in ArchmageAuraNames)
            {
                if (seman.HaveStatusEffect(auraName.GetStableHashCode())) return true;
            }
            return false;
        }

        private static Affinity? GetDominantAffinity(HitData hit, out float damage)
        {
            float fire = hit.m_damage.m_fire;
            float frost = hit.m_damage.m_frost;
            float lightning = hit.m_damage.m_lightning;
            float poison = hit.m_damage.m_poison;

            float max = Mathf.Max(Mathf.Max(fire, frost), Mathf.Max(lightning, poison));
            if (max <= 0f) { damage = 0f; return null; }

            damage = max;
            if (fire == max) return Affinity.Fire;
            if (frost == max) return Affinity.Frost;
            if (lightning == max) return Affinity.Lightning;
            return Affinity.Poison;
        }

        public static void AccumulateArchmageDamage(Player player, HitData hit)
        {
            DevLog.Log($"[Wizard] Archmage hit breakdown for {player.GetPlayerName()}: damage={hit.m_damage.m_damage:F1}, blunt={hit.m_damage.m_blunt:F1}, slash={hit.m_damage.m_slash:F1}, pierce={hit.m_damage.m_pierce:F1}, chop={hit.m_damage.m_chop:F1}, fire={hit.m_damage.m_fire:F1}, frost={hit.m_damage.m_frost:F1}, lightning={hit.m_damage.m_lightning:F1}, poison={hit.m_damage.m_poison:F1}, spirit={hit.m_damage.m_spirit:F1}");

            // No tracking while an aura is already active - only one can be active at a time.
            if (IsAnyArchmageAuraActive(player))
            {
                DevLog.Log($"[Wizard] Archmage: not tracking, an aura is already active for {player.GetPlayerName()}");
                return;
            }

            var affinity = GetDominantAffinity(hit, out float damageDealt);
            if (affinity == null)
            {
                DevLog.Log($"[Wizard] Archmage: hit had no fire/frost/lightning/poison component - not tracked");
                return;
            }

            if (!archmageTracker.TryGetValue(player, out var tracker))
            {
                tracker = new AffinityTracker();
                archmageTracker[player] = tracker;
            }

            if (tracker.current != affinity)
            {
                // Switched elements - clear progress and start tracking the new one.
                DevLog.Log($"[Wizard] Archmage: {player.GetPlayerName()} switched affinity {(tracker.current == null ? "(none)" : tracker.current.ToString())} -> {affinity} (progress reset from {tracker.damage:F1})");
                tracker.current = affinity;
                tracker.damage = 0f;
            }

            tracker.damage += damageDealt;
            DevLog.Log($"[Wizard] Archmage: {player.GetPlayerName()} tracking {affinity} at {tracker.damage:F1}/{ARCHMAGE_THRESHOLD:F0} (+{damageDealt:F1})");

            if (tracker.damage >= ARCHMAGE_THRESHOLD)
            {
                var triggeredAffinity = affinity.Value;
                tracker.current = null;
                tracker.damage = 0f;
                RefreshAffinityBuff(player, tracker);
                DevLog.Log($"[Wizard] Archmage: {player.GetPlayerName()} reached threshold - triggering {triggeredAffinity} aura");
                TriggerArchmageAura(triggeredAffinity, player);
            }
            else
            {
                RefreshAffinityBuff(player, tracker);
            }
        }

        private static void TriggerArchmageAura(Affinity affinity, Player player)
        {
            switch (affinity)
            {
                case Affinity.Fire: ApplyImmolationAura(player); break;
                case Affinity.Frost: ApplyFrostArmor(player); break;
                case Affinity.Lightning: ApplyStormstride(player); break;
                case Affinity.Poison: ApplyVerdantAura(player); break;
            }
        }

        private static void ClearArchmageAuras(SEMan seman)
        {
            foreach (var auraName in ArchmageAuraNames)
            {
                seman.RemoveStatusEffect(auraName.GetStableHashCode(), quiet: true);
            }
        }

        /// <summary>
        /// Live "progress toward the next Archmage aura" buff, so the player can see how close
        /// they are. Removed entirely once tracking resets to no affinity (aura triggered, or
        /// simply never started).
        /// </summary>
        private static void RefreshAffinityBuff(Player player, AffinityTracker tracker)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            if (tracker.current == null)
            {
                seman.RemoveStatusEffect("SE_ElementalAffinity".GetStableHashCode(), quiet: true);
                return;
            }

            var existing = seman.GetStatusEffect("SE_ElementalAffinity".GetStableHashCode()) as SE_ElementalAffinity;
            if (existing != null)
            {
                // The same instance is reused across an affinity switch (never removed/recreated),
                // so the icon has to be refreshed here too - it was previously only ever set once,
                // at creation, which is why switching affinities kept showing the old element's icon.
                existing.m_icon = GetAffinityIcon(tracker.current.Value, player);
                existing.UpdateProgress(tracker.current.Value, tracker.damage, ARCHMAGE_THRESHOLD);
                return;
            }

            var statusEffect = ScriptableObject.CreateInstance<SE_ElementalAffinity>();
            statusEffect.name = "SE_ElementalAffinity";
            statusEffect.m_icon = GetAffinityIcon(tracker.current.Value, player);
            statusEffect.m_ttl = 0f; // permanent until it switches/clears/triggers
            statusEffect.UpdateProgress(tracker.current.Value, tracker.damage, ARCHMAGE_THRESHOLD);

            seman.AddStatusEffect(statusEffect, resetTime: false);
        }

        private static Sprite GetAffinityIcon(Affinity affinity, Player player)
        {
            switch (affinity)
            {
                case Affinity.Fire: return GetFireIcon();
                case Affinity.Frost: return GetFrostIcon();
                case Affinity.Lightning: return GetStormstriderIcon();
                case Affinity.Poison: return GetVerdantAuraIcon();
                default: return player.GetCurrentWeapon()?.GetIcon();
            }
        }
        #endregion

        #region Icon Loading
        private static Sprite GetFrostIcon()
        {
            if (_cachedFrostIcon != null) return _cachedFrostIcon;
            return LoadIconFromResource(FROST_ICON_RESOURCE, "Frost", ref _cachedFrostIcon);
        }

        private static Sprite GetFireIcon()
        {
            if (_cachedFireIcon != null) return _cachedFireIcon;
            return LoadIconFromResource(FIRE_ICON_RESOURCE, "Fire", ref _cachedFireIcon);
        }

        private static Sprite GetEitrIcon()
        {
            if (_cachedEitrIcon != null) return _cachedEitrIcon;
            return LoadIconFromResource(EITR_ICON_RESOURCE, "Eitr", ref _cachedEitrIcon);
        }

        private static Sprite GetStormstriderIcon()
        {
            if (_cachedStormstriderIcon != null) return _cachedStormstriderIcon;
            return LoadIconFromResource(STORMSTRIDER_ICON_RESOURCE, "Storm Strider", ref _cachedStormstriderIcon);
        }

        private static Sprite GetVerdantAuraIcon()
        {
            if (_cachedVerdantAuraIcon != null) return _cachedVerdantAuraIcon;
            return LoadIconFromResource(VERDANT_AURA_ICON_RESOURCE, "Verdant Aura", ref _cachedVerdantAuraIcon);
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
                        Jotunn.Logger.LogWarning($"[Wizard] Embedded icon not found: {resourceName}");
                        return null;
                    }

                    byte[] header = new byte[8];
                    int read = s.Read(header, 0, 8);
                    if (read != 8)
                    {
                        Jotunn.Logger.LogWarning($"[Wizard] {iconType} icon header corrupt.");
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
                        Jotunn.Logger.LogWarning($"[Wizard] {iconType} icon pixel data incomplete ({off}/{expectedBytes}).");
                        return null;
                    }

                    Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.filterMode = FilterMode.Bilinear;
                    tex.LoadRawTextureData(pixels);
                    tex.Apply(false, false);

                    cachedSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
                    return cachedSprite;
                }
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Wizard] Failed to load {iconType} icon: {ex}");
                return null;
            }
        }
        #endregion

        #region Archmage Aura Effects
        public static void ApplyFrostArmor(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            ClearArchmageAuras(seman);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_FrostArmor";
            statusEffect.m_name = "Frost Armor";
            statusEffect.m_tooltip = "+25% Armor and Fire Immunity";
            statusEffect.m_icon = GetFrostIcon();
            statusEffect.m_ttl = ARCHMAGE_AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static void ApplyImmolationAura(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            ClearArchmageAuras(seman);

            var statusEffect = ScriptableObject.CreateInstance<SE_ImmolationAura>();
            statusEffect.name = "SE_ImmolationAura";
            statusEffect.m_name = "Immolation Aura";
            statusEffect.m_tooltip = "Nearby enemies take fire damage";
            statusEffect.m_icon = GetFireIcon();
            statusEffect.m_ttl = ARCHMAGE_AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static void ApplyStormstride(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            ClearArchmageAuras(seman);

            var statusEffect = ScriptableObject.CreateInstance<SE_Stats>();
            statusEffect.name = "SE_Stormstride";
            statusEffect.m_name = "Storm Strider";
            statusEffect.m_tooltip = "+20% movement speed and jump height";
            statusEffect.m_icon = GetStormstriderIcon();
            statusEffect.m_speedModifier = STORMSTRIDE_SPEED_BONUS;
            // m_jumpModifier is added on top of the base jump velocity (SEMan.ApplyStatusEffectJumpMods
            // passes the same un-modified baseJump to every active effect, confirmed via decompile),
            // so the Y-only 0.2 here is a straight +20% jump height - X/Z are left at 0 so this
            // doesn't also affect horizontal jump distance.
            statusEffect.m_jumpModifier = new Vector3(0f, STORMSTRIDE_JUMP_BONUS, 0f);
            statusEffect.m_ttl = ARCHMAGE_AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }

        public static void ApplyVerdantAura(Player player)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            ClearArchmageAuras(seman);

            var statusEffect = ScriptableObject.CreateInstance<SE_VerdantAura>();
            statusEffect.name = "SE_VerdantAura";
            statusEffect.m_name = "Verdant Aura";
            statusEffect.m_tooltip = "Heals you and nearby allies over time";
            statusEffect.m_icon = GetVerdantAuraIcon();
            statusEffect.m_ttl = ARCHMAGE_AURA_DURATION;

            seman.AddStatusEffect(statusEffect, resetTime: true);
        }
        #endregion

        /// <summary>
        /// Live "progress toward an Archmage aura" display buff - a pure tooltip/icon tracker,
        /// no gameplay effect of its own. Mirrors the dynamic-tooltip pattern already used by
        /// Lancer's old SE_SpearStorm (update a public field, refresh m_tooltip from it).
        /// </summary>
        public class SE_ElementalAffinity : SE_Stats
        {
            public Affinity affinity;
            public float damage;
            public float threshold;

            public void UpdateProgress(Affinity affinity, float damage, float threshold)
            {
                this.affinity = affinity;
                this.damage = damage;
                this.threshold = threshold;
                m_name = $"{affinity} Affinity";
                m_tooltip = $"{damage:F0} / {threshold:F0} {affinity} damage dealt";
            }

            // Same numeric-badge technique as Warlock's SE_Bloodwell - GetIconText normally shows
            // a countdown (this has no ttl), overridden here to show accumulated damage instead.
            public override string GetIconText()
            {
                return Mathf.RoundToInt(damage).ToString();
            }
        }

        public class SE_ImmolationAura : SE_Stats
        {
            private float lastDamageTime = 0f;
            private const float DamageInterval = 1f;

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);

                if (Time.time - lastDamageTime >= DamageInterval)
                {
                    ApplyAuraDamage();
                    lastDamageTime = Time.time;
                }
            }

            private void ApplyAuraDamage()
            {
                if (!(m_character is Player player)) return;

                var enemies = new List<Character>();
                Character.GetCharactersInRange(player.transform.position, IMMOLATION_AURA_RADIUS, enemies);

                foreach (var enemy in enemies)
                {
                    if (enemy == player || enemy.IsDead()) continue;
                    if (enemy.IsPlayer() || enemy.IsTamed()) continue;

                    var hitData = new HitData();
                    hitData.m_damage.m_fire = IMMOLATION_AURA_DAMAGE;
                    hitData.m_point = enemy.GetCenterPoint();
                    hitData.m_dir = (enemy.transform.position - player.transform.position).normalized;
                    hitData.SetAttacker(player);

                    enemy.Damage(hitData);
                }
            }
        }

        public class SE_VerdantAura : SE_Stats
        {
            private float lastHealTime = 0f;
            private const float HealInterval = 1f;

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);

                if (Time.time - lastHealTime >= HealInterval)
                {
                    ApplyHealPulse();
                    lastHealTime = Time.time;
                }
            }

            private void ApplyHealPulse()
            {
                if (!(m_character is Player player)) return;

                var nearby = new List<Character>();
                Character.GetCharactersInRange(player.transform.position, VERDANT_AURA_RADIUS, nearby);

                foreach (var target in nearby)
                {
                    if (target == player || target.IsDead()) continue;
                    if (!(target.IsPlayer() || target.IsTamed())) continue;

                    target.Heal(VERDANT_AURA_HEAL_PER_SEC);
                }

                player.Heal(VERDANT_AURA_HEAL_PER_SEC);
            }
        }
    }

    /// <summary>
    /// Harmony patches to integrate Wizard perks with game systems
    /// </summary>
    [HarmonyPatch]
    public static class WizardPerkPatches
    {
        /// <summary>
        /// Trigger Arcane Surge's Eitr-regen buff (Level 30) and accumulate Archmage's per-affinity
        /// damage tracking (Level 50) after an elemental magic hit lands. Eitr Weave's flat +7%
        /// (Level 10) lives only in ClassCombatManager.GetWizardDamageBonus - not duplicated here.
        /// </summary>
        [HarmonyPatch(typeof(Character), "Damage")]
        [HarmonyPostfix]
        public static void Character_Damage_Wizard_Postfix(Character __instance, HitData hit)
        {
            try
            {
                if (__instance == null || __instance is Player) return;
                if (hit.GetTotalDamage() <= 0) return;

                // Vanilla's own poison DoT ticks never carry an attacker (SE_Poison builds a bare
                // HitData with no attacker set, confirmed via decompile) - fall back to whoever
                // most recently applied poison to this target (see
                // ClassCombatManager.RecordPoisonSource) so Staff of the Wild's vine ticks still
                // resolve instead of silently going untracked.
                var attacker = hit.GetAttacker() ?? ClassCombatManager.GetRecentPoisonAttacker(__instance);

                if (attacker is Player player)
                {
                    var weapon = player.GetCurrentWeapon();
                    if (!ClassCombatManager.IsElementalMagicWeapon(weapon)) return;
                    if (!WizardPerkManager.HasWizardPerk(player, 1)) return;

                    if (WizardPerkManager.HasWizardPerk(player, 30))
                    {
                        WizardPerkManager.TriggerArcaneSurge(player);
                    }

                    if (WizardPerkManager.HasWizardPerk(player, 50))
                    {
                        WizardPerkManager.AccumulateArchmageDamage(player, hit);
                    }
                }
                else
                {
                    // Attributed summon damage (e.g. Staff of the Wild's vines) counts toward
                    // Archmage tracking (Level 50) for whoever commands it - same live
                    // MonsterAI.GetFollowTarget()-based resolution used for Warlock's summons.
                    // Arcane Surge is deliberately NOT triggered from summon damage - it's meant
                    // to reward the player's own continuous casting, not an idle summon ticking.
                    var owner = ClassCombatManager.GetCommandingPlayer(attacker);
                    if (owner == null) return;
                    if (!WizardPerkManager.HasWizardPerk(owner, 50)) return;

                    // Mirrors the direct-player IsElementalMagicWeapon gate above - without this,
                    // a Warlock summon (skeleton/troll) dealing any poison/fire/frost/lightning
                    // damage would also feed Wizard's Archmage tracking for a player running both
                    // classes at once, since only "owner has Wizard 50" was checked, not "this
                    // summon is actually one of Wizard's own".
                    if (ClassCombatManager.GetCommandedSummonSkill(attacker) != Skills.SkillType.ElementalMagic) return;

                    WizardPerkManager.AccumulateArchmageDamage(owner, hit);
                }
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Character_Damage_Wizard_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Arcane Efficiency (Level 40): reduces Eitr cost by 10% on top of whatever the vanilla
        /// Elemental Magic skill has already discounted (GetAttackEitr already applies up to a 33%
        /// skill-based discount before this runs).
        /// </summary>
        [HarmonyPatch(typeof(Attack), "GetAttackEitr", new System.Type[] { })]
        [HarmonyPostfix]
        public static void Attack_GetAttackEitr_Wizard_Postfix(ref float __result, Character ___m_character, ItemDrop.ItemData ___m_weapon)
        {
            try
            {
                if (__result <= 0f) return;
                if (!(___m_character is Player player)) return;
                if (!ClassCombatManager.IsElementalMagicWeapon(___m_weapon)) return;
                if (!WizardPerkManager.HasWizardPerk(player, 40)) return;

                __result *= 0.90f;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Attack_GetAttackEitr_Wizard_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Arcane Nourishment (Level 20): +15% Eitr granted by food. GetTotalFoodValue's `eitr`
        /// out-param already equals the sum of all active food's Eitr contribution (it has no
        /// separate "base Eitr" term the way health does), so scaling the whole value is exactly
        /// equivalent to scaling each qualifying food's own contribution.
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        [HarmonyPostfix]
        public static void Player_GetTotalFoodValue_Wizard_Postfix(Player __instance, ref float eitr)
        {
            try
            {
                if (!WizardPerkManager.HasWizardPerk(__instance, 20)) return;
                eitr *= 1.15f;
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Error in Player_GetTotalFoodValue_Wizard_Postfix: {ex.Message}");
            }
        }
    }
}
