using BepInEx.Configuration;
using Jotunn.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using Logger = Jotunn.Logger;

/// <summary>
/// Owns the data-driven creature/boss/level-curve XP table introduced by the "creature_xp_table.json"
/// / "player_xp_milestones.json" design (see docs/XP_System_Documentation_Updated.md). Every value is
/// a BepInEx ConfigEntry in its own file (not the main plugin config, to keep that one small) so
/// players can hand-edit balance without touching code, and the whole file is registered with
/// Jotunn's SynchronizationManager with every entry tagged IsAdminOnly - confirmed via decompiling
/// Jotunn.dll that this is Jotunn's real, built-in mechanism for "server value overrides local value":
/// SynchronizationManager patches ConfigEntryBase get/set to lock IsAdminOnly entries for non-admins
/// once a server pushes its own values down on join.
/// </summary>
public static class XPConfigManager
{
    private class CreatureXPSeed
    {
        // First entry is the "primary" prefab - the one the actual ConfigEntry is bound under.
        // Every prefab in this array (including the primary) resolves to that same entry at lookup
        // time, so biome/skin/sleeping variants of the same creature share one editable value.
        public string[] Prefabs;
        public string DisplayName;
        public string Category;
        public float BaseXP;

        public CreatureXPSeed(float baseXP, string displayName, string category, params string[] prefabs)
        {
            BaseXP = baseXP;
            DisplayName = displayName;
            Category = category;
            Prefabs = prefabs;
        }
    }

    // Base (0-star) XP per creature, cross-referenced against a live `listcreatures` dump of
    // ZNetScene's actual prefab registry (docs/creature_prefabs.txt) - not guessed from the design
    // doc's display names alone, since several don't match Valheim's real internal names at all
    // (e.g. "The Elder" is really "gd_king", "Moder" is "Dragon"). See conversation history for the
    // full cross-reference notes on ambiguous/inferred entries (marked below).
    private static readonly List<CreatureXPSeed> CreatureSeeds = new List<CreatureXPSeed>
    {
        // --- Meadows ---
        new CreatureXPSeed(2,   "Neck",             "Meadows", "Neck"),
        new CreatureXPSeed(3,   "Greyling",         "Meadows", "Greyling"),
        new CreatureXPSeed(3,   "Boar",              "Meadows", "Boar"),
        new CreatureXPSeed(5,   "Deer",              "Meadows", "Deer", "Deer_White"),

        // --- Black Forest ---
        new CreatureXPSeed(7,   "Greydwarf",         "BlackForest", "Greydwarf", "Greydwarf_Frozen"),
        new CreatureXPSeed(9,   "Skeleton",          "BlackForest", "Skeleton", "Skeleton_Meadows", "Skeleton_Meadows_noarcher", "Skeleton_Swamps", "Skeleton_Swamps_noarcher", "Skeleton_Mountains", "Skeleton_Mountains_noarcher", "Skeleton_NoArcher", "Skeleton_DeepNorth", "Skeleton_aspect"),
        new CreatureXPSeed(12,  "Ghost",             "BlackForest", "Ghost", "Ghost_sleeping", "Ghost_old"),
        new CreatureXPSeed(14,  "Greydwarf Shaman",  "BlackForest", "Greydwarf_Shaman", "Greydwarf_Shaman_Frozen"),
        new CreatureXPSeed(18,  "Rancid Remains",    "BlackForest", "Skeleton_Poison"),
        new CreatureXPSeed(22,  "Greydwarf Brute",   "BlackForest", "Greydwarf_Elite"),
        new CreatureXPSeed(80,  "Troll",             "BlackForest", "Troll", "Troll_sleeping"),
        new CreatureXPSeed(95,  "Bear",              "BlackForest", "Bjorn", "Bjorn_sleeping"), // inferred: no literal "Bear" prefab exists

        // --- Swamp ---
        new CreatureXPSeed(4,   "Surtling",          "Swamp", "Surtling"),
        new CreatureXPSeed(18,  "Blob",              "Swamp", "Blob"),
        new CreatureXPSeed(18,  "Leech",             "Swamp", "Leech", "Leech_cave"),
        new CreatureXPSeed(25,  "Draugr",            "Swamp", "Draugr", "Draugr_sleeping", "Draugr_Ranged", "Draugr_Ranged_sleeping"),
        new CreatureXPSeed(35,  "Wraith",            "Swamp", "Wraith"),
        new CreatureXPSeed(40,  "Oozer",             "Swamp", "BlobElite"),
        new CreatureXPSeed(45,  "Draugr Elite",      "Swamp", "Draugr_Elite", "Draugr_Elite_sleeping"),
        new CreatureXPSeed(110, "Kvastur",           "Swamp", "BogWitchKvastur"),
        new CreatureXPSeed(150, "Abomination",       "Swamp", "Abomination"),

        // --- Mountain ---
        new CreatureXPSeed(8,   "Bat",               "Mountain", "Bat", "Bat_Swamp"),
        new CreatureXPSeed(22,  "Ulv",               "Mountain", "Ulv"),
        new CreatureXPSeed(30,  "Drake",             "Mountain", "Hatchling"),
        new CreatureXPSeed(30,  "Wolf",              "Mountain", "Wolf"),
        new CreatureXPSeed(60,  "Cultist",           "Mountain", "Fenring_Cultist"), // inferred: base ranged Mountain caster
        new CreatureXPSeed(75,  "Fenring",           "Mountain", "Fenring"),
        new CreatureXPSeed(250, "Stone Golem",       "Mountain", "StoneGolem"),

        // --- Plains ---
        new CreatureXPSeed(25,  "Deathsquito",       "Plains", "Deathsquito"),
        new CreatureXPSeed(45,  "Fuling",            "Plains", "Goblin", "GoblinArcher"),
        new CreatureXPSeed(55,  "Growth",            "Plains", "BlobTar"),
        new CreatureXPSeed(55,  "Fuling Shaman",     "Plains", "GoblinShaman"),
        new CreatureXPSeed(175, "Fuling Berserker",  "Plains", "GoblinBrute"),
        new CreatureXPSeed(175, "Lox",               "Plains", "Lox"),
        new CreatureXPSeed(275, "Vile",              "Plains", "Unbjorn"),

        // --- Mistlands ---
        new CreatureXPSeed(5,   "Hare",              "Mistlands", "Hare"), // passive wildlife, not in the original design doc table
        new CreatureXPSeed(12,  "Seeker Brood",      "Mistlands", "SeekerBrood"),
        new CreatureXPSeed(25,  "Tick",              "Mistlands", "Tick"),
        new CreatureXPSeed(75,  "Seeker",            "Mistlands", "Seeker"),
        new CreatureXPSeed(90,  "Dvergr Rogue",      "Mistlands", "Dverger"),
        new CreatureXPSeed(125, "Dvergr Mage",       "Mistlands", "DvergerMage", "DvergerMageFire", "DvergerMageIce", "DvergerMageSupport"),
        new CreatureXPSeed(325, "Seeker Soldier",    "Mistlands", "SeekerBrute"), // inferred: heavy-armored Seeker variant
        new CreatureXPSeed(350, "Gjall",             "Mistlands", "Gjall"),

        // --- Ashlands ---
        new CreatureXPSeed(55,  "Volture",           "Ashlands", "Volture"),
        new CreatureXPSeed(60,  "Charred Twitcher",  "Ashlands", "Charred_Twitcher"),
        new CreatureXPSeed(65,  "Charred Marksman",  "Ashlands", "Charred_Archer"), // inferred: ranged Charred unit
        new CreatureXPSeed(75,  "Lava Blob",         "Ashlands", "BlobLava"),
        new CreatureXPSeed(90,  "Skugg",             "Ashlands", "piece_Charred_Balista"), // structural caveat - see XPTrackingPatches
        new CreatureXPSeed(120, "Charred Warrior",   "Ashlands", "Charred_Melee"),
        new CreatureXPSeed(160, "Charred Warlock",   "Ashlands", "Charred_Mage"),
        new CreatureXPSeed(175, "Asksvin",           "Ashlands", "Asksvin"),
        new CreatureXPSeed(225, "Bonemaw",           "Ashlands", "BonemawSerpent"),
        new CreatureXPSeed(375, "Fallen Valkyrie",   "Ashlands", "FallenValkyrie"),
        new CreatureXPSeed(500, "Morgen",            "Ashlands", "Morgen", "Morgen_NonSleeping"),

        // --- Deep North ---
        new CreatureXPSeed(5,   "Baby Seal",         "DeepNorth", "Seal_Pup"),
        new CreatureXPSeed(5,   "Seal",              "DeepNorth", "Seal"),
        new CreatureXPSeed(0,   "Shadow",            "DeepNorth", "ShadowPerson"), // inferred: only unkillable-feeling DeepNorth entity
        new CreatureXPSeed(150, "Elaking",           "DeepNorth", "Elaking", "ElakingLantern"),
        new CreatureXPSeed(250, "Moose",             "DeepNorth", "Moose"),
        new CreatureXPSeed(275, "Hexen",             "DeepNorth", "JotunWitch"), // inferred: no literal "Hexen" prefab exists
        new CreatureXPSeed(375, "Krigen",            "DeepNorth", "JotunWarrior", "JotunWarriorDualWield"), // inferred: no literal "Krigen" prefab exists
        new CreatureXPSeed(600, "Barka",             "DeepNorth", "Barka"),
        new CreatureXPSeed(800, "Gammeltroll",       "DeepNorth", "TrollFrost"), // inferred: "old troll" = the DeepNorth troll variant

        // --- Special Encounters ---
        // Brenna, Geirrhafa, Zil & Thungr, and Lord Reto don't appear as their own prefabs at all -
        // they're almost certainly a named instance of an ordinary creature (a Troll/Fenring/Goblin-
        // type with a unique name applied at spawn), which a prefab-name lookup alone can't
        // distinguish from the regular version. Deferred - see TODO.md. Only Fallen Warrior and
        // Kall Aspects map to real, distinct prefabs.
        new CreatureXPSeed(300, "Fallen Warrior",    "SpecialEncounter", "FallenWarrior"),
        new CreatureXPSeed(0,   "Kall Aspects",      "SpecialEncounter", "Aspect_Bonemass", "Aspect_Eikthyr", "Aspect_Elder", "Aspect_Fader", "Aspect_Moder", "Aspect_SeekerQueen", "Aspect_TentaRoot", "Aspect_Yagluth"),

        // --- Bosses ---
        // Well-known internal-name mismatches: The Elder = gd_king, Moder = Dragon, Yagluth = GoblinKing.
        new CreatureXPSeed(250,  "Eikthyr",             "Boss", "Eikthyr"),
        new CreatureXPSeed(500,  "The Elder",           "Boss", "gd_king"),
        new CreatureXPSeed(1000, "Bonemass",            "Boss", "Bonemass"),
        new CreatureXPSeed(1500, "Moder",               "Boss", "Dragon"),
        new CreatureXPSeed(2000, "Yagluth",              "Boss", "GoblinKing"),
        new CreatureXPSeed(3500, "The Queen",           "Boss", "SeekerQueen"),
        new CreatureXPSeed(5000, "Fader",               "Boss", "Fader"),
        // Kall Fimbulbringer is a 3-phase fight (FrozenKing -> _p2 -> _p3, each its own prefab that
        // presumably "dies" as the fight advances). Only the true final phase pays out, so the same
        // 8000 XP isn't awarded three times over one fight - see the 0-XP FrozenKing/_p2 entries below.
        new CreatureXPSeed(8000, "Kall Fimbulbringer",  "Boss", "FrozenKing_p3"),
        new CreatureXPSeed(0,    "Kall Fimbulbringer (early phase)", "Boss", "FrozenKing", "FrozenKing_p2"),
    };

    private static ConfigFile _xpConfig;
    private static readonly Dictionary<string, ConfigEntry<float>> _creatureXP = new Dictionary<string, ConfigEntry<float>>();
    private static readonly Dictionary<int, ConfigEntry<float>> _levelCumulativeXP = new Dictionary<int, ConfigEntry<float>>();
    private static readonly Dictionary<int, ConfigEntry<float>> _starMultipliers = new Dictionary<int, ConfigEntry<float>>();
    private const int MaxLevel = 50;
    private const int MaxStarTier = 5; // extensible per the design doc; only 0-2 are reachable in vanilla today

    // Design doc's own default milestone table (docs/player_xp_milestones.json) - cumulative XP
    // required to REACH each level. Level 1 is always 0 and isn't bound as a config entry.
    private static readonly float[] DefaultCumulativeXP =
    {
        0, 25, 60, 105, 160, 225, 300, 385, 480, 585,
        735, 915, 1125, 1365, 1635, 1935, 2265, 2625, 3025, 3475,
        3950, 4450, 4975, 5525, 6100, 6700, 7325, 7975, 8650, 9350,
        10060, 10780, 11510, 12250, 13000, 13760, 14530, 15310, 16100, 16900,
        17710, 18530, 19360, 20200, 21050, 21910, 22780, 23660, 24550, 25450,
    };

    // 1², 1.5², 2², 2.5², 3², 3.5² - the doc's own stated extensible sequence beyond 2-star.
    private static readonly float[] DefaultStarMultipliers = { 1f, 2.25f, 4f, 6.25f, 9f, 12.25f };

    /// <summary>
    /// Binds every creature/level/star-multiplier value as its own synced ConfigEntry, in a
    /// dedicated config file (kept separate from the main plugin config so that one doesn't balloon
    /// to 150+ lines), and registers it with Jotunn so server values override local ones for
    /// non-admins. Call once from ClassObeliskMod.Awake(), after Jotunn's managers are available.
    /// </summary>
    public static void Initialize()
    {
        try
        {
            string path = Path.Combine(BepInEx.Paths.ConfigPath, "com.bunzboi.valheimweaponclass.XP.cfg");
            _xpConfig = new ConfigFile(path, true);

            BindCreatures();
            BindLevelCurve();
            BindStarMultipliers();

            SynchronizationManager.Instance.RegisterCustomConfig(_xpConfig);
            Logger.LogInfo($"XP config initialized: {_creatureXP.Count} creature prefab mappings, {_levelCumulativeXP.Count} level thresholds, {_starMultipliers.Count} star multipliers.");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error in XPConfigManager.Initialize: {ex.Message}");
        }
    }

    private static ConfigEntry<float> BindAdminFloat(string section, string key, float defaultValue, string description)
    {
        var attrs = new ConfigurationManagerAttributes { IsAdminOnly = true };
        var desc = new ConfigDescription(description, null, attrs);
        return _xpConfig.Bind(section, key, defaultValue, desc);
    }

    private static void BindCreatures()
    {
        foreach (var seed in CreatureSeeds)
        {
            string primaryKey = seed.Prefabs[0];
            var entry = BindAdminFloat($"Creature XP - {seed.Category}", primaryKey,
                seed.BaseXP, $"{seed.DisplayName} - base XP for a 0-star kill. Prefab aliases sharing this value: {string.Join(", ", seed.Prefabs)}.");

            foreach (var prefab in seed.Prefabs)
            {
                _creatureXP[prefab] = entry;
            }
        }
    }

    private static void BindLevelCurve()
    {
        for (int level = 2; level <= MaxLevel; level++)
        {
            float defaultValue = DefaultCumulativeXP[level - 1]; // array is 0-indexed from level 1
            _levelCumulativeXP[level] = BindAdminFloat("Level Curve", $"Level_{level:D2}_CumulativeXP",
                defaultValue, $"Total cumulative XP required to reach level {level}.");
        }
    }

    private static void BindStarMultipliers()
    {
        for (int star = 1; star <= MaxStarTier; star++)
        {
            _starMultipliers[star] = BindAdminFloat("Star Multipliers", $"{star}_Star",
                DefaultStarMultipliers[star], $"XP multiplier for a {star}-star kill.");
        }
    }

    /// <summary>
    /// Base (0-star, pre-multiplier) XP for a creature prefab. `found` is false when the prefab
    /// isn't in the table at all (not in the design doc, or a vanilla variant not yet mapped) -
    /// callers should fall back to the pre-existing health-based formula in that case rather than
    /// award 0, and log it so gaps in the table surface during real play instead of staying silent.
    /// </summary>
    public static float GetBaseXPForCreature(string prefabName, out bool found)
    {
        if (_xpConfig != null && _creatureXP.TryGetValue(prefabName, out var entry))
        {
            found = true;
            return entry.Value;
        }
        found = false;
        return 0f;
    }

    public static float GetStarMultiplier(int starCount)
    {
        starCount = UnityEngine.Mathf.Clamp(starCount, 0, MaxStarTier);
        if (starCount == 0) return 1f;
        return _starMultipliers.TryGetValue(starCount, out var entry) ? entry.Value : DefaultStarMultipliers[UnityEngine.Mathf.Min(starCount, DefaultStarMultipliers.Length - 1)];
    }

    /// <summary>Cumulative XP required to reach a level. Level &lt;= 1 is always 0.</summary>
    public static float GetTotalXPForLevel(int level)
    {
        if (level <= 1) return 0f;
        if (level >= MaxLevel) return _levelCumulativeXP.TryGetValue(MaxLevel, out var maxEntry) ? maxEntry.Value : DefaultCumulativeXP[MaxLevel - 1];
        return _levelCumulativeXP.TryGetValue(level, out var entry) ? entry.Value : DefaultCumulativeXP[level - 1];
    }

    public static int GetMaxLevel() => MaxLevel;
}
