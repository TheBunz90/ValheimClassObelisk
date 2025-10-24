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

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
//[NetworkCompatibilityLevel(CompatibilityLevel.EveryoneMustHaveMod)]
internal class ClassObeliskMod : BaseUnityPlugin
{
    public const string PluginGUID = "com.yourname.classobelisk";
    public const string PluginName = "Class Obelisk";
    public const string PluginVersion = "1.0.0";

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
    public static GameObject selectClassButton;
    public static string selectedClassName = "";

    // Class names for the buttons
    public static readonly string[] ClassNames = {
        "Sword Master",
        "Archer",
        "Crusher",
        "Assassin",
        "Brawler",
        "Mage",
        "Lancer",
        "Bulwark"
    };

    // Class descriptions
    // Class descriptions
    public static readonly Dictionary<string, string> ClassDescriptions = new Dictionary<string, string>
{
    {
        "Sword Master",
        "Masters of blade combat with exceptional swordsmanship skills.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Riposte Training: +10% sword damage. After you parry, next sword hit within 2s deals +25% damage\n" +
        "• Lv20 – Dancing Steel: 15% increased attack speed with swords\n" +
        "• Lv30 – Fencer's Footwork: -15% sword stamina cost; +10% movement speed for 3s after hits\n" +
        "• Lv40 – Weakpoint Cut: +15% armor penetration; +25% stagger vs. humanoids/undead\n" +
        "• Lv50 – Counter Attacker: +50% Parry Bonus, +40 Block Power with all swords\n\n" +
        "Ideal for players who prefer melee combat with finesse and precision."
    },
    {
        "Archer",
        "Expert marksmen with unparalleled bow and crossbow mastery.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Steady Draw: -15% stamina drain while drawing\n" +
        "• Lv20 – Arrow Slinger: Arrows give a buff on hit that reduces draw time by 50% for 10 seconds\n" +
        "• Lv30 – Wind Reader: +15% damage beyond 25m; -25% stamina while aiming\n" +
        "• Lv40 – Magic Shot: 50% chance to not consume an arrow on attack\n" +
        "• Lv50 – Adrenaline Rush: Consecutive hits return 5% stamina\n\n" +
        "Perfect for players who enjoy ranged combat and precision shooting."
    },
    {
        "Crusher",
        "Powerful warriors who excel with heavy blunt weapons.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Bonebreaker: +15% blunt damage; +25% stagger power\n" +
        "• Lv20 – Cold Steel: Melee attacks imbued with frost dealing +20% weapon damage as frost\n" +
        "• Lv30 – Thundering Blows: Heavy melee attacks generate 2m shockwave of lightning damage\n" +
        "• Lv40 – Might of the Earth: -30% stamina drain on attacks from wielding heavy weapons\n" +
        "• Lv50 – Colossus: Ignore movement speed penalties from armor weight\n\n" +
        "Best suited for players who like devastating area attacks and crowd control."
    },
    {
        "Assassin",
        "Stealthy fighters who strike from the shadows with deadly precision.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Cutthroat: +15% knife damage; +30% backstab multiplier\n" +
        "• Lv20 – Venom Coating: Knife hits apply stacking Poison (up to 3 stacks) based on skill level\n" +
        "• Lv30 – Envenomous: Poisons apply 15% movement speed slow per stack\n" +
        "• Lv40 – Assassination: First knife hit from stealth deals +100% damage\n" +
        "• Lv50 – Twist the Knife: +25% damage to poisoned targets; poisoned enemies deal -10% damage\n\n" +
        "Great for players who prefer tactical, stealthy gameplay and damage over time."
    },
    {
        "Brawler",
        "Bare-knuckle brawlers with unmatched unarmed combat skills.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – One-Two Combo: Every 3rd consecutive punch deals +100% damage and restores 5% stamina\n" +
        "• Lv20 – Break Guard: Fist attacks deal +50% stagger damage\n" +
        "• Lv30 – Iron Fist: Fist attacks deal extra damage equal to 10% max health; +20% attack speed\n" +
        "• Lv40 – Tough: When not wearing chest piece, gain +25% physical damage resistance\n" +
        "• Lv50 – Rage: After unblocked damage, enter 5s rage: +50% attack speed, +50% damage resist, +25% fist damage (15s cooldown)\n\n" +
        "For players who want to fight with their fists like a true Viking warrior."
    },
    {
        "Mage",
        "Mystical practitioners of elemental magic and arcane arts.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Eitr Weave: +25% Eitr regeneration\n" +
        "• Lv20 – Icy Hot: +25% Fire and Frost damage\n" +
        "• Lv30 – Hungry For Knowledge: +25% more Eitr from food sources\n" +
        "• Lv40 – Frost Armor: Dealing 300 frost damage triggers Frost Armor (30s): +25% armor, fire immunity\n" +
        "• Lv50 – Immolation Aura: Dealing 500 fire damage triggers Immolation Aura (30s): +25% speed, 15 fire DPS to nearby enemies\n\n" +
        "Ideal for players who want to master Valheim's magic system and elemental combat."
    },
    {
        "Lancer",
        "Spear specialists with superior reach and polearm technique.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Reach Advantage: +10% damage; -15% thrust stamina cost\n" +
        "• Lv20 – Drill: Consecutive hits grant +5% armor penetration (stacks 5 times, refreshes on hit)\n" +
        "• Lv30 – Impale: First hit vs. unalerted targets deals +30% damage and +100% stagger\n" +
        "• Lv40 – Knightly: Blocking charges spear; first attack after block has +100% stagger damage\n" +
        "• Lv50 – Opportunist: Attacks against staggered targets deal +50% damage\n\n" +
        "Perfect for players who like versatile polearm combat and tactical positioning."
    },
    {
        "Bulwark",
        "Defensive specialists who excel at protection and shield mastery.\n\n" +
        "Passive Perks by Level:\n" +
        "• Lv10 – Shield Wall: +15% Block Power; -15% block stamina cost\n" +
        "• Lv20 – Perfect Guard: 260° block radius; blocked attacks restore 5 stamina\n" +
        "• Lv30 – Towering Presence: -50% movement penalty while blocking; tower shields gain +10% Block Power\n" +
        "• Lv40 – Thorns: Blocked attacks return 50% damage with increased stagger\n" +
        "• Lv50 – Reverb!: After blocking 200 damage, release shockwave dealing 200 blunt damage in 2m (10s cooldown)\n\n" +
        "Best for players who want to be the party's shield and ultimate protector."
    }
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
                    Debug.Log("Escape/Menu button pressed - closing class selection GUI");
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
        Debug.Log("Opening enhanced class selection GUI");

        // Close any existing GUI first
        CloseClassSelectionGUI();

        // Create the enhanced GUI
        CreateEnhancedClassSelectionGUI(player);
    }

    private void CreateEnhancedClassSelectionGUI(Player player)
    {
        try
        {
            Debug.Log("Creating enhanced GUI using Jotunn's GUIManager");

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
            CreateDescriptionWindow();

            // Create the select class button
            CreateSelectClassButton(player);

            // Create enhanced close button
            CreateEnhancedCloseButton();

            // Initialize with current player status
            var activeClasses = playerData.activeClasses.Count > 0 ? string.Join(", ", playerData.activeClasses) : "None";
            UpdateDescriptionText($"Current Active Classes: {activeClasses}\n\nClick on a class above to see its description and benefits.");
            selectedClassName = "";
            UpdateSelectButton();
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
        // Use Jotunn's CreateButton for proper styling
        var button = GUIManager.Instance.CreateButton(
            text: className,
            parent: parent.transform,
            anchorMin: new Vector2(0.5f, 0.5f),
            anchorMax: new Vector2(0.5f, 0.5f),
            position: new Vector2(x, y),
            width: (int)width,
            height: (int)height
        );

        // Get button component
        var buttonComponent = button.GetComponent<Button>();

        // Add click handler - now updates description instead of selecting immediately
        buttonComponent.onClick.AddListener(() => {
            Debug.Log($"Viewing class: {className}");
            OnClassButtonClicked(className);
        });

        // Add hover effect
        button.AddComponent<ButtonHoverEffect>();
    }

    private void CreateDescriptionWindow()
    {
        // Create a properly sized description container that fits within the panel
        var descriptionContainer = new GameObject("DescriptionContainer");
        descriptionContainer.transform.SetParent(classSelectionPanel.transform, false);

        var containerRect = descriptionContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.15f, 0.275f);  // 5% thinner (was 0.1f to 0.9f)
        containerRect.anchorMax = new Vector2(0.85f, 0.525f);  // 5% shorter (was 0.25f to 0.55f)
        containerRect.offsetMin = Vector2.zero;
        containerRect.offsetMax = Vector2.zero;

        // Add background
        var containerBg = descriptionContainer.AddComponent<Image>();
        containerBg.color = new Color(0.1f, 0.1f, 0.1f, 0.8f); // Dark semi-transparent background

        // Create the description text directly using GUIManager
        GameObject descriptionTextObj = GUIManager.Instance.CreateText(
            text: "Select a class to view its description...",
            parent: descriptionContainer.transform,
            anchorMin: Vector2.zero, // Fill the container
            anchorMax: Vector2.one,  // Fill the container
            position: Vector2.zero,
            font: GUIManager.Instance.AveriaSerif,
            fontSize: 14,
            color: new Color(0.9f, 0.9f, 0.9f, 1f),
            outline: false,
            outlineColor: Color.black,
            width: 0, // Use container width
            height: 0, // Use container height
            addContentSizeFitter: false);

        // Get the text component reference
        descriptionText = descriptionTextObj.GetComponent<Text>();
        descriptionText.alignment = TextAnchor.UpperLeft;
        descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
        descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

        // Add proper padding within the container
        var textRect = descriptionTextObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(15, 15); // Padding from edges
        textRect.offsetMax = new Vector2(-15, -15); // Negative padding on right/top

        Debug.Log("Properly sized description text created");
        Debug.Log("Container rect size: " + containerRect.rect.size);
        Debug.Log("Text rect size: " + textRect.rect.size);
    }

    private void CreateSelectClassButton(Player player)
    {
        selectClassButton = GUIManager.Instance.CreateButton(
            text: "Select Class",
            parent: classSelectionPanel.transform,
            anchorMin: new Vector2(0.5f, 0.08f), // Moved up slightly from the very bottom
            anchorMax: new Vector2(0.5f, 0.08f),
            position: new Vector2(0f, 20f), // Centered with some margin from bottom
            width: 200,
            height: 35 // Slightly smaller height
        );

        var buttonComponent = selectClassButton.GetComponent<Button>();

        // Add click handler
        buttonComponent.onClick.AddListener(() => {
            if (!string.IsNullOrEmpty(selectedClassName))
            {
                OnClassSelected(selectedClassName, player);
            }
        });

        // Initially disable the button
        UpdateSelectButton();
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
            Debug.Log("Enhanced close button clicked");
            CloseClassSelectionGUI();
        });
    }

    private void OnClassButtonClicked(string className)
    {
        selectedClassName = className;

        // Update description text
        if (ClassDescriptions.ContainsKey(className))
        {
            UpdateDescriptionText(ClassDescriptions[className]);
        }
        else
        {
            UpdateDescriptionText($"Description for {className} coming soon...");
        }

        // Update select button state
        UpdateSelectButton();
    }

    private void UpdateDescriptionText(string description)
    {
        if (descriptionText != null)
        {
            Debug.Log($"Updating description text to: {description.Substring(0, Math.Min(50, description.Length))}...");
            descriptionText.text = description;

            // Force text to wrap properly
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Overflow;

            // Force canvas update
            Canvas.ForceUpdateCanvases();

            // Adjust content height based on text
            var contentRect = descriptionText.transform.parent.GetComponent<RectTransform>();
            if (contentRect != null)
            {
                float preferredHeight = Mathf.Max(250, descriptionText.preferredHeight + 30);
                contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, preferredHeight);
                Debug.Log($"Updated content height to: {preferredHeight}");
            }

            // Reset scroll position to top
            var scrollView = classSelectionPanel.GetComponentInChildren<ScrollRect>();
            if (scrollView != null)
            {
                scrollView.normalizedPosition = new Vector2(0, 1);
            }
        }
        else
        {
            Debug.LogError("descriptionText is null when trying to update!");
        }
    }

    private void UpdateSelectButton()
    {
        if (selectClassButton != null)
        {
            var buttonComponent = selectClassButton.GetComponent<Button>();
            var buttonText = selectClassButton.GetComponentInChildren<Text>();

            if (string.IsNullOrEmpty(selectedClassName))
            {
                buttonComponent.interactable = false;
                if (buttonText != null) buttonText.text = "Select a Class";
            }
            else
            {
                buttonComponent.interactable = true;
                if (buttonText != null) buttonText.text = $"Select {selectedClassName}";
            }
        }
    }

    private void OnClassSelected(string className, Player player)
    {
        Debug.Log($"OnClassSelected called with className: '{className}' for player: {player?.GetPlayerName() ?? "null"}");

        if (string.IsNullOrEmpty(className))
        {
            Debug.LogError("Cannot select class: className is null or empty");
            player?.Message(MessageHud.MessageType.Center, "Error: Invalid class selection");
            return;
        }

        // Use the enhanced manager method
        bool success = PlayerClassManager.SetPlayerActiveClass(player, className);

        if (success)
        {
            // Get updated data to confirm change
            var playerData = PlayerClassManager.GetPlayerData(player);
            var activeClassesList = string.Join(", ", playerData.activeClasses);

            // Display success message
            player.Message(MessageHud.MessageType.Center, $"Selected {className} Class! Active: {activeClassesList}");

            Debug.Log($"Successfully selected class {className} for {player.GetPlayerName()}. Active classes: {activeClassesList}");
        }
        else
        {
            player.Message(MessageHud.MessageType.Center, $"Failed to select {className} class");
            Debug.LogError($"Failed to set active class {className} for {player.GetPlayerName()}");
        }

        // Close the GUI
        CloseClassSelectionGUI();
    }

    private static void CloseClassSelectionGUI()
    {
        if (classSelectionPanel != null)
        {
            Debug.Log("Closing class selection GUI");
            Destroy(classSelectionPanel);
            classSelectionPanel = null;
            descriptionText = null;
            selectClassButton = null;
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