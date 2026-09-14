using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Jotunn.GUI;
using System.Collections.Generic;
using System.Linq;
using Logger = Jotunn.Logger;
using System.Security;
using ValheimClassObelisk;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
//[NetworkCompatibilityLevel(CompatibilityLevel.EveryoneMustHaveMod)]
internal class ClassObeliskMod : BaseUnityPlugin
{
    public const string PluginGUID = "com.bunzboi.valheimweaponclass";
    public const string PluginName = "ValheimWeaponClasses";
    public const string PluginVersion = "1.0.4";

    private GameObject TestPanel;

    private static ClassObeliskMod _instance;
    private Harmony _harmony;

    // Config
    private ConfigEntry<bool> _enableMod;

    private void Awake()
    {
        _instance = this;

        // Config setup
        _enableMod = Config.Bind("General", "EnableMod", true, "Enable the class obelisk mod");

        if (!_enableMod.Value) return;

        _harmony = new Harmony(PluginGUID);
        _harmony.PatchAll();

        // Try multiple registration approaches
        PrefabManager.OnVanillaPrefabsAvailable += AddClassObelisk;

        Logger.LogInfo($"{PluginName} loaded successfully!");
    }

    private void AddClassObelisk()
    {
        try
        {
            // Create Class Obelisk piece
            var obeliskPrefab = CreateClassObeliskPrefab();
            //var obeliskPrefab = PrefabManager.Instance.GetPrefab("piece_workbench");
            if (obeliskPrefab != null)
            {
                var obeliskPiece = new PieceConfig();
                obeliskPiece.Name = "$class_obelisk";
                obeliskPiece.PieceTable = "Hammer";
                obeliskPiece.Category = "Crafting";
                obeliskPiece.AddRequirement("Wood", 2);

                var classObelisk = new CustomPiece(obeliskPrefab, fixReference: true, obeliskPiece);
                //PieceManager.Instance.AddPiece(obeliskPiece);
                //PieceManager.Instance.AddPiece(new CustomPiece("class_obelisk", "piece_workbench", classObelisk));
                PieceManager.Instance.AddPiece(classObelisk);
                Logger.LogInfo("Class Obelisk piece added successfully!");

                // You want that to run only once, Jotunn has the piece cached for the game session
                PrefabManager.OnVanillaPrefabsAvailable -= AddClassObelisk;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error adding Class Obelisk: {ex.Message}");
        }
    }

    private GameObject CreateClassObeliskPrefab()
    {
        try
        {
            var workbenchPrefab = PrefabManager.Instance.GetPrefab("piece_workbench");
            if (workbenchPrefab == null)
            {
                Logger.LogError("Could not find workbench prefab!");
                return null;
            }
            var obeliskPrefab = PrefabManager.Instance.CreateClonedPrefab("ClassObelisk", workbenchPrefab);

            var obeliskPiece = new PieceConfig();
            obeliskPiece.Name = "$class_obelisk";
            obeliskPiece.PieceTable = "Hammer";
            obeliskPiece.Category = "Crafting";
            obeliskPiece.AddRequirement("Wood", 2);

            // Remove the crafting station functionality(we just want a decorative piece for now)
            var craftingStation = obeliskPrefab.GetComponent<CraftingStation>();
            if (craftingStation != null)
            {
                // These child objects (build-range circle, "in use" hammer icon, fire glow) are
                // normally shown/hidden by CraftingStation's own Start()/Update(), which never runs
                // once the component is destroyed - so force them off or they stay visible forever.
                if (craftingStation.m_areaMarker != null) craftingStation.m_areaMarker.SetActive(false);
                if (craftingStation.m_inUseObject != null) craftingStation.m_inUseObject.SetActive(false);
                if (craftingStation.m_haveFireObject != null) craftingStation.m_haveFireObject.SetActive(false);

                DestroyImmediate(craftingStation);
            }

            // Add our custom component
            var classObelisk = obeliskPrefab.AddComponent<ClassObeliskInteract>();

            Logger.LogInfo("Class Obelisk prefab created successfully!");
            return obeliskPrefab;
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error creating Class Obelisk prefab: {ex.Message}");
            return null;
        }
    }

    public static ClassObeliskMod Instance => _instance;
}

// Class selection interface component
public class ClassObeliskInteract : MonoBehaviour, Hoverable, Interactable
{
    public static GameObject classSelectionPanel;
    public static Text descriptionText;
    public static ScrollRect descriptionScrollRect;
    public static Dictionary<string, GameObject> classButtons = new Dictionary<string, GameObject>();
    public static GameObject activationButton;
    public static Text limitHintText;
    public static string selectedClassName = "";

    private static readonly Color ActiveClassColor = new Color(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Color InactiveClassColor = Color.white;

    // Class names for the buttons
    public static readonly string[] ClassNames = {
        "Sword Master",
        "Archer",
        "Crusher",
        "Assassin",
        "Brawler",
        "Wizard",
        "Lancer",
        "Bulwark"
    };

    // Per-class description providers backed by the PerkManagers - builds each class's
    // description from its Perks metadata, masking any perk the player hasn't reached yet as "???".
    private static readonly Dictionary<string, Func<Player, string>> DynamicClassDescriptionProviders = new Dictionary<string, Func<Player, string>>
    {
        { "Sword Master", SwordMasterPerkManager.GetClassDescription },
        { "Archer", ArcherPerkManager.GetClassDescription },
        { "Crusher", CrusherPerkManager.GetClassDescription },
        { "Assassin", AssassinPerkManager.GetClassDescription },
        { "Brawler", BrawlerPerkManager.GetClassDescription },
        { "Wizard", WizardPerkManager.GetClassDescription },
        { "Lancer", LancerPerkManager.GetClassDescription },
        { "Bulwark", BulwarkPerkManager.GetClassDescription }
    };

    private void Start()
    {
        // Initialize if needed
    }

    private void Update()
    {
        // Check for escape key to close the GUI using Valheim's input system
        if (classSelectionPanel != null && classSelectionPanel.activeSelf)
        {
            // Since our Update function in our BepInEx mod class will load BEFORE Valheim loads,
            // we need to check that ZInput is ready to use first.
            if (ZInput.instance != null)
            {
                if (ZInput.GetButtonDown("Escape") || ZInput.GetButtonDown("JoyMenu"))
                {
                    DevLog.Log("Escape/Menu button pressed - closing class selection GUI");
                    CloseClassSelectionGUI();
                }
            }
        }
    }

    public string GetHoverText()
    {
        return "Class Obelisk\n[<color=yellow>E</color>] Select your combat class";
    }

    public string GetHoverName()
    {
        return "Class Obelisk";
    }

    public float GetHoverOffset()
    {
        return 0f;
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || alt) return false;

        var player = user as Player;
        if (player == null) return false;

        // Open the class selection GUI
        OpenClassSelectionGUI(player);

        return true;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        return false;
    }

    private void OpenClassSelectionGUI(Player player)
    {
        DevLog.Log("Opening enhanced class selection GUI");

        // Close any existing GUI first
        CloseClassSelectionGUI();

        // Create the enhanced GUI
        CreateEnhancedClassSelectionGUI(player);
    }

    private void CreateEnhancedClassSelectionGUI(Player player)
    {
        try
        {
            DevLog.Log("Creating enhanced GUI using Jotunn's GUIManager");

            // Get player class data
            var playerData = PlayerClassManager.GetPlayerData(player);
            if (playerData == null)
            {
                Debug.LogError("Could not get player data for GUI creation");
                return;
            }

            // Create custom wood panel
            CreateWoodenGUIPanel();

            // Create class selection buttons in 2 columns
            CreateTwoColumnClassButtons(player);

            // Create the description window
            CreateDescriptionWindow(player);

            // Create the activation button (and its class-limit hint text)
            CreateActivationButton(player);

            // Create enhanced close button
            CreateEnhancedCloseButton();

            // Initialize with current player status
            var activeClasses = playerData.activeClasses.Count > 0 ? string.Join(", ", playerData.activeClasses) : "None";
            string slotHint = playerData.CanSelectSecondClass()
                ? "Click a class to see its description, then Activate/Deactivate below (up to 2 active)."
                : "Click a class to see its description, then Activate/Deactivate below. Reach level 50 to unlock a second active class.";
            UpdateDescriptionText($"Current Active Classes: {activeClasses}\n\n{slotHint}");
            selectedClassName = "";
            UpdateActivationButton(player);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating enhanced GUI: {ex.Message}");
        }
    }

    private void CreateWoodenGUIPanel()
    {
        // Create the panel if it does not exist
        if (!classSelectionPanel)
        {
            if (GUIManager.Instance == null)
            {
                Logger.LogError("GUIManager instance is null");
                return;
            }

            if (!GUIManager.CustomGUIFront)
            {
                Logger.LogError("GUIManager CustomGUI is null");
                return;
            }

            // Create the panel object - made it taller for the description window
            classSelectionPanel = GUIManager.Instance.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0, 0),
                width: 1450,
                height: 900, // Increased height for description window
                draggable: false);
            classSelectionPanel.SetActive(false);

            // Add the Jötunn draggable Component to the panel
            classSelectionPanel.AddComponent<DragWindowCntrl>();

            // Create the text object
            GameObject textObject = GUIManager.Instance.CreateText(
                text: "Choose a class!",
                parent: classSelectionPanel.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(0f, -50f),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: 30,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: 400f,
                height: 50f,
                addContentSizeFitter: false);

            textObject.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        }

        // Switch the current state
        bool state = !classSelectionPanel.activeSelf;

        // Set the active state of the panel
        classSelectionPanel.SetActive(state);

        // Toggle input for the player and camera while displaying the GUI
        GUIManager.BlockInput(state);
    }

    private void CreateTwoColumnClassButtons(Player player)
    {
        // Create container for buttons
        var buttonContainer = new GameObject("ButtonContainer");
        buttonContainer.transform.SetParent(classSelectionPanel.transform, false);

        var containerRect = buttonContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.05f, 0.65f);  // Positioned above description window
        containerRect.anchorMax = new Vector2(0.95f, 0.85f);  // Takes up width for 4 columns
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;

        // Create buttons in 4 columns, 2 rows
        float buttonWidth = 180f;  // Adjusted for 4 columns
        float buttonHeight = 50f;  // Slightly taller for better readability
        float columnSpacing = 25f; // Spacing between columns
        float rowSpacing = 15f;    // Spacing between rows

        // Calculate starting positions for centered layout with 4 columns
        float totalWidth = (buttonWidth * 4) + (columnSpacing * 3);
        float startX = -totalWidth / 2f + buttonWidth / 2f;
        float startY = 25f; // Start from center of container

        for (int i = 0; i < ClassNames.Length; i++)
        {
            int col = i % 4; // 0, 1, 2, or 3 (4 columns)
            int row = i / 4; // 0 or 1 (2 rows)

            float x = startX + (col * (buttonWidth + columnSpacing));
            float y = startY - (row * (buttonHeight + rowSpacing));

            CreateEnhancedClassButton(buttonContainer, ClassNames[i], player, x, y, buttonWidth, buttonHeight);
        }
    }

    private void CreateEnhancedClassButton(GameObject parent, string className, Player player, float x, float y, float width, float height)
    {
        int level = PlayerClassManager.GetPlayerData(player).GetClassLevel(className);
        string buttonLabel = $"{className} ({level})";
        // Use Jotunn's CreateButton for proper styling
        var button = GUIManager.Instance.CreateButton(
            text: buttonLabel,
            parent: parent.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            position: new Vector2(x, y),
            width: (int)width,
            height: (int)height
        );

        // Get button component
        var buttonComponent = button.GetComponent<Button>();

        // Add click handler - updates the description panel and the Activate/Deactivate button below
        buttonComponent.onClick.AddListener(() => {
            OnClassButtonClicked(className, player);
        });

        // Add hover effect
        button.AddComponent<ButtonHoverEffect>();

        classButtons[className] = button;
        ApplyClassButtonHighlight(className, player);
    }

    // Recolors/relabels a class button to reflect whether it's currently active, so active
    // classes are visible at a glance while browsing descriptions.
    private void ApplyClassButtonHighlight(string className, Player player)
    {
        if (!classButtons.TryGetValue(className, out var button) || button == null) return;

        var playerData = PlayerClassManager.GetPlayerData(player);
        bool isActive = playerData != null && playerData.IsClassActive(className);
        int level = playerData?.GetClassLevel(className) ?? 0;

        var buttonComponent = button.GetComponent<Button>();
        var buttonImage = button.GetComponent<Image>();
        var buttonText = button.GetComponentInChildren<Text>();

        Color color = isActive ? ActiveClassColor : InactiveClassColor;

        var colorBlock = buttonComponent.colors;
        colorBlock.normalColor = color;
        buttonComponent.colors = colorBlock;

        if (buttonImage != null) buttonImage.color = color;
        if (buttonText != null) buttonText.text = $"{className} ({level}){(isActive ? "  [Active]" : "")}";
    }

    private void RefreshClassButtonHighlights(Player player)
    {
        foreach (var className in ClassNames)
        {
            ApplyClassButtonHighlight(className, player);
        }
    }

    private void CreateDescriptionWindow(Player player)
    {
        // TODO: set a new string here that is the players currently selected class.

        // Create a properly sized description container that fits within the panel
        var descriptionContainer = new GameObject("DescriptionContainer");
        descriptionContainer.transform.SetParent(classSelectionPanel.transform, false);

        var containerRect = descriptionContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.15f, 0.2f);  // 5% thinner (was 0.1f to 0.9f)
        containerRect.anchorMax = new Vector2(0.85f, 0.6f);  // 5% shorter (was 0.25f to 0.55f)
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;

        // Add background
        var containerBg = descriptionContainer.AddComponent<Image>();
        containerBg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f); // Dark semi-transparent background

        // Viewport: fixed-size window that clips scrolling content, padded from the container edges
        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(descriptionContainer.transform, false);
        var viewportRect = viewport.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(15, 15); // Padding from edges
        viewportRect.offsetMax = new Vector2(-15, -15);
        viewport.AddComponent<RectMask2D>(); // Clips content outside the fixed viewport bounds

        // Create the description text directly using GUIManager, parented to the viewport so it can scroll
        GameObject descriptionTextObj = GUIManager.Instance.CreateText(
            text: "Select a class to view its description...",
            parent: viewport.transform,
            anchorMin: Vector2.zero,
            anchorMax: Vector2.one,
            position: Vector2.zero,
            font: GUIManager.Instance.AveriaSerif,
            fontSize: 18,
            color: new Color(0.9f, 0.9f, 0.9f, 1f),
            outline: false,
            outlineColor: Color.black,
            width: 0,
            height: 0,
            addContentSizeFitter: false);

        // Get the text component reference
        descriptionText = descriptionTextObj.GetComponent<Text>();
        descriptionText.alignment = TextAnchor.UpperLeft;
        descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

        // The text's own rect is the scrollable "content" - stretched to the viewport's width,
        // anchored to the top, and grown vertically to fit whatever text is set.
        var textRect = descriptionTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(1f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = Vector2.zero;

        var contentSizeFitter = descriptionTextObj.AddComponent<ContentSizeFitter>();
        contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ScrollRect ties the fixed viewport to the growing text content
        descriptionScrollRect = descriptionContainer.AddComponent<ScrollRect>();
        descriptionScrollRect.viewport = viewportRect;
        descriptionScrollRect.content = textRect;
        descriptionScrollRect.horizontal = false;
        descriptionScrollRect.vertical = true;
        descriptionScrollRect.movementType = ScrollRect.MovementType.Clamped;
        descriptionScrollRect.scrollSensitivity = 20f;

        DevLog.Log("Properly sized description text created");
        DevLog.Log("Container rect size: " + containerRect.rect.size);
    }

    private void CreateActivationButton(Player player)
    {
        activationButton = GUIManager.Instance.CreateButton(
            text: "Select a Class",
            parent: classSelectionPanel.transform,
            anchorMin: new Vector2(0.5f, 0.08f),
            anchorMax: new Vector2(0.5f, 0.08f),
            position: new Vector2(0f, 20f),
            width: 200,
            height: 35
        );

        var buttonComponent = activationButton.GetComponent<Button>();
        buttonComponent.onClick.AddListener(() => {
            OnActivationButtonClicked(player);
        });

        // Class-limit hint, shown only when activation is blocked because both slots are full
        GameObject hintObj = GUIManager.Instance.CreateText(
            text: "",
            parent: classSelectionPanel.transform,
            anchorMin: new Vector2(0.5f, 0.08f),
            anchorMax: new Vector2(0.5f, 0.08f),
            position: new Vector2(0f, -20f),
            font: GUIManager.Instance.AveriaSerif,
            fontSize: 16,
            color: new Color(0.85f, 0.45f, 0.35f, 1f),
            outline: false,
            outlineColor: Color.black,
            width: 600f,
            height: 30f,
            addContentSizeFitter: false);

        limitHintText = hintObj.GetComponent<Text>();
        limitHintText.alignment = TextAnchor.MiddleCenter;
        limitHintText.fontStyle = FontStyle.Italic;

        UpdateActivationButton(player);
    }

    private void OnActivationButtonClicked(Player player)
    {
        if (string.IsNullOrEmpty(selectedClassName)) return;

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData == null) return;

        if (playerData.IsClassActive(selectedClassName))
        {
            PlayerClassManager.DeactivateClass(player, selectedClassName);
        }
        else
        {
            PlayerClassManager.ActivateClass(player, selectedClassName);
        }

        // Refresh the description ("Active" tag), the activation button/hint, and every
        // button's highlight to reflect whatever just changed.
        OnClassButtonClicked(selectedClassName, player);
        RefreshClassButtonHighlights(player);
    }

    // Updates the Activate/Deactivate button's label and interactability, and the class-limit
    // hint, to reflect the currently previewed class and the player's current active classes.
    // This is the panel's own feedback mechanism for activation attempts - MessageHud toasts
    // render underneath this panel while it's open, so they're invisible to the player.
    private void UpdateActivationButton(Player player)
    {
        if (activationButton == null) return;

        var buttonComponent = activationButton.GetComponent<Button>();
        var buttonText = activationButton.GetComponentInChildren<Text>();
        var playerData = PlayerClassManager.GetPlayerData(player);

        if (string.IsNullOrEmpty(selectedClassName) || playerData == null)
        {
            buttonComponent.interactable = false;
            if (buttonText != null) buttonText.text = "Select a Class";
            SetLimitHintVisible(false);
            return;
        }

        if (playerData.IsClassActive(selectedClassName))
        {
            buttonComponent.interactable = true;
            if (buttonText != null) buttonText.text = "Deactivate";
            SetLimitHintVisible(false);
            return;
        }

        // Not currently active: activation is allowed either if there's a free slot, or -
        // for players who haven't unlocked a second slot yet - by swapping out their one
        // active class, so it's only ever actually blocked once dual classes are unlocked
        // and both slots are already taken.
        bool hasRoom = playerData.activeClasses.Count < playerData.GetMaxActiveClasses();
        bool canSwap = playerData.GetMaxActiveClasses() == 1;
        bool canActivate = hasRoom || canSwap;

        buttonComponent.interactable = canActivate;
        if (buttonText != null) buttonText.text = "Activate";
        SetLimitHintVisible(!canActivate);
    }

    private void SetLimitHintVisible(bool visible)
    {
        if (limitHintText == null) return;
        limitHintText.text = visible ? "Class limit reached - de-activate another class before picking a new one." : "";
    }

    private void CreateEnhancedCloseButton()
    {
        // Close button with better styling
        var closeButton = GUIManager.Instance.CreateButton(
            text: "X",
            parent: classSelectionPanel.transform,
            anchorMin: new Vector2(0.92f, 0.92f),
            anchorMax: new Vector2(0.98f, 0.98f),
            position: Vector2.zero,
            width: 35,
            height: 35
        );

        var closeComponent = closeButton.GetComponent<Button>();
        var closeImage = closeButton.GetComponent<Image>();

        if (closeImage != null)
        {
            closeImage.color = new Color(0.8f, 0.3f, 0.3f, 0.9f);

            var colorBlock = closeComponent.colors;
            colorBlock.normalColor = new Color(0.8f, 0.3f, 0.3f, 0.9f);
            colorBlock.highlightedColor = new Color(0.9f, 0.4f, 0.4f, 1f);
            colorBlock.pressedColor = new Color(0.7f, 0.2f, 0.2f, 1f);
            closeComponent.colors = colorBlock;
        }

        // Enhance close button text
        var closeText = closeButton.GetComponentInChildren<Text>();
        if (closeText != null)
        {
            closeText.fontSize = 18;
            closeText.fontStyle = FontStyle.Bold;
            closeText.color = Color.white;
        }

        closeComponent.onClick.AddListener(() => {
            DevLog.Log("Enhanced close button clicked");
            CloseClassSelectionGUI();
        });
    }

    private void OnClassButtonClicked(string className, Player player)
    {
        selectedClassName = className;

        // Update description text
        string description = DynamicClassDescriptionProviders.TryGetValue(className, out var provider)
            ? provider(player)
            : $"Description for {className} coming soon...";

        var playerData = PlayerClassManager.GetPlayerData(player);
        if (playerData != null && playerData.IsClassActive(className))
        {
            description += "\n\n\n<size=24><b>Active</b></size>";
        }

        UpdateDescriptionText(description);
        UpdateActivationButton(player);
    }

    private void UpdateDescriptionText(string description)
    {
        if (descriptionText != null)
        {
            DevLog.Log($"Updating description text to: {description.Substring(0, Math.Min(50, description.Length))}...");
            descriptionText.text = description;

            // Force text to wrap properly
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            // Force the content's ContentSizeFitter to recompute its height for the new text
            LayoutRebuilder.ForceRebuildLayoutImmediate(descriptionText.rectTransform);
            Canvas.ForceUpdateCanvases();

            // Reset scroll position to top
            if (descriptionScrollRect != null)
            {
                descriptionScrollRect.verticalNormalizedPosition = 1f;
            }
        }
        else
        {
            Debug.LogError("descriptionText is null when trying to update!");
        }
    }

    private static void CloseClassSelectionGUI()
    {
        if (classSelectionPanel != null)
        {
            DevLog.Log("Closing class selection GUI");
            Destroy(classSelectionPanel);
            classSelectionPanel = null;
            descriptionText = null;
            descriptionScrollRect = null;
            classButtons.Clear();
            activationButton = null;
            limitHintText = null;
            selectedClassName = "";

            // Restore game input if we were using Jotunn's system
            if (GUIManager.Instance != null)
            {
                GUIManager.BlockInput(false);
            }
        }
    }
}

// Simple hover effect component for buttons
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Vector3 originalScale;
    private bool isHovering = false;

    private void Start()
    {
        originalScale = transform.localScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isHovering)
        {
            isHovering = true;
            transform.localScale = originalScale * 1.05f;
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (isHovering)
        {
            isHovering = false;
            transform.localScale = originalScale;
        }
    }
}

// Simple console command for testing
// Dev-only: excluded from Release builds.
#if DEBUG
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class Terminal_InitTerminal_Patch
{
    private static void Postfix()
    {
        new Terminal.ConsoleCommand("obelisktest", "Test the class obelisk functionality",
            delegate (Terminal.ConsoleEventArgs args)
            {
                args.Context.AddString("Class Obelisk mod is working!");
                args.Context.AddString("Try building a Class Obelisk with your hammer.");
            }
        );

        new Terminal.ConsoleCommand("debugpieces", "List all hammer pieces to log file",
            delegate (Terminal.ConsoleEventArgs args)
            {
                try
                {
                    var hammer = ObjectDB.instance?.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData;
                    if (hammer?.m_shared?.m_buildPieces?.m_pieces != null)
                    {
                        var logPath = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "hammer_pieces.txt");
                        var lines = new System.Collections.Generic.List<string>();

                        lines.Add($"=== HAMMER PIECES DEBUG - {System.DateTime.Now} ===");
                        lines.Add($"Total pieces: {hammer.m_shared.m_buildPieces.m_pieces.Count}");
                        lines.Add("");

                        foreach (var piece in hammer.m_shared.m_buildPieces.m_pieces)
                        {
                            var pieceName = piece.name;
                            if (pieceName.Contains("Obelisk") || pieceName.Contains("Class"))
                            {
                                lines.Add($"*** FOUND OUR PIECE: {pieceName} ***");
                            }
                            else
                            {
                                lines.Add($"- {pieceName}");
                            }
                        }

                        System.IO.File.WriteAllLines(logPath, lines);
                        args.Context.AddString($"Hammer pieces written to: {logPath}");
                        args.Context.AddString($"Total pieces: {hammer.m_shared.m_buildPieces.m_pieces.Count}");
                    }
                    else
                    {
                        args.Context.AddString("Could not find hammer or its piece table!");
                    }
                }
                catch (System.Exception ex)
                {
                    args.Context.AddString($"Error writing debug file: {ex.Message}");
                }
            }
        );
    }
}
#endif