using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Enum defining all available player classes
/// </summary>
public enum PlayerClass
{
    SwordMaster,
    Archer,
    Crusher,
    Assassin,
    Pugilist,
    Mage,
    Lancer,
    Bulwark
}

/// <summary>
/// Helper class for PlayerClass enum operations
/// </summary>
public static class PlayerClassHelper
{
    // Display names for UI
    private static readonly Dictionary<PlayerClass, string> DisplayNames = new Dictionary<PlayerClass, string>
    {
        { PlayerClass.SwordMaster, "Sword Master" },
        { PlayerClass.Archer, "Archer" },
        { PlayerClass.Crusher, "Crusher" },
        { PlayerClass.Assassin, "Assassin" },
        { PlayerClass.Pugilist, "Pugilist" },
        { PlayerClass.Mage, "Mage" },
        { PlayerClass.Lancer, "Lancer" },
        { PlayerClass.Bulwark, "Bulwark" }
    };

    // Internal names for save data (legacy support)
    private static readonly Dictionary<PlayerClass, string> InternalNames = new Dictionary<PlayerClass, string>
    {
        { PlayerClass.SwordMaster, "Sword Master" },
        { PlayerClass.Archer, "Archer" },
        { PlayerClass.Crusher, "Crusher" },
        { PlayerClass.Assassin, "Assassin" },
        { PlayerClass.Pugilist, "Pugilist" },
        { PlayerClass.Mage, "Mage" },
        { PlayerClass.Lancer, "Lancer" },
        { PlayerClass.Bulwark, "Bulwark" }
    };

    // Weapon type descriptions for each class
    private static readonly Dictionary<PlayerClass, string> WeaponTypes = new Dictionary<PlayerClass, string>
    {
        { PlayerClass.SwordMaster, "Swords" },
        { PlayerClass.Archer, "Bows & Crossbows" },
        { PlayerClass.Crusher, "Maces & Hammers" },
        { PlayerClass.Assassin, "Knives" },
        { PlayerClass.Pugilist, "Unarmed" },
        { PlayerClass.Mage, "Staves" },
        { PlayerClass.Lancer, "Spears & Polearms" },
        { PlayerClass.Bulwark, "Shields" }
    };

    /// <summary>
    /// Get display name for a class (for UI)
    /// </summary>
    public static string GetDisplayName(PlayerClass playerClass)
    {
        return DisplayNames[playerClass];
    }

    /// <summary>
    /// Get internal name for a class (for save data)
    /// </summary>
    public static string GetInternalName(PlayerClass playerClass)
    {
        return InternalNames[playerClass];
    }

    /// <summary>
    /// Get weapon type for a class
    /// </summary>
    public static string GetWeaponType(PlayerClass playerClass)
    {
        return WeaponTypes[playerClass];
    }

    /// <summary>
    /// Parse class from display name (for backwards compatibility)
    /// </summary>
    public static PlayerClass? ParseFromDisplayName(string displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return null;

        foreach (var kvp in DisplayNames)
        {
            if (string.Equals(kvp.Value, displayName, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        // Also check enum names directly (for "SwordMaster" format)
        if (Enum.TryParse<PlayerClass>(displayName.Replace(" ", ""), true, out PlayerClass result))
        {
            return result;
        }

        return null;
    }

    /// <summary>
    /// Parse class from internal name (for save data)
    /// </summary>
    public static PlayerClass? ParseFromInternalName(string internalName)
    {
        if (string.IsNullOrEmpty(internalName)) return null;

        foreach (var kvp in InternalNames)
        {
            if (string.Equals(kvp.Value, internalName, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Key;
            }
        }

        return null;
    }

    /// <summary>
    /// Get all player classes as a list
    /// </summary>
    public static List<PlayerClass> GetAllClasses()
    {
        return Enum.GetValues(typeof(PlayerClass)).Cast<PlayerClass>().ToList();
    }

    /// <summary>
    /// Get all display names
    /// </summary>
    public static string[] GetAllDisplayNames()
    {
        return GetAllClasses().Select(c => GetDisplayName(c)).ToArray();
    }

    /// <summary>
    /// Check if a weapon is appropriate for a specific class
    /// </summary>
    public static bool IsWeaponAppropriateForClass(ItemDrop.ItemData weapon, PlayerClass playerClass)
    {
        switch (playerClass)
        {
            case PlayerClass.SwordMaster:
                return ClassCombatManager.IsSwordWeapon(weapon);
            case PlayerClass.Archer:
                return ClassCombatManager.IsBowWeapon(weapon);
            case PlayerClass.Crusher:
                return ClassCombatManager.IsBluntWeapon(weapon);
            case PlayerClass.Assassin:
                return ClassCombatManager.IsKnifeWeapon(weapon);
            case PlayerClass.Pugilist:
                return ClassCombatManager.IsUnarmedAttack(weapon);
            case PlayerClass.Mage:
                return ClassCombatManager.IsMagicWeapon(weapon);
            case PlayerClass.Lancer:
                return ClassCombatManager.IsSpearWeapon(weapon);
            case PlayerClass.Bulwark:
                return true; // Bulwark gains XP from any combat (defensive class)
            default:
                return false;
        }
    }

    /// <summary>
    /// Convert a list of class enums to internal names for saving
    /// </summary>
    public static List<string> ToInternalNames(List<PlayerClass> classes)
    {
        return classes.Select(c => GetInternalName(c)).ToList();
    }

    /// <summary>
    /// Convert a list of internal names to class enums
    /// </summary>
    public static List<PlayerClass> FromInternalNames(List<string> internalNames)
    {
        var classes = new List<PlayerClass>();
        foreach (var name in internalNames)
        {
            var playerClass = ParseFromInternalName(name);
            if (playerClass.HasValue)
            {
                classes.Add(playerClass.Value);
            }
        }
        return classes;
    }
}