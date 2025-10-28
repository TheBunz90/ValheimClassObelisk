using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using Logger = Jotunn.Logger;

// Player class data storage with persistence
[Serializable]
public class PlayerClassData
{
    // Internal storage still uses string for backwards compatibility with save data
    public Dictionary<string, int> classLevels = new Dictionary<string, int>();
    public Dictionary<string, float> classXP = new Dictionary<string, float>();
    public List<string> activeClasses = new List<string>();
    public Dictionary<string, bool> classUnlocked = new Dictionary<string, bool>();

    // For JSON serialization - these properties handle the dictionary serialization
    [SerializeField] private List<string> _classNames = new List<string>();
    [SerializeField] private List<int> _classLevels = new List<int>();
    [SerializeField] private List<float> _classXPValues = new List<float>();
    [SerializeField] private List<bool> _classUnlockedValues = new List<bool>();

    public PlayerClassData()
    {
        InitializeAllClasses();
    }

    private void InitializeAllClasses()
    {
        // Initialize all classes using enum
        foreach (var playerClass in PlayerClassHelper.GetAllClasses())
        {
            string className = PlayerClassHelper.GetInternalName(playerClass);
            if (!classLevels.ContainsKey(className))
                classLevels[className] = 0;
            if (!classXP.ContainsKey(className))
                classXP[className] = 0f;
            if (!classUnlocked.ContainsKey(className))
                classUnlocked[className] = true; // All unlocked for testing
        }
    }

    // Called before JSON serialization
    public void PrepareForSerialization()
    {
        _classNames.Clear();
        _classLevels.Clear();
        _classXPValues.Clear();
        _classUnlockedValues.Clear();

        foreach (var kvp in classLevels)
        {
            _classNames.Add(kvp.Key);
            _classLevels.Add(kvp.Value);
            _classXPValues.Add(classXP.ContainsKey(kvp.Key) ? classXP[kvp.Key] : 0f);
            _classUnlockedValues.Add(classUnlocked.ContainsKey(kvp.Key) ? classUnlocked[kvp.Key] : true);
        }
    }

    // Called after JSON deserialization
    public void RestoreFromSerialization()
    {
        classLevels.Clear();
        classXP.Clear();
        classUnlocked.Clear();

        for (int i = 0; i < _classNames.Count; i++)
        {
            if (i < _classLevels.Count) classLevels[_classNames[i]] = _classLevels[i];
            if (i < _classXPValues.Count) classXP[_classNames[i]] = _classXPValues[i];
            if (i < _classUnlockedValues.Count) classUnlocked[_classNames[i]] = _classUnlockedValues[i];
        }

        // Ensure all classes are initialized
        InitializeAllClasses();
    }

    public bool CanSelectSecondClass()
    {
        return classLevels.Values.Any(level => level >= 50);
    }

    public int GetMaxActiveClasses()
    {
        return CanSelectSecondClass() ? 2 : 1;
    }

    public bool IsClassActive(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        return activeClasses.Contains(className);
    }

    // Overload for backwards compatibility
    public bool IsClassActive(string className)
    {
        return activeClasses.Contains(className);
    }

    public bool CanActivateClass(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (!classUnlocked.ContainsKey(className) || !classUnlocked[className]) return false;
        if (IsClassActive(playerClass)) return false;
        return activeClasses.Count < GetMaxActiveClasses();
    }

    public void SetActiveClass(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (!classUnlocked.ContainsKey(className) || !classUnlocked[className])
        {
            Debug.LogWarning($"Cannot set active class {className}: class not unlocked");
            return;
        }

        // Clear all active classes and set the new one
        var previousClasses = new List<string>(activeClasses);
        activeClasses.Clear();
        activeClasses.Add(className);

        Debug.Log($"Set active class: {className} (was: {string.Join(", ", previousClasses)})");
    }

    public void AddActiveClass(PlayerClass playerClass)
    {
        if (!CanActivateClass(playerClass))
        {
            Debug.LogWarning($"Cannot activate class {playerClass}");
            return;
        }

        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (!activeClasses.Contains(className))
        {
            activeClasses.Add(className);
            Debug.Log($"Added active class: {className}. Active classes: {string.Join(", ", activeClasses)}");
        }
    }

    public void RemoveActiveClass(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (activeClasses.Remove(className))
        {
            Debug.Log($"Removed active class: {className}. Active classes: {string.Join(", ", activeClasses)}");
        }
    }

    public int GetClassLevel(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        return classLevels.ContainsKey(className) ? classLevels[className] : 0;
    }

    // Overload for backwards compatibility
    public int GetClassLevel(string className)
    {
        return classLevels.ContainsKey(className) ? classLevels[className] : 0;
    }

    public float GetClassXP(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        return classXP.ContainsKey(className) ? classXP[className] : 0f;
    }

    // Overload for backwards compatibility
    public float GetClassXP(string className)
    {
        return classXP.ContainsKey(className) ? classXP[className] : 0f;
    }

    public void AddClassXP(PlayerClass playerClass, float xpAmount)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (!classXP.ContainsKey(className))
        {
            classXP[className] = 0f;
        }

        classXP[className] += xpAmount;
        CheckForLevelUp(className);
    }

    // Overload for backwards compatibility
    public void AddClassXP(string className, float xpAmount)
    {
        if (!classXP.ContainsKey(className))
        {
            classXP[className] = 0f;
        }

        classXP[className] += xpAmount;
        CheckForLevelUp(className);
    }

    private void CheckForLevelUp(string className)
    {
        int currentLevel = GetClassLevel(className);
        if (currentLevel >= 50) return; // Max level

        float currentXP = GetClassXP(className);
        float totalXPForNextLevel = XPCurveHelper.GetTotalXPForLevel(currentLevel + 1);

        if (currentXP >= totalXPForNextLevel)
        {
            classLevels[className] = currentLevel + 1;
            Debug.Log($"Class {className} leveled up to {currentLevel + 1}!");

            // Check if it's a perk level (10, 20, 30, 40, 50)
            if ((currentLevel + 1) % 10 == 0)
            {
                Debug.Log($"New perk unlocked for {className} at level {currentLevel + 1}!");
            }
        }
    }

    // Get active classes as PlayerClass enums
    public List<PlayerClass> GetActiveClassEnums()
    {
        return PlayerClassHelper.FromInternalNames(activeClasses);
    }
}

// Enhanced class data manager with persistent storage via Harmony patches
public static class PlayerClassManager
{
    private static Dictionary<long, PlayerClassData> playerData = new Dictionary<long, PlayerClassData>();

    public static PlayerClassData GetPlayerData(Player player)
    {
        if (player == null)
        {
            Debug.LogError("GetPlayerData called with null player");
            return null;
        }

        long playerId = player.GetPlayerID();

        if (!playerData.ContainsKey(playerId))
        {
            // Create new data - will be loaded by the Load patch if save exists
            playerData[playerId] = new PlayerClassData();
            Debug.Log($"Created new class data for player {player.GetPlayerName()} (ID: {playerId})");
        }

        return playerData[playerId];
    }

    // Called by the Load patch to set loaded data
    public static void SetPlayerDataDirectly(long playerId, PlayerClassData data)
    {
        playerData[playerId] = data;
        Debug.Log($"[MANAGER] Directly set class data for player ID: {playerId}");
    }

    // Enhanced class selection method with enum support
    public static bool SetPlayerActiveClass(Player player, PlayerClass playerClass)
    {
        var data = GetPlayerData(player);
        if (data == null)
        {
            Debug.LogError($"Could not get player data for {player?.GetPlayerName() ?? "null"}");
            return false;
        }

        // Get previous state for debugging
        var previousClasses = string.Join(", ", data.activeClasses);

        // Set the class
        data.SetActiveClass(playerClass);

        // Verify the change was applied
        var newClasses = string.Join(", ", data.activeClasses);

        Debug.Log($"Class change for {player.GetPlayerName()}: '{previousClasses}' -> '{newClasses}'");

        return data.IsClassActive(playerClass);
    }

    // Overload for backwards compatibility with string
    public static bool SetPlayerActiveClass(Player player, string className)
    {
        var playerClass = PlayerClassHelper.ParseFromInternalName(className);
        if (!playerClass.HasValue)
        {
            Debug.LogError($"Invalid class name: {className}");
            return false;
        }

        return SetPlayerActiveClass(player, playerClass.Value);
    }

    // Get all class names (for backwards compatibility)
    public static string[] GetAllClassNames()
    {
        return PlayerClassHelper.GetAllDisplayNames();
    }

    // Get weapon type for a class using enum
    public static string GetWeaponTypeForClass(PlayerClass playerClass)
    {
        return PlayerClassHelper.GetWeaponType(playerClass);
    }

    // Overload for backwards compatibility
    public static string GetWeaponTypeForClass(string className)
    {
        var playerClass = PlayerClassHelper.ParseFromInternalName(className);
        if (!playerClass.HasValue) return "Unknown";
        return PlayerClassHelper.GetWeaponType(playerClass.Value);
    }

    // Check if player has any active classes
    public static bool HasActiveClasses(Player player)
    {
        var data = GetPlayerData(player);
        return data != null && data.activeClasses.Count > 0;
    }

    // Get active class names as a formatted string
    public static string GetActiveClassesString(Player player)
    {
        var data = GetPlayerData(player);
        if (data == null || data.activeClasses.Count == 0)
            return "None";

        return string.Join(", ", data.activeClasses);
    }

    // Debug method to clear all data (for testing)
    public static void ClearAllPlayerData()
    {
        playerData.Clear();
        Debug.Log("Cleared all player class data from memory");
    }

    // Debug method to force reload a player's data
    public static void ReloadPlayerData(Player player)
    {
        if (player == null) return;

        long playerId = player.GetPlayerID();
        if (playerData.ContainsKey(playerId))
        {
            playerData.Remove(playerId);
        }

        // This will create fresh data - will be populated by Load patch on next load
        GetPlayerData(player);
        Debug.Log($"Reloaded data for player {player.GetPlayerName()}");
    }

    // Resets the specified ACTIVE class to level 0 and 0 XP, keeping it active.
    // Returns true if reset succeeded; false otherwise.
    public static bool ResetActiveClassProgress(Player player, string activeClassInternalName)
    {
        // Defensive checks
        var data = GetPlayerData(player);
        if (player == null || data == null || string.IsNullOrWhiteSpace(activeClassInternalName))
            return false;

        // Only allow resetting if this class is currently active
        if (!data.activeClasses.Contains(activeClassInternalName))
            return false;

        // Ensure dictionaries contain the class key
        if (!data.classLevels.ContainsKey(activeClassInternalName))
            data.classLevels[activeClassInternalName] = 0;
        if (!data.classXP.ContainsKey(activeClassInternalName))
            data.classXP[activeClassInternalName] = 0f;

        // Perform the reset
        data.classLevels[activeClassInternalName] = 0;
        data.classXP[activeClassInternalName] = 0f;

        // Keep it active (no changes needed), but ensure it's present
        if (!data.activeClasses.Contains(activeClassInternalName))
            data.activeClasses.Add(activeClassInternalName);

        // NOTE: Persistence: your Save/Load patches will persist this on next save.
        return true;
    }

}

// Write AFTER Valheim has written its data
[HarmonyPatch(typeof(Player), nameof(Player.Save))]
public static class Player_Save_Patch
{
    private static void Postfix(Player __instance, ZPackage pkg)
    {
        try
        {
            Debug.Log($"[PATCH:SAVE] Saving class data for {__instance.GetPlayerName()} (ID: {__instance.GetPlayerID()})");

            var playerData = PlayerClassManager.GetPlayerData(__instance);
            if (playerData != null)
            {
                playerData.PrepareForSerialization();
                string jsonData = JsonUtility.ToJson(playerData, false);

                // Write to the package AFTER all vanilla data
                pkg.Write(jsonData);

                Debug.Log($"[PATCH:SAVE] ✓ Wrote {jsonData.Length} bytes. Active classes: {string.Join(", ", playerData.activeClasses)}");
            }
            else
            {
                pkg.Write("");
                Debug.LogWarning($"[PATCH:SAVE] No class data to save for {__instance.GetPlayerName()}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PATCH:SAVE] Error saving class data: {ex}");
            // Don't write anything on error - the save is already complete
        }
    }
}

// Read AFTER Valheim has read its data
[HarmonyPatch(typeof(Player), nameof(Player.Load))]
public static class Player_Load_Patch
{
    private static void Postfix(Player __instance, ZPackage pkg)
    {
        try
        {
            Debug.Log($"[PATCH:LOAD] Loading class data for {__instance.GetPlayerName()} (ID: {__instance.GetPlayerID()})");

            // Check if there's more data to read
            if (pkg.GetPos() >= pkg.Size())
            {
                Debug.Log($"[PATCH:LOAD] No class data in save (pre-mod character or new character)");
                return;
            }

            // Read the JSON data from the save package
            string jsonData = pkg.ReadString();

            if (!string.IsNullOrEmpty(jsonData))
            {
                Debug.Log($"[PATCH:LOAD] Found saved data: {jsonData.Length} bytes");

                var playerData = JsonUtility.FromJson<PlayerClassData>(jsonData);
                playerData.RestoreFromSerialization();

                // Store in the manager's dictionary
                long playerId = __instance.GetPlayerID();
                PlayerClassManager.SetPlayerDataDirectly(playerId, playerData);

                Debug.Log($"[PATCH:LOAD] ✓ Loaded successfully. Active classes: {string.Join(", ", playerData.activeClasses)}");
            }
            else
            {
                Debug.Log($"[PATCH:LOAD] Empty class data string");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PATCH:LOAD] Error loading class data: {ex}");
        }
    }
}

[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class PCMTestCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        
        new Terminal.ConsoleCommand("resetclass", "reset the active class to level 0",
            delegate (Terminal.ConsoleEventArgs args)
            {
                Player player = Player.m_localPlayer;
                if (player != null)
                {
                    string activeClass = PlayerClassManager.GetActiveClassesString(player);
                    PlayerClassManager.ResetActiveClassProgress(player, activeClass);
                }
            }
        );
    }
}
