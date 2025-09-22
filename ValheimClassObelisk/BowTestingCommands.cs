using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System.Reflection;
using System.Linq;

// Testing class for bow draw duration modification
public static class DrawDurationTestManager
{
    // Testing multipliers
    public static float DrawDurationMultiplier = 1.0f;

    // Track if testing is active
    private static bool isTestingActive = false;

    // Find and list all draw-related fields and methods
    public static void InspectDrawMechanics(ItemDrop.ItemData weapon)
    {
        if (weapon?.m_shared?.m_attack == null)
        {
            Debug.LogError("No weapon or attack component found!");
            return;
        }

        Debug.Log($"=== Inspecting Draw Mechanics: {weapon.m_shared.m_name} ===");

        var attackType = weapon.m_shared.m_attack.GetType();

        // Look for draw-related fields
        var fields = attackType.GetFields(BindingFlags.Public | BindingFlags.Instance);
        var drawFields = fields.Where(f =>
            f.Name.ToLower().Contains("draw") ||
            f.Name.ToLower().Contains("duration") ||
            f.Name.ToLower().Contains("time") ||
            f.Name.ToLower().Contains("speed")
        ).ToList();

        Debug.Log("Draw-related fields found in Attack component:");
        foreach (var field in drawFields)
        {
            var value = field.GetValue(weapon.m_shared.m_attack);
            Debug.Log($"  Attack.{field.Name} ({field.FieldType.Name}): {value}");
        }

        // Look for draw-related methods
        var methods = attackType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var drawMethods = methods.Where(m =>
            m.Name.ToLower().Contains("draw") ||
            m.Name.ToLower().Contains("duration") ||
            m.Name.ToLower().Contains("speed")
        ).ToList();

        Debug.Log("\nDraw-related methods found in Attack component:");
        foreach (var method in drawMethods)
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Debug.Log($"  {method.ReturnType.Name} {method.Name}({parameters})");
        }

        // Also check Player class for draw methods
        var playerType = typeof(Player);
        var playerMethods = playerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var playerDrawMethods = playerMethods.Where(m =>
            m.Name.ToLower().Contains("draw") ||
            m.Name.ToLower().Contains("bow")
        ).ToList();

        Debug.Log("\nDraw-related methods found in Player class:");
        foreach (var method in playerDrawMethods)
        {
            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
            Debug.Log($"  {method.ReturnType.Name} {method.Name}({parameters})");
        }
    }

    // Set draw duration multiplier for testing
    public static void SetDrawDurationMultiplier(float multiplier)
    {
        DrawDurationMultiplier = multiplier;
        isTestingActive = multiplier != 1.0f;

        float percentage = (1.0f - multiplier) * 100f;
        string effect = multiplier < 1.0f ? "faster" : "slower";

        Debug.Log($"Set draw duration multiplier to {multiplier:F3}");
        Debug.Log($"This should make drawing {Mathf.Abs(percentage):F1}% {effect}");
        Debug.Log("Draw duration patches will now modify bow drawing behavior");
    }

    // Reset testing
    public static void ResetTesting()
    {
        DrawDurationMultiplier = 1.0f;
        isTestingActive = false;
        Debug.Log("Reset draw duration testing to default");
    }

    // Check if testing is active
    public static bool IsTestingActive()
    {
        return isTestingActive;
    }

    // Check if current weapon is a bow
    public static bool IsUsingBow(Player player)
    {
        if (player == null) return false;

        ItemDrop.ItemData weapon = player.GetCurrentWeapon();
        if (weapon == null) return false;

        return weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;
    }
}

// Harmony patches to intercept draw duration methods
[HarmonyPatch]
public static class DrawDurationPatches
{
    //Try to patch common draw duration method names
    //[HarmonyPatch(typeof(Attack), "GetDrawDuration")]
    //[HarmonyPostfix]
    //public static void Attack_GetDrawDuration_Postfix(Attack __instance, ref float __result)
    //{
    //    try
    //    {
    //        if (DrawDurationTestManager.IsTestingActive() &&
    //            Player.m_localPlayer != null &&
    //            DrawDurationTestManager.IsUsingBow(Player.m_localPlayer))
    //        {
    //            float originalDuration = __result;
    //            __result *= DrawDurationTestManager.DrawDurationMultiplier;
    //            Debug.Log($"Modified draw duration: {originalDuration:F3} -> {__result:F3} (multiplier: {DrawDurationTestManager.DrawDurationMultiplier:F3})");
    //        }
    //    }
    //    catch (System.Exception ex)
    //    {
    //        // Method might not exist, that's okay
    //        Logger.LogWarning($"GetDrawDuration patch failed (method might not exist): {ex.Message}");
    //    }
    //}

    // Try alternative method name
    //[HarmonyPatch(typeof(Attack), "GetDrawTime")]
    //[HarmonyPostfix]
    //public static void Attack_GetDrawTime_Postfix(Attack __instance, ref float __result)
    //{
    //    try
    //    {
    //        if (DrawDurationTestManager.IsTestingActive() &&
    //            Player.m_localPlayer != null &&
    //            DrawDurationTestManager.IsUsingBow(Player.m_localPlayer))
    //        {
    //            float originalTime = __result;
    //            __result *= DrawDurationTestManager.DrawDurationMultiplier;
    //            Debug.Log($"Modified draw time: {originalTime:F3} -> {__result:F3} (multiplier: {DrawDurationTestManager.DrawDurationMultiplier:F3})");
    //        }
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogWarning($"GetDrawTime patch failed (method might not exist): {ex.Message}");
    //    }
    //}

    // Try patching Player.IsDrawingBow to see when drawing happens
    //[HarmonyPatch(typeof(Player), "IsDrawingBow")]
    //[HarmonyPostfix]
    //public static void Player_IsDrawingBow_Postfix(Player __instance, ref bool __result)
    //{
    //    try
    //    {
    //        if (__result && DrawDurationTestManager.IsTestingActive())
    //        {
    //            Debug.Log("Player is currently drawing bow - draw duration modifications should be active");
    //        }
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogWarning($"IsDrawingBow patch failed: {ex.Message}");
    //    }
    //}

    // Try to patch any Update method in Attack that might handle drawing
    //[HarmonyPatch(typeof(Attack), "Update")]
    //[HarmonyPrefix]
    //public static void Attack_Update_Prefix(Attack __instance, float dt)
    //{
    //    try
    //    {
    //        if (DrawDurationTestManager.IsTestingActive() &&
    //            Player.m_localPlayer != null &&
    //            DrawDurationTestManager.IsUsingBow(Player.m_localPlayer) &&
    //            Player.m_localPlayer.IsDrawingBow())
    //        {
    //            // We could potentially modify dt here, but let's just log for now
    //            // Debug.Log($"Attack.Update called while drawing bow, dt: {dt:F4}");
    //        }
    //    }
    //    catch (System.Exception ex)
    //    {
    //        Logger.LogWarning($"Attack Update patch failed: {ex.Message}");
    //    }
    //}
}

// Console commands for draw duration testing
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class DrawDurationCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("inspectdraw", "Inspect draw mechanics of current bow",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (Player.m_localPlayer == null)
                {
                    args.Context.AddString("No local player found!");
                    return;
                }

                ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
                if (weapon == null)
                {
                    args.Context.AddString("No weapon equipped!");
                    return;
                }

                if (!DrawDurationTestManager.IsUsingBow(Player.m_localPlayer))
                {
                    args.Context.AddString("Current weapon is not a bow!");
                    return;
                }

                args.Context.AddString($"Inspecting draw mechanics: {weapon.m_shared.m_name}");
                args.Context.AddString("Check console/log for detailed method and field information");

                DrawDurationTestManager.InspectDrawMechanics(weapon);
            }
        );

        new Terminal.ConsoleCommand("testdrawduration", "Test draw duration modification (testdrawduration [multiplier])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: testdrawduration [multiplier]");
                    args.Context.AddString("Example: testdrawduration 0.5 (50% faster draw - half duration)");
                    args.Context.AddString("Example: testdrawduration 1.5 (50% slower draw)");
                    args.Context.AddString("Example: testdrawduration 0.75 (25% faster draw)");
                    args.Context.AddString($"Current multiplier: {DrawDurationTestManager.DrawDurationMultiplier:F3}");
                    return;
                }

                if (!float.TryParse(args.Args[1], out float multiplier))
                {
                    args.Context.AddString("Invalid multiplier value");
                    return;
                }

                if (multiplier <= 0)
                {
                    args.Context.AddString("Multiplier must be greater than 0");
                    return;
                }

                DrawDurationTestManager.SetDrawDurationMultiplier(multiplier);

                float percentage = (1.0f - multiplier) * 100f;
                string effect = multiplier < 1.0f ? "faster" : "slower";

                args.Context.AddString($"Set draw duration multiplier to {multiplier:F3}");
                args.Context.AddString($"Draw should be {Mathf.Abs(percentage):F1}% {effect}");

                if (Player.m_localPlayer != null && DrawDurationTestManager.IsUsingBow(Player.m_localPlayer))
                {
                    args.Context.AddString("BOW equipped - try drawing to test the change");
                    args.Context.AddString("Watch console for draw duration modification logs");
                }
                else
                {
                    args.Context.AddString("Equip a bow to test draw duration changes");
                }
            }
        );

        new Terminal.ConsoleCommand("resetdraw", "Reset draw duration testing",
            delegate (Terminal.ConsoleEventArgs args)
            {
                DrawDurationTestManager.ResetTesting();
                args.Context.AddString("Reset draw duration testing to default");
                args.Context.AddString("Draw duration multiplier: 1.0 (normal)");
            }
        );

        new Terminal.ConsoleCommand("drawinfo", "Show current draw testing status",
            delegate (Terminal.ConsoleEventArgs args)
            {
                args.Context.AddString("=== Draw Duration Testing Status ===");
                args.Context.AddString($"Draw Duration Multiplier: {DrawDurationTestManager.DrawDurationMultiplier:F3}");
                args.Context.AddString($"Testing Active: {DrawDurationTestManager.IsTestingActive()}");

                if (Player.m_localPlayer != null)
                {
                    ItemDrop.ItemData weapon = Player.m_localPlayer.GetCurrentWeapon();
                    if (weapon != null)
                    {
                        args.Context.AddString($"Current Weapon: {weapon.m_shared.m_name}");

                        if (DrawDurationTestManager.IsUsingBow(Player.m_localPlayer))
                        {
                            args.Context.AddString("Weapon Type: BOW - compatible with testing");
                            args.Context.AddString($"Currently Drawing: {Player.m_localPlayer.IsDrawingBow()}");
                        }
                        else
                        {
                            args.Context.AddString("Weapon Type: NOT A BOW - testing inactive");
                        }
                    }
                    else
                    {
                        args.Context.AddString("No weapon equipped");
                    }
                }

                args.Context.AddString("");
                args.Context.AddString("Commands:");
                args.Context.AddString("• inspectdraw - Examine bow draw mechanics");
                args.Context.AddString("• testdrawduration [multiplier] - Modify draw speed");
                args.Context.AddString("• resetdraw - Restore normal behavior");
            }
        );
    }
}