using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Logger = Jotunn.Logger;

// Combat system manager for handling class-based bonuses
public static class ClassCombatManager
{
    // Weapon type detection. Classified by the weapon's own m_skillType (the same
    // Skills.SkillType the game uses for weapon-skill leveling) rather than matching
    // keywords in the item name - authoritative, and doesn't need updating when new
    // named/legendary weapons are added in future updates.
    private static bool IsWeaponItemType(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared == null) return false;
        ItemDrop.ItemData.ItemType type = weapon.m_shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
               type == ItemDrop.ItemData.ItemType.Bow;
    }

    // Mirrors vanilla's own (private) ItemDrop.ItemData.IsTwoHanded() - confirmed via decompile.
    // The one generic 1H/2H primitive for classes that need different perk behavior per hand
    // count (Executioner now; Crusher/Sword Master/Lancer in later reworks).
    public static bool IsTwoHandedWeapon(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared == null) return false;
        ItemDrop.ItemData.ItemType type = weapon.m_shared.m_itemType;
        return type == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
               type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
               type == ItemDrop.ItemData.ItemType.Bow;
    }

    public static bool IsSwordWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Swords;
    }

    public static bool IsAxeWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Axes;
    }

    public static bool IsBowWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;

        // Crossbows count as Archer weapons alongside regular bows.
        return weapon.m_shared.m_skillType == Skills.SkillType.Bows ||
               weapon.m_shared.m_skillType == Skills.SkillType.Crossbows;
    }

    public static bool IsBluntWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Clubs;
    }

    public static bool IsKnifeWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Knives;
    }

    public static bool IsSpearWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;

        // Polearms (Atgeirs) count alongside Spears, as they did before this refactor.
        return weapon.m_shared.m_skillType == Skills.SkillType.Spears ||
               weapon.m_shared.m_skillType == Skills.SkillType.Polearms;
    }

    // Narrower than IsSpearWeapon - distinguishes the two Lancer weapon sub-types for perks
    // that behave differently on spears (thrown/thrust, 1H) vs polearms (swept, 2H).
    public static bool IsPolearmWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.Polearms;
    }

    public static bool IsUnarmedAttack(ItemDrop.ItemData weapon)
    {
        return weapon == null ||
               weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None ||
               weapon.m_shared.m_skillType == Skills.SkillType.Unarmed ||
               weapon.m_shared.m_name == "Unarmed";
    }

    // Same class <-> skill mapping as the IsXWeapon functions above, but keyed directly off
    // a Skills.SkillType (e.g. from HitData.m_skill) instead of an ItemData object. Needed
    // because ItemDrop.ItemData.GetCurrentWeapon() reads live inventory/equipment state that
    // is only reliably populated on the attacking player's own client - when a hit is
    // processed on a different peer (the ZDO owner of whoever got hit, not necessarily the
    // attacker), a remote attacker's weapon reads back as an "Unarmed" placeholder. HitData's
    // own m_skill field is set by the attacking client and travels with the networked hit,
    // so it's reliable regardless of which peer evaluates it.
    public static bool IsSkillAppropriateForClass(Skills.SkillType skill, string className)
    {
        switch (className)
        {
            case "Sword Master":
                return skill == Skills.SkillType.Swords;
            case "Executioner":
                return skill == Skills.SkillType.Axes;
            case "Archer":
                return skill == Skills.SkillType.Bows || skill == Skills.SkillType.Crossbows;
            case "Crusher":
                return skill == Skills.SkillType.Clubs;
            case "Assassin":
                return skill == Skills.SkillType.Knives;
            case "Brawler":
                return skill == Skills.SkillType.Unarmed;
            case "Wizard":
                return skill == Skills.SkillType.ElementalMagic;
            case "Warlock":
                return skill == Skills.SkillType.BloodMagic;
            case "Lancer":
                return skill == Skills.SkillType.Spears || skill == Skills.SkillType.Polearms;
            case "Bulwark":
                return true; // Bulwark gains XP from any combat (defensive class)
            default:
                return false;
        }
    }

    public static bool IsMagicWeapon(ItemDrop.ItemData weapon)
    {
        return IsElementalMagicWeapon(weapon) || IsBloodMagicWeapon(weapon);
    }

    /// <summary>
    /// Resolves a tamed/summoned Character's commanding player. Primary mechanism: the same live
    /// call vanilla itself uses to attribute a summon's skill gains back to its owner
    /// (Character.RaiseSkill's base implementation redirects through exactly this, confirmed via
    /// decompile) - always current, not a spawn-time snapshot, works for any roaming/followed
    /// creature (skeletons, trolls, etc.). Falls back to a time/position-correlated heuristic for
    /// summons that never get a follow target set at all - confirmed via testing that Staff of the
    /// Wild's vines have a MonsterAI component but, being stationary, are never "commanded" the
    /// way a roaming pet is, so the primary mechanism can't resolve them.
    /// </summary>
    public static Player GetCommandingPlayer(Character character)
    {
        var monsterAI = character?.GetComponent<MonsterAI>();
        if (monsterAI != null)
        {
            var followTarget = monsterAI.GetFollowTarget();
            var player = followTarget?.GetComponent<Player>();
            if (player != null) return player;
        }

        return ResolveFallback(character)?.owner;
    }

    /// <summary>
    /// Reads the skill a commanded/summoned Character's actions should credit to its owner
    /// (Tameable.m_levelUpOwnerSkill - the exact field vanilla's own Character.RaiseSkill reads
    /// for this same purpose). Lets summon-attributed XP be routed to the correct class the same
    /// way direct damage already is (via IsSkillAppropriateForClass), instead of hardcoding which
    /// class(es) can have summons - important once a player has more than one summon-capable
    /// class active at once (e.g. Wizard's vines and Warlock's skeletons simultaneously).
    /// </summary>
    public static Skills.SkillType GetCommandedSummonSkill(Character character)
    {
        var tameable = character?.GetComponent<Tameable>();
        if (tameable != null && tameable.m_levelUpOwnerSkill != Skills.SkillType.None)
        {
            return tameable.m_levelUpOwnerSkill;
        }

        return ResolveFallback(character)?.skill ?? Skills.SkillType.None;
    }

    #region Summon Ownership Fallback
    // Best-effort fallback for summons the primary mechanisms above can't resolve at all -
    // confirmed via testing that Staff of the Wild's vines have neither a commandable
    // MonsterAI follow-target (they're stationary, never "commanded" the way a roaming pet is)
    // nor a Tameable component to read a skill from. Records (player, weapon's skill, time,
    // position) on every SpawnAbility cast (see CombatPatches.SpawnAbility_Setup_Postfix below),
    // then correlates a recent, nearby cast against a Character the primary lookups failed on.
    private class PendingSummonCast
    {
        public long ownerPlayerID;
        public Skills.SkillType weaponSkill;
        public float time;
        public Vector3 position;
    }

    private class FallbackAttribution
    {
        public Player owner;
        public Skills.SkillType skill;
    }

    // 2s was too tight in practice: confirmed via testing that a stationary summon (Staff of the
    // Wild's vine) can easily take longer than that just to root/animate before landing its first
    // attack, by which point the cast record it needed to match against had already expired -
    // meaning it could NEVER resolve an owner, not just "only the first tick or two" as originally
    // assumed. 15s comfortably covers spawn/target latency for any summon type while the
    // hostile-faction filter in ResolveFallback (above) keeps a wider window from misattributing
    // an unrelated hostile creature that merely wanders past within it.
    private const float SUMMON_FALLBACK_TIME_WINDOW = 15f;
    private const float SUMMON_FALLBACK_RADIUS = 10f;
    private static readonly List<PendingSummonCast> pendingSummonCasts = new List<PendingSummonCast>();

    // ConditionalWeakTable (not a plain Dictionary) so a resolved entry is dropped automatically
    // once the summon Character itself is garbage-collected, rather than leaking one entry per
    // summon ever cast for the life of the session (same pattern AnimationSpeedManager already
    // uses for its own per-character tracking). Caching matters here specifically because a
    // stationary summon lives - and keeps dealing damage - long after the correlation window
    // that first identified it has closed; without caching, only its first tick or two would
    // ever resolve an owner (confirmed via testing).
    private static readonly ConditionalWeakTable<Character, FallbackAttribution> fallbackCache = new ConditionalWeakTable<Character, FallbackAttribution>();

    public static void RecordSummonCast(Player owner, Vector3 position, Skills.SkillType weaponSkill)
    {
        pendingSummonCasts.Add(new PendingSummonCast { ownerPlayerID = owner.GetPlayerID(), weaponSkill = weaponSkill, time = Time.time, position = position });
    }

    private static FallbackAttribution ResolveFallback(Character character)
    {
        if (character == null) return null;
        if (fallbackCache.TryGetValue(character, out var cached)) return cached;
        if (pendingSummonCasts.Count == 0) return null;

        // A wild hostile creature (e.g. a Troll fighting the player's actual summon) can easily
        // wander within the time/position window of a recent cast without being the summon at
        // all - confirmed via testing where a hostile Troll got permanently misattributed as the
        // player's own summon this way. IsMonsterFaction already returns false for anything tamed,
        // so this only excludes genuinely wild/hostile factions, never a real player summon.
        if (character.IsMonsterFaction(0f)) return null;

        Vector3 pos = character.transform.position;

        for (int i = pendingSummonCasts.Count - 1; i >= 0; i--)
        {
            var cast = pendingSummonCasts[i];
            if (Time.time - cast.time > SUMMON_FALLBACK_TIME_WINDOW)
            {
                pendingSummonCasts.RemoveAt(i);
                continue;
            }

            if (Vector3.Distance(cast.position, pos) <= SUMMON_FALLBACK_RADIUS)
            {
                var player = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == cast.ownerPlayerID);
                if (player == null) return null;

                DevLog.Log($"[Ownership] Fallback resolved '{character.name}' owner via a recent nearby cast: player={player.GetPlayerName()}, weaponSkill={cast.weaponSkill}");
                var result = new FallbackAttribution { owner = player, skill = cast.weaponSkill };
                fallbackCache.Add(character, result);
                return result;
            }
        }

        return null;
    }
    #endregion

    #region Poison DoT Attribution
    // Vanilla's own poison damage-over-time ticks (SE_Poison.UpdateStatusEffect, confirmed via
    // decompile) build a bare `new HitData()` with only m_point/m_damage.m_poison/m_hitType set -
    // never an attacker (StatusEffect.SetAttacker is a no-op base method SE_Poison never
    // overrides). So hit.GetAttacker() is permanently null for every tick after the first, no
    // matter who applied it - confirmed via testing that Staff of the Wild's vine poison ticks
    // against a target produced zero attacker info at all. The ORIGINAL applying hit (a real
    // Attack/Projectile hit, not a DoT tick) does carry a real attacker, so recording that here
    // (see CombatPatches.Character_Damage_PoisonSource_Postfix) lets later ticks against the same
    // target fall back to "whoever most recently poisoned this target" instead of going completely
    // unattributed.
    private class PoisonSource
    {
        public Character attacker;
        public float time;
    }

    private const float POISON_SOURCE_WINDOW = 60f;
    private static readonly ConditionalWeakTable<Character, PoisonSource> poisonSourceCache = new ConditionalWeakTable<Character, PoisonSource>();

    public static void RecordPoisonSource(Character target, Character attacker)
    {
        if (target == null || attacker == null) return;
        if (poisonSourceCache.TryGetValue(target, out var existing))
        {
            existing.attacker = attacker;
            existing.time = Time.time;
        }
        else
        {
            poisonSourceCache.Add(target, new PoisonSource { attacker = attacker, time = Time.time });
        }
    }

    public static Character GetRecentPoisonAttacker(Character target)
    {
        if (target != null && poisonSourceCache.TryGetValue(target, out var source) && Time.time - source.time <= POISON_SOURCE_WINDOW)
        {
            return source.attacker;
        }
        return null;
    }
    #endregion

    // Wizard (elemental) vs. Warlock (blood magic) - classified by skill type like every other
    // weapon check in this file. The named-staff lists in the design doc (Staff of Embers,
    // Trollstav, etc.) are just documentation of which real items use which skill type; nothing
    // here needs to hardcode item names.
    public static bool IsElementalMagicWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.ElementalMagic;
    }

    public static bool IsBloodMagicWeapon(ItemDrop.ItemData weapon)
    {
        if (!IsWeaponItemType(weapon)) return false;
        return weapon.m_shared.m_skillType == Skills.SkillType.BloodMagic;
    }

    // Calculate damage multiplier based on player's active classes and weapon type
    public static float GetClassDamageMultiplier(Player player, ItemDrop.ItemData weapon)
    {
        if (player == null) return 1f;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null || playerData.activeClasses.Count == 0) return 1f;

        float totalMultiplier = 1f;

        foreach (string activeClass in playerData.activeClasses)
        {
            float classMultiplier = GetClassSpecificDamageMultiplier(activeClass, playerData, weapon);
            if (classMultiplier > 1f)
            {
                // Add the bonus (not multiply) - so 10% + 15% = 25% total bonus
                totalMultiplier += (classMultiplier - 1f);
            }
        }

        return totalMultiplier;
    }

    private static float GetClassSpecificDamageMultiplier(string className, PlayerClassData playerData, ItemDrop.ItemData weapon)
    {
        int classLevel = playerData.GetClassLevel(className);

        switch (className)
        {
            case "Sword Master":
                return GetSwordMasterDamageBonus(classLevel, weapon);

            case "Executioner":
                return GetExecutionerDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Brawler":
                return GetBrawlerDamageBonus(classLevel, weapon);

            case "Wizard":
                return GetWizardDamageBonus(classLevel, weapon);

            case "Warlock":
                return GetWarlockDamageBonus(classLevel, weapon);

            case "Lancer":
                return GetLancerDamageBonus(classLevel, weapon);

            case "Bulwark":
                return GetBulwarkDamageBonus(classLevel, weapon);

            default:
                return 1f;
        }
    }

    // Class-specific damage bonus calculations
    private static float GetSwordMasterDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsSwordWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Blade Training - +7% sword damage (1H and greatswords alike)
        if (level >= 10) bonus += 0.07f;

        return 1f + bonus;
    }

    private static float GetExecutionerDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsAxeWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Chopper's Training - +8% axe damage (1H and 2H alike)
        if (level >= 10) bonus += 0.08f;

        // Level 50: Execute - +15% axe damage (flat component; the conditional
        // low-health bonus is handled separately in ExecutionerPerkManager)
        if (level >= 50) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetArcherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBowWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Practiced Aim - +7% bow/crossbow damage
        if (level >= 10) bonus += 0.07f;

        // Level 50: Deadeye - +15% bow/crossbow damage (flat component; the conditional
        // beyond-25m bonus is handled separately in ArcherPerkManager)
        if (level >= 50) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetCrusherDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBluntWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Bonebreaker - +8% blunt damage (1H maces and 2H hammers alike)
        if (level >= 10) bonus += 0.08f;

        // Level 50: Earthshaker - +15% blunt damage (flat component; the conditional
        // post-stagger attack-speed bonus is handled separately in CrusherPerkManager)
        if (level >= 50) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetAssassinDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsKnifeWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Cutthroat - +7% knife damage (the only flat bonus Assassin gets; Twist the
        // Knife at 50 is conditional on the target being poisoned, handled in AssassinPerkManager)
        if (level >= 10) bonus += 0.07f;

        return 1f + bonus;
    }

    private static float GetBrawlerDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsUnarmedAttack(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Bare-Knuckle Training - +8% unarmed damage
        if (level >= 10) bonus += 0.08f;

        return 1f + bonus;
    }

    private static float GetWizardDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsElementalMagicWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Eitr Weave - +7% elemental magic damage. Archmage (Level 50) carries no flat
        // damage bonus in this design - only the affinity-aura trigger, handled in WizardPerkManager.
        if (level >= 10) bonus += 0.07f;

        return 1f + bonus;
    }

    private static float GetWarlockDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsBloodMagicWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Forbidden Knowledge - +7% blood magic damage (summon damage bonus handled
        // separately in WarlockPerkManager, since summons don't route through the caster's own
        // Character.Damage calls). Sanguine Reclamation (Level 50) carries no flat damage bonus.
        if (level >= 10) bonus += 0.07f;

        return 1f + bonus;
    }

    private static float GetLancerDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        if (!IsSpearWeapon(weapon)) return 1f;

        float bonus = 0f;

        // Level 10: Reach Advantage - +8% pierce damage (spears and polearms alike)
        if (level >= 10) bonus += 0.08f;

        // Level 50: Impaling Momentum - +15% spear/polearm damage (flat component; the
        // conditional distance/special-attack bonus is handled separately in LancerPerkManager)
        if (level >= 50) bonus += 0.15f;

        return 1f + bonus;
    }

    private static float GetBulwarkDamageBonus(int level, ItemDrop.ItemData weapon)
    {
        // Bulwark doesn't get weapon damage bonuses, they get defensive bonuses
        // Those would be handled in different patches (block power, etc.)
        return 1f;
    }

    // Public version of damage multiplier calculation for console commands
    public static float GetClassSpecificDamageMultiplierPublic(string className, PlayerClassData playerData, ItemDrop.ItemData weapon)
    {
        int classLevel = playerData.GetClassLevel(className);

        switch (className)
        {
            case "Sword Master":
                return GetSwordMasterDamageBonus(classLevel, weapon);

            case "Executioner":
                return GetExecutionerDamageBonus(classLevel, weapon);

            case "Archer":
                return GetArcherDamageBonus(classLevel, weapon);

            case "Crusher":
                return GetCrusherDamageBonus(classLevel, weapon);

            case "Assassin":
                return GetAssassinDamageBonus(classLevel, weapon);

            case "Brawler":
                return GetBrawlerDamageBonus(classLevel, weapon);

            case "Wizard":
                return GetWizardDamageBonus(classLevel, weapon);

            case "Warlock":
                return GetWarlockDamageBonus(classLevel, weapon);

            case "Lancer":
                return GetLancerDamageBonus(classLevel, weapon);

            case "Bulwark":
                return GetBulwarkDamageBonus(classLevel, weapon);

            default:
                return 1f;
        }
    }
}

// Harmony patches for applying damage bonuses
[HarmonyPatch]
public static class CombatPatches
{
    // Patch for melee weapon damage - using the correct method name
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_Prefix(Character __instance, ref HitData hit)
    {
        try
        {
            // Skip synthetic damage-over-time ticks (e.g. a class's own bleed/poison DoT
            // re-entering Character.Damage on a timer). A real weapon swing always has
            // hit.m_skill set by Valheim's own attack pipeline; a hand-built HitData for a DoT
            // tick leaves it at the default None unless deliberately set otherwise. Without this,
            // a DoT that uses a physical damage field (m_slash/m_pierce/m_blunt/m_chop, the only
            // fields this prefix touches) would get re-multiplied by the class's own damage bonus
            // on every tick.
            if (hit.m_skill == Skills.SkillType.None) return;

            // Only apply to player attacks
            if (hit.GetAttacker() is Player player)
            {
                // Get the weapon used for this attack
                ItemDrop.ItemData weapon = player.GetCurrentWeapon();

                // Calculate class-based damage multiplier
                float multiplier = ClassCombatManager.GetClassDamageMultiplier(player, weapon);

                if (multiplier > 1f)
                {
                    // Apply the multiplier to physical damage
                    hit.m_damage.m_slash *= multiplier;
                    hit.m_damage.m_pierce *= multiplier;
                    hit.m_damage.m_blunt *= multiplier;
                    hit.m_damage.m_chop *= multiplier;
                }
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_Prefix: {ex.Message}");
        }
    }

    /// <summary>
    /// Feeds ClassCombatManager's summon-ownership fallback (see GetCommandingPlayer) - shared
    /// infrastructure, not tied to any one class, since any class's summon-type weapon can end up
    /// needing it (confirmed necessary for Wizard's stationary vine summons; Warlock's roaming
    /// skeletons/trolls don't need it, but recording the cast for them too is harmless).
    /// </summary>
    [HarmonyPatch(typeof(SpawnAbility), "Setup")]
    [HarmonyPostfix]
    public static void SpawnAbility_Setup_Postfix(Character owner)
    {
        try
        {
            if (owner is Player player)
            {
                // The currently-equipped weapon at the moment of casting is the reliable signal
                // for "what skill should this summon's actions credit" - simpler and more robust
                // than depending on SpawnAbility.Setup's own item parameter being populated the
                // same way for every possible summon-spawning path.
                var weaponSkill = player.GetCurrentWeapon()?.m_shared?.m_skillType ?? Skills.SkillType.None;
                ClassCombatManager.RecordSummonCast(player, player.transform.position, weaponSkill);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in SpawnAbility_Setup_Postfix: {ex.Message}");
        }
    }

    /// <summary>
    /// Feeds ClassCombatManager's poison-DoT attribution fallback (see GetRecentPoisonAttacker) -
    /// records the real attacker whenever a hit that actually carries one also deals poison
    /// damage, so later attacker-less DoT ticks against the same target can still be traced back
    /// to whoever poisoned them. Must be a PREFIX, not a postfix: Character.RPC_Damage (called
    /// synchronously from within Damage() for the local owner, confirmed via decompile) strips
    /// hit.m_damage.m_poison to 0 on this same HitData object - a reference type - before diverting
    /// it into the SE_Poison DoT system, so a postfix here would always see it already zeroed.
    /// </summary>
    [HarmonyPatch(typeof(Character), "Damage")]
    [HarmonyPrefix]
    public static void Character_Damage_PoisonSource_Prefix(Character __instance, HitData hit)
    {
        try
        {
            if (__instance == null) return;
            if (hit.m_damage.m_poison <= 0f) return;

            var attacker = hit.GetAttacker();
            if (attacker == null) return;

            ClassCombatManager.RecordPoisonSource(__instance, attacker);
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Character_Damage_PoisonSource_Prefix: {ex.Message}");
        }
    }
}

// Projectile damage tracking - inspired by your HarpoonFish mod approach
[HarmonyPatch]
public static class ProjectilePatches
{
    // Track active projectiles and their owners
    private static Dictionary<int, ProjectileInfo> trackedProjectiles = new Dictionary<int, ProjectileInfo>();

    private class ProjectileInfo
    {
        public Player owner;
        public ItemDrop.ItemData weapon;
        public float createdTime;
        public string projectileName;
    }

    // Patch projectile creation/setup methods to register them
    [HarmonyPatch(typeof(Projectile), "Setup")]
    [HarmonyPostfix]
    public static void Projectile_Setup_Postfix(Projectile __instance, Character owner, Vector3 velocity, float hitNoise, HitData hitData, ItemDrop.ItemData item)
    {
        try
        {
            if (owner is Player player && __instance != null)
            {
                // Determine weapon type from the projectile
                ItemDrop.ItemData weapon = GetWeaponFromProjectileContext(player, __instance, item);

                var projectileInfo = new ProjectileInfo
                {
                    owner = player,
                    weapon = weapon,
                    createdTime = Time.time,
                    projectileName = __instance.name
                };

                trackedProjectiles[__instance.GetInstanceID()] = projectileInfo;

                Debug.Log($"Registered projectile {__instance.name} for {player.GetPlayerName()} with weapon type {weapon?.m_shared?.m_name ?? "unknown"}");
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_Setup_Postfix: {ex.Message}");
        }
    }

    // Alternative patch for projectiles that don't use Setup
    [HarmonyPatch(typeof(Projectile), "Awake")]
    [HarmonyPostfix]
    public static void Projectile_Awake_Postfix(Projectile __instance)
    {
        try
        {
            // Some projectiles might not go through Setup, try to get owner info later
            if (__instance != null && !trackedProjectiles.ContainsKey(__instance.GetInstanceID()))
            {
                // We'll try to resolve the owner when the projectile hits something
                var projectileInfo = new ProjectileInfo
                {
                    owner = null, // Will resolve on hit
                    weapon = null,
                    createdTime = Time.time,
                    projectileName = __instance.name
                };

                trackedProjectiles[__instance.GetInstanceID()] = projectileInfo;
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Projectile_Awake_Postfix: {ex.Message}");
        }
    }

    // Patch the projectile hit with the correct signature
    //[HarmonyPatch(typeof(Projectile), "OnHit", new System.Type[] { typeof(Collider), typeof(Vector3), typeof(bool) })]
    //[HarmonyPrefix]
    //public static void Projectile_OnHit_Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water)
    //{
    //    try
    //    {
    //        if (__instance == null) return;

    //        int projectileId = __instance.GetInstanceID();
    //        if (!trackedProjectiles.TryGetValue(projectileId, out ProjectileInfo info)) return;

    //        // Try to resolve owner if we don't have it
    //        if (info.owner == null)
    //        {
    //            info.owner = ResolveProjectileOwner(__instance);
    //            info.weapon = GetWeaponFromProjectileContext(info.owner, __instance, null);
    //        }

    //        if (info.owner == null) return;

    //        // Check if we hit a valid creature
    //        Character hitCharacter = collider?.GetComponent<Character>();
    //        if (hitCharacter == null || hitCharacter is Player) return;

    //        // Calculate damage multiplier for projectile
    //        float multiplier = ClassCombatManager.GetClassDamageMultiplier(info.owner, info.weapon);

    //        if (multiplier > 1f)
    //        {
    //            Debug.Log($"Projectile {info.projectileName} hit {hitCharacter.name}, would apply {multiplier:F2}x multiplier");
    //            // Note: We can't modify the damage here as it hasn't been calculated yet
    //            // The damage bonus will need to be applied elsewhere
    //        }

    //        // Clean up tracking
    //        trackedProjectiles.Remove(projectileId);
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogError($"Error in Projectile_OnHit_Prefix: {ex.Message}");
    //    }
    //}

    // Helper method to determine weapon from projectile context
    private static ItemDrop.ItemData GetWeaponFromProjectileContext(Player player, Projectile projectile, ItemDrop.ItemData item)
    {
        if (item != null) return item; // Use provided item if available
        if (player == null || projectile == null) return null;

        string projectileName = projectile.name.ToLower();

        // Check for arrow projectiles
        if (projectileName.Contains("arrow"))
        {
            // Find bow in player's inventory
            return player.GetInventory().GetAllItems()
                .FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow);
        }

        // Check for spear projectiles
        if (projectileName.Contains("spear") || projectileName.Contains("harpoon"))
        {
            // Create dummy spear weapon for damage calculation
            var dummySpear = new ItemDrop.ItemData();
            dummySpear.m_shared = new ItemDrop.ItemData.SharedData();
            dummySpear.m_shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
            dummySpear.m_shared.m_name = "spear";
            return dummySpear;
        }

        // Fallback: check player's current weapon
        return player.GetCurrentWeapon();
    }

    // Helper method to resolve projectile owner (similar to your HarpoonFish approach)
    private static Player ResolveProjectileOwner(Projectile projectile)
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
            if (localPlayer != null && Vector3.Distance(localPlayer.transform.position, projectile.transform.position) < 50f)
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

    // Cleanup old projectiles periodically
    [HarmonyPatch(typeof(Game), "Update")]
    [HarmonyPostfix]
    public static void Game_Update_Postfix()
    {
        try
        {
            if (Time.frameCount % 300 != 0) return; // Check every 300 frames (~5 seconds)

            var toRemove = new List<int>();
            float currentTime = Time.time;

            foreach (var kvp in trackedProjectiles)
            {
                if (currentTime - kvp.Value.createdTime > 30f) // Remove after 30 seconds
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int id in toRemove)
            {
                trackedProjectiles.Remove(id);
            }
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error in Game_Update_Postfix (projectile cleanup): {ex.Message}");
        }
    }
}

// Additional console commands for testing combat bonuses
// Dev-only: excluded from Release builds.
#if DEBUG
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class CombatDebugCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("testdamage", "Test current weapon damage bonus",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var weapon = Player.m_localPlayer.GetCurrentWeapon();
                var multiplier = ClassCombatManager.GetClassDamageMultiplier(Player.m_localPlayer, weapon);
                var weaponName = weapon?.m_shared?.m_name ?? "Unarmed";

                args.Context.AddString($"Current weapon: {weaponName}");
                args.Context.AddString($"Damage multiplier: {multiplier:F2}x ({(multiplier - 1f) * 100f:F0}% bonus)");

                var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                if (playerData != null)
                {
                    args.Context.AddString($"Active classes: {string.Join(", ", playerData.activeClasses)}");
                }
            }
        );

        new Terminal.ConsoleCommand("weapontype", "Check what type the current weapon is detected as",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                var weapon = Player.m_localPlayer.GetCurrentWeapon();
                var weaponName = weapon?.m_shared?.m_name ?? "None";

                args.Context.AddString($"Current weapon: {weaponName}");
                args.Context.AddString($"Is Sword: {ClassCombatManager.IsSwordWeapon(weapon)}");
                args.Context.AddString($"Is Bow: {ClassCombatManager.IsBowWeapon(weapon)}");
                args.Context.AddString($"Is Blunt: {ClassCombatManager.IsBluntWeapon(weapon)}");
                args.Context.AddString($"Is Knife: {ClassCombatManager.IsKnifeWeapon(weapon)}");
                args.Context.AddString($"Is Spear: {ClassCombatManager.IsSpearWeapon(weapon)}");
                args.Context.AddString($"Is Unarmed: {ClassCombatManager.IsUnarmedAttack(weapon)}");
                args.Context.AddString($"Is Magic: {ClassCombatManager.IsMagicWeapon(weapon)} (Elemental: {ClassCombatManager.IsElementalMagicWeapon(weapon)}, Blood: {ClassCombatManager.IsBloodMagicWeapon(weapon)})");
            }
        );

        new Terminal.ConsoleCommand("classlevels", "Show all class levels and damage bonuses",
            delegate (Terminal.ConsoleEventArgs args)
            {
                try
                {
                    if (Player.m_localPlayer == null)
                    {
                        args.Context.AddString("No local player found!");
                        return;
                    }

                    var playerData = PlayerClassManager.GetPlayerData(Player.m_localPlayer);
                    if (playerData == null)
                    {
                        args.Context.AddString("No class data found!");
                        return;
                    }

                    var currentWeapon = Player.m_localPlayer.GetCurrentWeapon();
                    var weaponName = currentWeapon?.m_shared?.m_name ?? "Unarmed";

                    args.Context.AddString($"=== CLASS LEVELS & DAMAGE BONUSES ===");
                    args.Context.AddString($"Current weapon: {weaponName}");
                    args.Context.AddString($"Active classes: {string.Join(", ", playerData.activeClasses)}");
                    args.Context.AddString("");

                    foreach (var className in PlayerClassManager.GetAllClassNames())
                    {
                        int level = playerData.GetClassLevel(className);
                        float xp = playerData.GetClassXP(className);
                        bool isActive = playerData.activeClasses.Contains(className);

                        // Get damage multiplier for current weapon
                        float multiplier = 1f;
                        if (isActive)
                        {
                            multiplier = ClassCombatManager.GetClassSpecificDamageMultiplierPublic(className, playerData, currentWeapon);
                        }

                        string activeMarker = isActive ? " [ACTIVE]" : "";
                        string bonusText = multiplier > 1f ? $" (Damage: +{(multiplier - 1f) * 100f:F1}%)" : " (No bonus)";

                        args.Context.AddString($"{className}: Level {level} (XP: {xp:F0}){activeMarker}{bonusText}");

                        // Show next perk info
                        int nextPerkLevel = ((level / 10) + 1) * 10;
                        if (nextPerkLevel <= 50)
                        {
                            float totalXPForNextPerk = XPCurveHelper.GetTotalXPForLevel(nextPerkLevel);
                            float xpNeeded = totalXPForNextPerk - xp;
                            args.Context.AddString($"  Next perk at level {nextPerkLevel} (need {xpNeeded:F0} more XP)");
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    args.Context.AddString($"Error: {ex.Message}");
                }
            }
        );
    }
}
#endif