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
    private static GameObject classSelectionPanel;
    private static Text descriptionText;
    private static GameObject selectClassButton;
    private static string selectedClassName = "";

    // Class names for the buttons
    private static readonly string[] ClassNames = {
        "Sword Master",
        "Archer",
        "Crusher",
        "Assassin",
        "Pugilist",
        "Mage",
        "Lancer",
        "Bulwark"
    };

    // Class descriptions
    private static readonly Dictionary<string, string> ClassDescriptions = new Dictionary<string, string>
    {
        {
            "Sword Master",
            "Masters of blade combat with exceptional swordsmanship skills.\n\n" +
            "Benefits:\n" +
            "• +15% damage with swords and knives\n" +
            "• +10% parry efficiency\n" +
            "• Faster weapon skill progression\n" +
            "• Special sword techniques unlock at higher levels\n\n" +
            "Ideal for players who prefer melee combat with finesse and precision."
        },
        {
            "Archer",
            "Expert marksmen with unparalleled bow mastery.\n\n" +
            "Benefits:\n" +
            "• +20% bow damage and accuracy\n" +
            "• +25% arrow crafting efficiency\n" +
            "• Reduced stamina cost for bow usage\n" +
            "• Special arrow types become available\n\n" +
            "Perfect for players who enjoy ranged combat and hunting."
        },
        {
            "Crusher",
            "Powerful warriors who excel with heavy weapons.\n\n" +
            "Benefits:\n" +
            "• +20% damage with clubs, hammers, and axes\n" +
            "• +15% knockback power\n" +
            "• Reduced stamina drain from heavy attacks\n" +
            "• Area damage bonuses with two-handed weapons\n\n" +
            "Best suited for players who like devastating melee attacks."
        },
        {
            "Assassin",
            "Stealthy fighters who strike from the shadows.\n\n" +
            "Benefits:\n" +
            "• +30% backstab damage multiplier\n" +
            "• Improved stealth movement\n" +
            "• +10% movement speed when crouching\n" +
            "• Critical hit chance increases at night\n\n" +
            "Great for players who prefer tactical, stealthy gameplay."
        },
        {
            "Pugilist",
            "Bare-knuckle brawlers with unmatched unarmed combat skills.\n\n" +
            "Benefits:\n" +
            "• +25% unarmed damage\n" +
            "• Fists scale with unarmed skill level\n" +
            "• +20% stamina regeneration during combat\n" +
            "• Stunning attacks with high unarmed skill\n\n" +
            "For players who want to fight with their fists like a true Viking."
        },
        {
            "Mage",
            "Mystical practitioners of elemental magic.\n\n" +
            "Benefits:\n" +
            "• +15% magic damage with staves\n" +
            "• +20% eitr (magic stamina) capacity\n" +
            "• Faster eitr regeneration\n" +
            "• Enhanced spell effects and duration\n\n" +
            "Ideal for players who want to master Valheim's magic system."
        },
        {
            "Lancer",
            "Spear specialists with superior reach and technique.\n\n" +
            "Benefits:\n" +
            "• +18% spear damage and thrust speed\n" +
            "• Extended reach for spear attacks\n" +
            "• +15% damage when attacking from behind shields\n" +
            "• Throwing spears deal increased damage\n\n" +
            "Perfect for players who like versatile polearm combat."
        },
        {
            "Bulwark",
            "Defensive specialists who excel at protection and tanking.\n\n" +
            "Benefits:\n" +
            "• +20% block power with shields\n" +
            "• +15% armor effectiveness\n" +
            "• Reduced stamina cost for blocking\n" +
            "• Taunt ability to draw enemy attention\n\n" +
            "Best for players who want to be the party's shield and protector."
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

            // Initialize with default message
            UpdateDescriptionText("Click on a class above to see its description and benefits.");
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
                width: 1300,
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
                position: new Vector2(0f, -100f),
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
        containerRect.anchorMin = new Vector2(0.15f, 0.15f);  // 5% thinner (was 0.1f to 0.9f)
        containerRect.anchorMax = new Vector2(0.85f, 0.65f);  // 5% shorter (was 0.25f to 0.55f)
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
            fontSize: 24,
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

        closeButton.AddComponent<ButtonHoverEffect>();
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
            //var contentRect = descriptionText.transform.parent.GetComponent<RectTransform>();
            //if (contentRect != null)
            //{
            //    float preferredHeight = Mathf.Max(250, descriptionText.preferredHeight + 30);
            //    contentRect.sizeDelta = new Vector2(contentRect.sizeDelta.x, preferredHeight);
            //    Debug.Log($"Updated content height to: {preferredHeight}");
            //}

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
        // Display selection message
        player.Message(MessageHud.MessageType.Center, $"Selected {className} Class!");

        // Log for debugging
        Debug.Log($"Player confirmed selection: {className}");

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