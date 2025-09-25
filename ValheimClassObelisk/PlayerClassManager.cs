using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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

        // Force save immediately after class change
        PlayerClassManager.SavePlayerData();
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

            // Force save immediately after class change
            PlayerClassManager.SavePlayerData();
        }
    }

    public void RemoveActiveClass(PlayerClass playerClass)
    {
        string className = PlayerClassHelper.GetInternalName(playerClass);
        if (activeClasses.Remove(className))
        {
            Debug.Log($"Removed active class: {className}. Active classes: {string.Join(", ", activeClasses)}");

            // Force save immediately after class change
            PlayerClassManager.SavePlayerData();
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

            // Save progress immediately on level up
            PlayerClassManager.SavePlayerData();
        }
    }

    // Get active classes as PlayerClass enums
    public List<PlayerClass> GetActiveClassEnums()
    {
        return PlayerClassHelper.FromInternalNames(activeClasses);
    }
}

// Enhanced class data manager with persistent storage
public static class PlayerClassManager
{
    private static Dictionary<long, PlayerClassData> playerData = new Dictionary<long, PlayerClassData>();
    private const string SAVE_KEY_PREFIX = "ClassObelisk_PlayerData_";

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
            // Try to load from persistent storage first
            var loadedData = LoadPlayerDataFromStorage(playerId);
            if (loadedData != null)
            {
                playerData[playerId] = loadedData;
                Debug.Log($"Loaded existing class data for player {player.GetPlayerName()} (ID: {playerId})");
            }
            else
            {
                // Create new data if none exists
                playerData[playerId] = new PlayerClassData();
                Debug.Log($"Created new class data for player {player.GetPlayerName()} (ID: {playerId})");
                SavePlayerDataToStorage(playerId, playerData[playerId]);
            }
        }

        return playerData[playerId];
    }

    public static void SavePlayerData()
    {
        foreach (var kvp in playerData)
        {
            SavePlayerDataToStorage(kvp.Key, kvp.Value);
        }
        Debug.Log($"Saved class data for {playerData.Count} players");
    }

    private static void SavePlayerDataToStorage(long playerId, PlayerClassData data)
    {
        try
        {
            // Prepare data for serialization
            data.PrepareForSerialization();

            // Convert to JSON
            string jsonData = JsonUtility.ToJson(data, true);

            // Save to player's custom data
            string saveKey = SAVE_KEY_PREFIX + playerId;

            // Use Valheim's custom data system
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
            {
                Player.m_localPlayer.m_customData[saveKey] = jsonData;
                Debug.Log($"Saved class data to local player custom data for ID: {playerId}");
            }
            else
            {
                // For other players, save to global custom data or file system
                SaveToGlobalCustomData(saveKey, jsonData);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving player data for ID {playerId}: {ex.Message}");
        }
    }

    private static PlayerClassData LoadPlayerDataFromStorage(long playerId)
    {
        try
        {
            string saveKey = SAVE_KEY_PREFIX + playerId;
            string jsonData = null;

            // Try to load from local player first
            if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == playerId)
            {
                if (Player.m_localPlayer.m_customData.TryGetValue(saveKey, out jsonData))
                {
                    Debug.Log($"Found class data in local player custom data for ID: {playerId}");
                }
            }

            // If not found locally, try global custom data
            if (string.IsNullOrEmpty(jsonData))
            {
                jsonData = LoadFromGlobalCustomData(saveKey);
            }

            if (!string.IsNullOrEmpty(jsonData))
            {
                var data = JsonUtility.FromJson<PlayerClassData>(jsonData);
                data.RestoreFromSerialization();
                Debug.Log($"Successfully loaded class data for player ID: {playerId}");
                return data;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading player data for ID {playerId}: {ex.Message}");
        }

        return null;
    }

    private static void SaveToGlobalCustomData(string key, string data)
    {
        // Save to world's custom data or a global file
        try
        {
            if (ZNet.instance?.GetWorldUID() != 0)
            {
                // Use world-specific storage
                var worldId = ZNet.instance.GetWorldUID();
                var worldKey = $"World_{worldId}_{key}";

                // For now, we'll use a simple file-based approach
                var savePath = System.IO.Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), $"{worldKey}.json");
                System.IO.File.WriteAllText(savePath, data);
                Debug.Log($"Saved to world file: {savePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error saving to global storage: {ex.Message}");
        }
    }

    private static string LoadFromGlobalCustomData(string key)
    {
        try
        {
            if (ZNet.instance?.GetWorldUID() != 0)
            {
                var worldId = ZNet.instance.GetWorldUID();
                var worldKey = $"World_{worldId}_{key}";
                var savePath = System.IO.Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), $"{worldKey}.json");

                if (System.IO.File.Exists(savePath))
                {
                    var data = System.IO.File.ReadAllText(savePath);
                    Debug.Log($"Loaded from world file: {savePath}");
                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading from global storage: {ex.Message}");
        }

        return null;
    }

    public static void LoadPlayerData()
    {
        Debug.Log("PlayerClassManager: Loading all player class data");
        // Data is loaded on-demand when GetPlayerData is called
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

        // This will force a reload from storage
        GetPlayerData(player);
        Debug.Log($"Reloaded data for player {player.GetPlayerName()}");
    }
}