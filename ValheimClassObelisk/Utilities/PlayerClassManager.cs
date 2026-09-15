using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using HarmonyLib;
using ValheimClassObelisk;
using Logger = Jotunn.Logger;

// Outcome of PlayerClassManager.ActivateClass, so callers (the Obelisk UI) can show
// the right feedback without re-deriving why the activation did or didn't apply.
public enum ClassActivationResult
{
    Activated,
    Swapped,
    AtLimit,
    NotUnlocked,
    Error
}

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

    // Sets the class to whatever level its total XP actually corresponds to, not just
    // currentLevel+1 - a single kill (especially with a boosted kill-bonus multiplier, or a
    // large XP award after being under-leveled for a while) can carry enough XP to cross
    // several level thresholds at once.
    private void CheckForLevelUp(string className)
    {
        int currentLevel = GetClassLevel(className);
        if (currentLevel >= 50) return; // Max level

        float currentXP = GetClassXP(className);
        int correctLevel = XPCurveHelper.GetLevelFromXP(currentXP);

        if (correctLevel > currentLevel)
        {
            classLevels[className] = correctLevel;
            Debug.Log($"Class {className} leveled up to {correctLevel}! (was {currentLevel})");
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

    // Called when a "CO_SetActiveClasses" RPC is received - overwrites just the active
    // classes for a (possibly not-yet-known) player, without touching their XP/levels.
    public static void SetActiveClassesDirectly(long playerId, List<string> activeClasses)
    {
        if (!playerData.ContainsKey(playerId))
        {
            playerData[playerId] = new PlayerClassData();
        }

        playerData[playerId].activeClasses = activeClasses;
        Debug.Log($"[MANAGER] Synced active classes for player ID {playerId}: {string.Join(", ", activeClasses)}");
    }

    // Exposes the full known roster so the server can resync newly-connected peers.
    public static IEnumerable<KeyValuePair<long, PlayerClassData>> GetAllPlayerData()
    {
        return playerData;
    }

    // Re-broadcasts every known player's current active classes to everyone. Called
    // server-side whenever a player connects, so a client that joins after someone else
    // already selected a class (possibly while alone on the server) catches up immediately
    // instead of waiting for that other player to change class again.
    //
    // NOTE: this only has something useful to relay because BroadcastMyActiveClasses (below)
    // is called every time a player's OWN client loads their OWN save data - that's the only
    // way the server's copy of `playerData` ever learns a player's pre-existing selection in
    // the first place, since Player.Load() never runs server-side (see BroadcastMyActiveClasses
    // for the full explanation). The two work together: on connect, a player announces their
    // own already-saved selection to everyone (server included), and separately, the server
    // replays everything it has accumulated back out to that same newly-connected player (who
    // otherwise has no way to know what anyone else already had selected before they joined).
    public static void BroadcastFullRoster()
    {
        if (ZRoutedRpc.instance == null) return;

        foreach (var entry in playerData)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "CO_SetActiveClasses", entry.Key, string.Join(",", entry.Value.activeClasses));
        }
    }

    // Broadcasts one player's own current active classes to every connected peer. Used both
    // by ActivateClass/DeactivateClass (a live reselection) and by Player_Load_Patch (below) for the
    // case that turned out to be the actual gap: a player's own client is the ONLY place that
    // correctly loads their pre-existing saved selection (via Player.Load, which never runs on
    // the dedicated server), but nothing was ever telling anyone else about it - the old code
    // only broadcast on an explicit reselection, so a player who didn't touch an Obelisk this
    // session was invisible to everyone, including the server, no matter how long they'd
    // already had a class selected in earlier sessions.
    public static void BroadcastMyActiveClasses(long playerId, List<string> activeClasses)
    {
        ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, "CO_SetActiveClasses", playerId, string.Join(",", activeClasses));
    }

    // Broadcasts a kill-XP award for a specific player/class to everyone. Only the receiving
    // peer whose OWN local player matches `targetPlayerId` actually applies it (see
    // RPC_ReceiveClassXPAward) - everyone else, including the sender, just ignores it. This is
    // what makes XP application always happen on the correct player's own authoritative copy
    // of their data, regardless of which peer actually owned the creature that died and ran
    // the eligibility/award calculation.
    public static void BroadcastClassXPAward(long targetPlayerId, string className, float xpAmount)
    {
        ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, "CO_AwardClassXP", targetPlayerId, className, xpAmount);
    }

    // Activates a class for the player. If they still have a free slot (GetMaxActiveClasses(),
    // 1 normally, 2 once any class hits level 50), it's simply added. If they're full but only
    // have 1 max slot (haven't unlocked dual classes), the currently active class is swapped out
    // for this one. If they're full WITH 2 max slots, nothing changes - the UI should keep the
    // activation control disabled in that case rather than relying on this to reject it.
    public static ClassActivationResult ActivateClass(Player player, string className)
    {
        var data = GetPlayerData(player);
        if (data == null)
        {
            Debug.LogError($"Could not get player data for {player?.GetPlayerName() ?? "null"}");
            return ClassActivationResult.Error;
        }

        var playerClass = PlayerClassHelper.ParseFromInternalName(className);
        if (!playerClass.HasValue)
        {
            Debug.LogError($"Invalid class name: {className}");
            return ClassActivationResult.Error;
        }

        if (data.IsClassActive(playerClass.Value)) return ClassActivationResult.Activated; // already active, nothing to do

        int maxSlots = data.GetMaxActiveClasses();
        ClassActivationResult result;

        if (data.activeClasses.Count < maxSlots)
        {
            if (!data.CanActivateClass(playerClass.Value)) return ClassActivationResult.NotUnlocked;
            data.AddActiveClass(playerClass.Value);
            result = ClassActivationResult.Activated;
        }
        else if (maxSlots == 1)
        {
            // Single-slot players swap directly: drop whatever's active, activate the new pick.
            foreach (string activeClassName in new List<string>(data.activeClasses))
            {
                var activePlayerClass = PlayerClassHelper.ParseFromInternalName(activeClassName);
                if (activePlayerClass.HasValue) data.RemoveActiveClass(activePlayerClass.Value);
            }
            data.AddActiveClass(playerClass.Value);
            result = ClassActivationResult.Swapped;
        }
        else
        {
            // No state change, so nothing to broadcast - the UI should already have this control
            // disabled in this state, so reaching here means it let a stale click through.
            return ClassActivationResult.AtLimit;
        }

        Debug.Log($"Activated class for {player.GetPlayerName()}: {className} -> {result} (active: {string.Join(", ", data.activeClasses)})");

        // Let every other connected peer (including the server, and whichever peer ends up
        // owning a given monster's ZDO) know right away, instead of waiting for the next
        // Player.Save/Load cycle to carry it over.
        BroadcastMyActiveClasses(player.GetPlayerID(), data.activeClasses);

        return result;
    }

    // Deactivates a class the player currently has active. No-op if it wasn't active.
    public static void DeactivateClass(Player player, string className)
    {
        var data = GetPlayerData(player);
        if (data == null)
        {
            Debug.LogError($"Could not get player data for {player?.GetPlayerName() ?? "null"}");
            return;
        }

        var playerClass = PlayerClassHelper.ParseFromInternalName(className);
        if (!playerClass.HasValue || !data.IsClassActive(playerClass.Value)) return;

        data.RemoveActiveClass(playerClass.Value);
        Debug.Log($"Deactivated class for {player.GetPlayerName()}: {className} (active: {string.Join(", ", data.activeClasses)})");

        BroadcastMyActiveClasses(player.GetPlayerID(), data.activeClasses);
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

    // Maps each class to its GetClassDescription(Player) provider - the same perk text
    // (with locked perks masked as "???") already used by the class-selection GUI.
    private static readonly Dictionary<PlayerClass, Func<Player, string>> ClassDescriptionProviders = new Dictionary<PlayerClass, Func<Player, string>>
    {
        { PlayerClass.SwordMaster, SwordMasterPerkManager.GetClassDescription },
        { PlayerClass.Archer, ArcherPerkManager.GetClassDescription },
        { PlayerClass.Crusher, CrusherPerkManager.GetClassDescription },
        { PlayerClass.Assassin, AssassinPerkManager.GetClassDescription },
        { PlayerClass.Brawler, BrawlerPerkManager.GetClassDescription },
        { PlayerClass.Wizard, WizardPerkManager.GetClassDescription },
        { PlayerClass.Lancer, LancerPerkManager.GetClassDescription },
        { PlayerClass.Bulwark, BulwarkPerkManager.GetClassDescription },
        { PlayerClass.Axemaster, AxemasterPerkManager.GetClassDescription }
    };

    // Builds the rich-text body shown in the "Active Classes" entry of the Valheim Compendium
    // (see ActiveClassesTextsDialogPatch). Reuses each class's existing perk description text.
    public static string BuildActiveClassesCompendiumText(Player player)
    {
        var data = GetPlayerData(player);
        if (data == null || data.activeClasses.Count == 0)
        {
            return "No active class selected. Visit a Class Obelisk to choose one.";
        }

        var sb = new StringBuilder();

        if (data.activeClasses.Count < data.GetMaxActiveClasses())
        {
            sb.Append("<color=#FFD700><b>★ A second class slot is available — visit a Class Obelisk to activate one! ★</b></color>\n\n");
        }

        foreach (var playerClass in data.GetActiveClassEnums())
        {
            int level = data.GetClassLevel(playerClass);
            sb.Append($"<color=yellow>{PlayerClassHelper.GetDisplayName(playerClass)} — Level {level}</color>\n");

            if (level >= 50)
            {
                sb.Append("<color=#00FFFF>Max Level Reached</color>\n\n");
            }
            else
            {
                float totalXP = data.GetClassXP(playerClass);
                var (current, required) = XPCurveHelper.GetXPProgress(totalXP, level);
                float percent = required > 0 ? current / required * 100f : 0f;
                sb.Append($"<color=#00FFFF>XP: {current:N0} / {required:N0} ({percent:F0}%) to next level</color>\n\n");
            }

            sb.Append(ClassDescriptionProviders[playerClass](player));
            sb.Append("\n\n");
        }

        return sb.ToString().TrimEnd();
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
            // The main-menu character-preview model (FejdStartup.SetupCharacterPreview) is
            // instantiated with ZNetView.m_forceDisableInit = true, so it never gets a real ZDO
            // and Player.GetPlayerID() always reports 0 for it - yet both browsing the character
            // list and finishing "New Character" route through this same Player.Save patch.
            // Treating that shared "0" bucket as a real player let one character's class data
            // leak into whatever character got created or saved next in the same session
            // (surviving even deleting the source character in between, since the leak happens
            // in memory at menu time, not through the deleted file). Skip it - only the real
            // spawned player (valid ZDO, real per-character ID) should ever persist class data.
            if (__instance.GetPlayerID() == 0) return;

            DevLog.Log($"[PATCH:SAVE] Saving class data for {__instance.GetPlayerName()} (ID: {__instance.GetPlayerID()})");

            var playerData = PlayerClassManager.GetPlayerData(__instance);
            if (playerData != null)
            {
                playerData.PrepareForSerialization();
                string jsonData = JsonUtility.ToJson(playerData, false);

                // Write to the package AFTER all vanilla data
                pkg.Write(jsonData);

                DevLog.Log($"[PATCH:SAVE] ✓ Wrote {jsonData.Length} bytes. Active classes: {string.Join(", ", playerData.activeClasses)}");
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
            // Same disabled-ZNetView menu character-preview object as Player_Save_Patch above -
            // skip it so we never cache a real character's class data under the shared "0"
            // bucket, where a later Save from that same object could leak it into an unrelated
            // character's save file.
            if (__instance.GetPlayerID() == 0) return;

            DevLog.Log($"[PATCH:LOAD] Loading class data for {__instance.GetPlayerName()} (ID: {__instance.GetPlayerID()})");

            // Check if there's more data to read
            if (pkg.GetPos() >= pkg.Size())
            {
                DevLog.Log($"[PATCH:LOAD] No class data in save (pre-mod character or new character)");
            }
            else
            {
                // Read the JSON data from the save package
                string jsonData = pkg.ReadString();

                if (!string.IsNullOrEmpty(jsonData))
                {
                    DevLog.Log($"[PATCH:LOAD] Found saved data: {jsonData.Length} bytes");

                    var playerData = JsonUtility.FromJson<PlayerClassData>(jsonData);
                    playerData.RestoreFromSerialization();

                    // Store in the manager's dictionary
                    long playerId = __instance.GetPlayerID();
                    PlayerClassManager.SetPlayerDataDirectly(playerId, playerData);

                    // This is the actual fix for "shouldn't need to reselect": Player.Load()
                    // only ever runs for this client's OWN character (verified via decompile -
                    // it's called only from Game.cs's local spawn flow against that client's
                    // own PlayerProfile, never server-side, never for a remote player). That
                    // means this is the ONE place a player's pre-existing, already-saved class
                    // selection becomes known at all this session - nothing else announces it
                    // unless the player actively reselects. Broadcast it now so everyone
                    // currently connected (server included) learns it immediately on connect,
                    // instead of only on the next explicit reselection.
                    PlayerClassManager.BroadcastMyActiveClasses(playerId, playerData.activeClasses);

                    DevLog.Log($"[PATCH:LOAD] ✓ Loaded successfully. Active classes: {string.Join(", ", playerData.activeClasses)}");
                }
                else
                {
                    DevLog.Log($"[PATCH:LOAD] Empty class data string");
                }
            }

        }
        catch (Exception ex)
        {
            Debug.LogError($"[PATCH:LOAD] Error loading class data: {ex}");
        }
    }
}

// Resyncs the whole roster whenever a player connects, so a client that joins after someone
// else already selected a class (possibly while alone on the server) catches up immediately
// instead of needing to reselect. This does NOT piggyback on Player.Load - verified via
// decompile that Player.Load()/Save() are only ever called from Game.cs's local spawn/logout
// flow and FejdStartup.cs's main-menu character management, both entirely client-side against
// that client's own PlayerProfile. The dedicated server never calls Player.Load() for anyone,
// including itself, so a "ZNet.instance.IsServer()" guard inside a Player_Load_Patch postfix
// can never actually fire - that was the previous (broken) approach here.
// ZNet.RPC_CharacterID is the real signal: each connecting client sends its own character's
// ZDOID to the server once known (see ZNet.SetCharacterID), and the server receives it via
// this RPC handler - exactly the "a player just joined and is ready" moment we need, and it
// only meaningfully fires server-side (a non-server peer never receives this RPC from itself).
[HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
public static class ClassRosterResyncOnConnect
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            DevLog.Log("[XPDBG] RPC_CharacterID fired on server - resyncing class roster to all peers");
            PlayerClassManager.BroadcastFullRoster();
        }
    }
}

// Syncs active-class selections across peers immediately, rather than relying solely on
// the Save/Load cycle above (which only reaches that one player's own client + the server).
// ZRoutedRpc is a plain class (not a MonoBehaviour) - it has no Awake/Start, it registers
// itself via its constructor (`s_instance = this` in `ZRoutedRpc(bool server)`), so that's
// what needs patching, not "Awake" (patching a nonexistent method throws and can abort the
// rest of this mod's Harmony.PatchAll() pass).
[HarmonyPatch(typeof(ZRoutedRpc), MethodType.Constructor, typeof(bool))]
public static class ClassSyncRpc
{
    [HarmonyPostfix]
    public static void Constructor_Postfix()
    {
        ZRoutedRpc.instance.Register<long, string>("CO_SetActiveClasses", RPC_ReceiveActiveClasses);
        ZRoutedRpc.instance.Register<long, string, float>("CO_AwardClassXP", RPC_ReceiveClassXPAward);
    }

    private static void RPC_ReceiveActiveClasses(long sender, long playerId, string activeClassesCsv)
    {
        var classes = string.IsNullOrEmpty(activeClassesCsv)
            ? new List<string>()
            : activeClassesCsv.Split(',').ToList();

        PlayerClassManager.SetActiveClassesDirectly(playerId, classes);
    }

    // Broadcast from ClassXPManager.AwardKillBonusXP (see XPSystemManager.cs). Every peer
    // receives this (including the sender and the dedicated server), but only the one whose
    // OWN local player matches `targetPlayerId` actually applies it - guaranteeing XP always
    // lands on that player's own correctly-loaded copy of their data, never a stale/empty
    // stand-in created on whichever peer happened to own the creature that died.
    private static void RPC_ReceiveClassXPAward(long sender, long targetPlayerId, string className, float xpAmount)
    {
        Player localPlayer = Player.m_localPlayer;
        if (localPlayer == null || localPlayer.GetPlayerID() != targetPlayerId) return;

        var playerData = PlayerClassManager.GetPlayerData(localPlayer);
        if (playerData == null) return;

        int oldLevel = playerData.GetClassLevel(className);
        bool couldSelectSecondClass = playerData.CanSelectSecondClass();
        playerData.AddClassXP(className, xpAmount);
        int newLevel = playerData.GetClassLevel(className);

        localPlayer.Message(MessageHud.MessageType.TopLeft, $"{className}: +{xpAmount:F0} XP");

        if (newLevel > oldLevel)
        {
            localPlayer.Message(MessageHud.MessageType.Center, $"{className} Level Up! Level {newLevel}");

            // Announce every perk tier crossed by this award, not just whether newLevel itself
            // is a multiple of 10 - a multi-level jump (e.g. 38 -> 42) can skip straight past a
            // perk tier (40) without landing on it.
            int firstPerkTier = ((oldLevel / 10) + 1) * 10;
            for (int perkLevel = firstPerkTier; perkLevel <= newLevel; perkLevel += 10)
            {
                localPlayer.Message(MessageHud.MessageType.Center, $"New {className} Perk Unlocked! (Level {perkLevel})");
            }

            // Fires exactly once per character - CanSelectSecondClass() only flips false->true
            // the first time ANY class reaches 50, so a later class also reaching 50 finds it
            // already true and this is skipped.
            if (!couldSelectSecondClass && playerData.CanSelectSecondClass())
            {
                localPlayer.Message(MessageHud.MessageType.Center, "★ Dual Class Unlocked! ★\nVisit a Class Obelisk to activate a 2nd class.");
            }
        }

        DevLog.Log($"[XPDBG] Applied {xpAmount:F1} XP to {className} for local player (level {oldLevel} -> {newLevel})");
    }
}

// Dev-only: excluded from Release builds.
#if DEBUG
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
#endif
