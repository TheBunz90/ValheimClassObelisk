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

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[BepInDependency(Jotunn.Main.ModGuid)]
//[NetworkCompatibilityLevel(CompatibilityLevel.EveryoneMustHaveMod)]
internal class ClassObeliskMod : BaseUnityPlugin
{
    public const string PluginGUID = "com.yourname.classobelisk";
    public const string PluginName = "Class Obelisk";
    public const string PluginVersion = "1.0.0";

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

    private void Start()
    {
        // Initialize if needed
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
        Debug.Log("Opening class selection GUI using Jotunn GUIManager");

        // Close any existing GUI first
        CloseClassSelectionGUI();

        // Create the GUI using Jotunn's system
        CreateJotunnClassSelectionGUI(player);
    }

    private void CreateJotunnClassSelectionGUI(Player player)
    {
        // Check if GUIManager is available
        if (GUIManager.Instance == null)
        {
            Debug.LogError("GUIManager.Instance is null!");
            // Fall back to the old method if Jotunn isn't ready
            CreateClassSelectionPanel(player);
            return;
        }

        try
        {
            Debug.Log("Creating GUI using Jotunn's GUIManager");

            // Create a simple panel using Jotunn
            var gui = new GameObject("ClassSelectionPanel");
            gui.transform.SetParent(GUIManager.CustomGUIFront.transform, false);

            // Add RectTransform
            var rect = gui.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // Add background
            var bg = gui.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.8f);

            classSelectionPanel = gui;

            Debug.Log("Created Jotunn GUI panel");

            // Create title using simple method
            CreateJotunnTitle(gui);

            // Create class selection buttons
            CreateJotunnClassButtons(gui, player);

            // Create close button using Jotunn's CreateButton
            var closeButton = GUIManager.Instance.CreateButton(
                text: "X",
                parent: gui.transform,
                anchorMin: new Vector2(0.9f, 0.9f),
                anchorMax: new Vector2(0.95f, 0.95f),
                position: Vector2.zero,
                width: 40,
                height: 40
            );

            closeButton.GetComponent<Button>().onClick.AddListener(() => {
                Debug.Log("Close button clicked");
                CloseClassSelectionGUI();
            });

            // Block game input while GUI is open
            GUIManager.BlockInput(true);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error creating Jotunn GUI: {ex.Message}");
            // Fall back to the old method
            CreateClassSelectionPanel(player);
        }
    }

    private void CreateJotunnTitle(GameObject parent)
    {
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(parent.transform, false);

        var titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.2f, 0.8f);
        titleRect.anchorMax = new Vector2(0.8f, 0.9f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        var titleText = titleObj.AddComponent<Text>();
        titleText.text = "Choose Your Combat Class";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 24;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;
    }

    private void CreateJotunnClassButtons(GameObject parent, Player player)
    {
        // Create buttons using Jotunn's CreateButton method
        // Simple vertical layout for better compatibility
        float buttonHeight = 40f;
        float buttonWidth = 200f;
        float spacing = 5f;
        float startY = 250f;

        for (int i = 0; i < ClassNames.Length; i++)
        {
            float yPos = startY - (i * (buttonHeight + spacing));

            var button = GUIManager.Instance.CreateButton(
                text: ClassNames[i],
                parent: parent.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: new Vector2(0f, yPos),
                width: (int)buttonWidth,
                height: (int)buttonHeight
            );

            // Store the class name for the click handler
            string className = ClassNames[i];

            button.GetComponent<Button>().onClick.AddListener(() => {
                Debug.Log($"Selected class: {className}");
                OnClassSelected(className, player);
            });
        }
    }

    // Keep your original fallback method in case Jotunn isn't available
    private void CreateClassSelectionPanel(Player player)
    {
        Debug.Log("Attempting to find GUI canvas...");

        // Try multiple methods to find the correct canvas
        GameObject hudCanvas = null;

        // Method 1: Look for IngameGui
        var ingameGui = GameObject.Find("IngameGui");
        if (ingameGui != null)
        {
            hudCanvas = ingameGui;
            Debug.Log("Found IngameGui canvas");
        }

        // Method 2: Look for GUI/Canvas structure  
        if (hudCanvas == null)
        {
            var gui = GameObject.Find("GUI");
            if (gui != null)
            {
                var canvas = gui.transform.Find("Canvas");
                if (canvas != null)
                {
                    hudCanvas = canvas.gameObject;
                    Debug.Log("Found GUI/Canvas structure");
                }
            }
        }

        // Method 3: Find any Canvas component
        if (hudCanvas == null)
        {
            var allCanvases = GameObject.FindObjectsOfType<Canvas>();
            foreach (var canvas in allCanvases)
            {
                if (canvas.gameObject.activeInHierarchy && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    hudCanvas = canvas.gameObject;
                    Debug.Log($"Found Canvas component: {canvas.gameObject.name}");
                    break;
                }
            }
        }

        if (hudCanvas == null)
        {
            Debug.LogError("Could not find any suitable GUI canvas!");
            return;
        }

        Debug.Log($"Using canvas: {hudCanvas.name}");

        // Create main panel
        classSelectionPanel = new GameObject("ClassSelectionPanel");
        classSelectionPanel.transform.SetParent(hudCanvas.transform, false);

        // Add RectTransform manually
        var panelRect = classSelectionPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Add Canvas Group for fading
        var canvasGroup = classSelectionPanel.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;

        // Panel background
        var panelImage = classSelectionPanel.AddComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.8f); // Semi-transparent black

        Debug.Log("Main panel created, adding title and buttons...");

        // Title
        CreateTitle(classSelectionPanel);

        // Create button grid
        CreateClassButtons(classSelectionPanel, player);

        // Close button
        CreateCloseButton(classSelectionPanel);

        Debug.Log("Class selection panel creation complete");
    }

    private void CreateTitle(GameObject parent)
    {
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(parent.transform, false);

        var titleText = titleObj.AddComponent<Text>();
        titleText.text = "Select Your Combat Class";
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.fontSize = 24;
        titleText.color = Color.white;
        titleText.alignment = TextAnchor.MiddleCenter;

        var titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 0.8f);
        titleRect.anchorMax = new Vector2(1f, 0.95f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
    }

    private void CreateClassButtons(GameObject parent, Player player)
    {
        Debug.Log($"Starting button creation");

        // Create a simple vertical layout instead of grid
        float buttonHeight = 35f;
        float buttonWidth = 250f;
        float spacing = 5f;
        float startY = 200f;

        // Create buttons for each class in a simple vertical layout
        for (int i = 0; i < ClassNames.Length; i++)
        {
            CreateSimpleClassButton(parent, ClassNames[i], i, player, buttonWidth, buttonHeight, startY - (i * (buttonHeight + spacing)));
        }
    }

    private void CreateSimpleClassButton(GameObject parent, string className, int classIndex, Player player, float width, float height, float yPos)
    {
        Debug.Log($"Creating button for class: {className} (index: {classIndex})");

        var buttonObj = new GameObject($"Button_{className}");
        buttonObj.transform.SetParent(parent.transform, false);

        // Add RectTransform manually
        var buttonRect = buttonObj.AddComponent<RectTransform>();
        buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
        buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.pivot = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(width, height);
        buttonRect.anchoredPosition = new Vector2(0f, yPos);

        // Button background
        var buttonImage = buttonObj.AddComponent<Image>();
        buttonImage.color = new Color(0.2f, 0.3f, 0.5f, 0.9f);

        // Button component
        var button = buttonObj.AddComponent<Button>();
        button.targetGraphic = buttonImage;

        // Button text
        var textObj = new GameObject("Text");
        textObj.transform.SetParent(buttonObj.transform, false);

        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var buttonText = textObj.AddComponent<Text>();
        buttonText.text = className;
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 16;
        buttonText.color = Color.white;
        buttonText.alignment = TextAnchor.MiddleCenter;

        // Button click handler
        button.onClick.AddListener(() => OnClassSelected(className, player));

        // Hover effects
        var colorBlock = button.colors;
        colorBlock.highlightedColor = new Color(0.3f, 0.4f, 0.7f, 0.9f);
        colorBlock.pressedColor = new Color(0.1f, 0.2f, 0.4f, 0.9f);
        button.colors = colorBlock;

        Debug.Log($"Button {className} created successfully");
    }

    private void CreateCloseButton(GameObject parent)
    {
        var closeButtonObj = new GameObject("CloseButton");
        closeButtonObj.transform.SetParent(parent.transform, false);

        // Add RectTransform manually
        var closeRect = closeButtonObj.AddComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(0.9f, 0.9f);
        closeRect.anchorMax = new Vector2(0.95f, 0.95f);
        closeRect.sizeDelta = new Vector2(30f, 30f);
        closeRect.anchoredPosition = Vector2.zero;

        // Button background
        var closeImage = closeButtonObj.AddComponent<Image>();
        closeImage.color = new Color(0.8f, 0.2f, 0.2f, 0.9f);

        // Button component
        var closeButton = closeButtonObj.AddComponent<Button>();
        closeButton.targetGraphic = closeImage;

        // X text
        var closeTextObj = new GameObject("Text");
        closeTextObj.transform.SetParent(closeButtonObj.transform, false);

        var closeTextRect = closeTextObj.AddComponent<RectTransform>();
        closeTextRect.anchorMin = Vector2.zero;
        closeTextRect.anchorMax = Vector2.one;
        closeTextRect.offsetMin = Vector2.zero;
        closeTextRect.offsetMax = Vector2.zero;

        var closeText = closeTextObj.AddComponent<Text>();
        closeText.text = "X";
        closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        closeText.fontSize = 16;
        closeText.color = Color.white;
        closeText.alignment = TextAnchor.MiddleCenter;

        // Close button click handler
        closeButton.onClick.AddListener(CloseClassSelectionGUI);
    }

    private void OnClassSelected(string className, Player player)
    {
        // Display selection message
        player.Message(MessageHud.MessageType.Center, $"{className} Selected!");

        // Log for debugging
        Debug.Log($"Player selected class: {className}");

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

            // Restore game input if we were using Jotunn's system
            if (GUIManager.Instance != null)
            {
                GUIManager.BlockInput(false);
            }
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