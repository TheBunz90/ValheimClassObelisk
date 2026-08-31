using System.Collections.Generic;
using HarmonyLib;
using Logger = Jotunn.Logger;

// Adds an "Active Classes" entry to the Valheim Compendium (the raven-icon screen, internally
// TextsDialog) alongside the vanilla "Active Effects" and "Logs" entries. TextsDialog.Setup()
// calls UpdateTextsList() fresh every time the Compendium is opened, so patching here keeps
// the entry's contents current without any separate live-refresh mechanism.
[HarmonyPatch]
public static class ActiveClassesTextsDialogPatch
{
    [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
    [HarmonyPostfix]
    public static void TextsDialog_UpdateTextsList_Postfix(List<TextsDialog.TextInfo> ___m_texts)
    {
        try
        {
            if (Player.m_localPlayer == null) return;

            string text = PlayerClassManager.BuildActiveClassesCompendiumText(Player.m_localPlayer);
            ___m_texts.Insert(0, new TextsDialog.TextInfo("Active Classes", text));
        }
        catch (System.Exception ex)
        {
            Logger.LogError($"Error adding Active Classes compendium entry: {ex}");
        }
    }
}
