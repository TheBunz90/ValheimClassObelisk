using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;
using System.Reflection;
using System.Linq;
using System;
using System.Text;

/// <summary>
/// Console commands for exploring Valheim's API - discovering methods, fields, and types
/// </summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
public static class ApiDiscoveryCommands
{
    [HarmonyPostfix]
    public static void InitTerminal_Postfix()
    {
        new Terminal.ConsoleCommand("list_methods", "Lists methods for a specified class (list_methods <className> [filter])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: list_methods <className> [filter]");
                    args.Context.AddString("Example: list_methods Player damage");
                    args.Context.AddString("Example: list_methods Character attack");
                    args.Context.AddString("Shortcuts: player, skills, character, humanoid, itemdrop, piece, attack");
                    return;
                }

                string className = args.Args[1];
                string filter = args.Length > 2 ? args.Args[2].ToLower() : "";

                try
                {
                    Type targetType = GetTypeByName(className);

                    if (targetType == null)
                    {
                        args.Context.AddString($"Type '{className}' not found.");
                        args.Context.AddString("Available shortcuts: player, skills, character, humanoid, itemdrop, piece, attack");
                        return;
                    }

                    args.Context.AddString($"Methods for {targetType.Name}:");
                    var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => string.IsNullOrEmpty(filter) || m.Name.ToLower().Contains(filter))
                        .OrderBy(m => m.Name)
                        .GroupBy(m => m.Name)
                        .Take(25); // Show up to 25 method groups

                    int count = 0;
                    foreach (var methodGroup in methods)
                    {
                        // Show first overload of each method
                        var method = methodGroup.First();
                        var parameters = string.Join(", ", method.GetParameters().Take(3).Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        if (method.GetParameters().Length > 3) parameters += "...";

                        var modifiers = "";
                        if (method.IsStatic) modifiers += "static ";
                        if (method.IsPublic) modifiers += "public ";
                        else if (method.IsPrivate) modifiers += "private ";
                        else modifiers += "protected ";

                        args.Context.AddString($"  {modifiers}{method.ReturnType.Name} {method.Name}({parameters})");

                        // If there are multiple overloads, show count
                        if (methodGroup.Count() > 1)
                        {
                            args.Context.AddString($"    (+{methodGroup.Count() - 1} more overloads)");
                        }

                        count++;
                    }

                    if (count == 0)
                    {
                        args.Context.AddString("No methods found matching the criteria.");
                    }
                    else if (count >= 25)
                    {
                        args.Context.AddString("... (showing first 25 results, use filter to narrow down)");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"Error listing methods: {ex.Message}");
                    Logger.LogError($"list_methods error: {ex}");
                }
            }
        );

        new Terminal.ConsoleCommand("list_fields", "Lists fields for a specified class (list_fields <className> [filter])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: list_fields <className> [filter]");
                    args.Context.AddString("Example: list_fields Player health");
                    args.Context.AddString("Example: list_fields ItemDrop shared");
                    args.Context.AddString("Shortcuts: player, skills, character, humanoid, itemdrop, piece, attack");
                    return;
                }

                string className = args.Args[1];
                string filter = args.Length > 2 ? args.Args[2].ToLower() : "";

                try
                {
                    Type targetType = GetTypeByName(className);

                    if (targetType == null)
                    {
                        args.Context.AddString($"Type '{className}' not found.");
                        return;
                    }

                    args.Context.AddString($"Fields for {targetType.Name}:");
                    var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(f => string.IsNullOrEmpty(filter) || f.Name.ToLower().Contains(filter))
                        .OrderBy(f => f.Name)
                        .Take(30);

                    int count = 0;
                    foreach (var field in fields)
                    {
                        var modifiers = "";
                        if (field.IsStatic) modifiers += "static ";
                        if (field.IsPublic) modifiers += "public ";
                        else if (field.IsPrivate) modifiers += "private ";
                        else modifiers += "protected ";

                        args.Context.AddString($"  {modifiers}{field.FieldType.Name} {field.Name}");
                        count++;
                    }

                    if (count == 0)
                    {
                        args.Context.AddString("No fields found matching the criteria.");
                    }
                    else if (count >= 30)
                    {
                        args.Context.AddString("... (showing first 30 results, use filter to narrow down)");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"Error listing fields: {ex.Message}");
                    Logger.LogError($"list_fields error: {ex}");
                }
            }
        );

        new Terminal.ConsoleCommand("list_skills", "Lists all player skills and their levels",
            delegate (Terminal.ConsoleEventArgs args)
            {
                var player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("No player found");
                    return;
                }

                try
                {
                    // Try to get skills using reflection to find the correct field/property
                    var playerType = typeof(Player);
                    var skillsField = playerType.GetField("m_skills", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (skillsField == null)
                    {
                        args.Context.AddString("Skills field not found. Use 'list_fields player skill' to find correct field name.");
                        return;
                    }

                    var skills = skillsField.GetValue(player);
                    if (skills == null)
                    {
                        args.Context.AddString("Skills object is null");
                        return;
                    }

                    // Try to get skill data using reflection
                    var skillsType = skills.GetType();
                    var skillDataField = skillsType.GetField("m_skillData", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (skillDataField == null)
                    {
                        args.Context.AddString("Skill data field not found. Skills object exists but structure unknown.");
                        return;
                    }

                    var skillData = skillDataField.GetValue(skills);
                    args.Context.AddString($"Found skills object of type: {skillsType.Name}");
                    args.Context.AddString("Use 'list_methods skills' to explore available methods");
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"Error accessing skills: {ex.Message}");
                    args.Context.AddString("Try 'list_fields player' to find skill-related fields");
                }
            }
        );

        new Terminal.ConsoleCommand("inspect_object", "Inspect current values of an object's fields (inspect_object <target>)",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: inspect_object <target>");
                    args.Context.AddString("Targets: player, weapon, skills, character");
                    return;
                }

                string target = args.Args[1].ToLower();

                try
                {
                    switch (target)
                    {
                        case "player":
                            InspectPlayerObject(args);
                            break;
                        case "weapon":
                            InspectCurrentWeapon(args);
                            break;
                        case "skills":
                            InspectSkillsObject(args);
                            break;
                        case "character":
                            InspectCharacterObject(args);
                            break;
                        default:
                            args.Context.AddString($"Unknown target: {target}");
                            args.Context.AddString("Available targets: player, weapon, skills, character");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"Error inspecting object: {ex.Message}");
                    Logger.LogError($"inspect_object error: {ex}");
                }
            }
        );

        new Terminal.ConsoleCommand("find_type", "Find types by name pattern (find_type <pattern>)",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: find_type <pattern>");
                    args.Context.AddString("Example: find_type Player");
                    args.Context.AddString("Example: find_type Attack");
                    return;
                }

                string pattern = args.Args[1].ToLower();

                try
                {
                    var types = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .Where(t => t.Name.ToLower().Contains(pattern))
                        .OrderBy(t => t.Name)
                        .Take(20);

                    args.Context.AddString($"Types matching '{pattern}':");

                    int count = 0;
                    foreach (var type in types)
                    {
                        args.Context.AddString($"  {type.FullName}");
                        count++;
                    }

                    if (count == 0)
                    {
                        args.Context.AddString("No types found matching the pattern.");
                    }
                    else if (count >= 20)
                    {
                        args.Context.AddString("... (showing first 20 results)");
                    }
                }
                catch (Exception ex)
                {
                    args.Context.AddString($"Error finding types: {ex.Message}");
                    Logger.LogError($"find_type error: {ex}");
                }
            }
        );

        new Terminal.ConsoleCommand("test_draw_duration", "Test modifying bow draw duration (test_draw_duration [multiplier])",
            delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: test_draw_duration [multiplier]");
                    args.Context.AddString("Example: test_draw_duration 0.5 (50% faster - half time)");
                    args.Context.AddString("Example: test_draw_duration 2.0 (twice as slow)");
                    return;
                }

                if (!float.TryParse(args.Args[1], out float multiplier))
                {
                    args.Context.AddString("Invalid multiplier value");
                    return;
                }

                var player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("No local player found");
                    return;
                }

                var weapon = player.GetCurrentWeapon();
                if (weapon == null || !weapon.m_shared.m_itemType.Equals(ItemDrop.ItemData.ItemType.Bow))
                {
                    args.Context.AddString("No bow equipped!");
                    return;
                }

                var attack = weapon.m_shared.m_attack;
                if (attack == null)
                {
                    args.Context.AddString("No attack component found");
                    return;
                }

                float originalDuration = attack.m_drawDurationMin;
                float newDuration = originalDuration * multiplier;

                // Modify the draw duration
                attack.m_drawDurationMin = newDuration;

                args.Context.AddString($"Modified bow draw duration:");
                args.Context.AddString($"  Original: {originalDuration:F2}s");
                args.Context.AddString($"  New: {newDuration:F2}s");
                args.Context.AddString($"  Multiplier: {multiplier:F2}x");
                args.Context.AddString("Try drawing your bow to test the change!");
            }
        );

        new Terminal.ConsoleCommand("inspect_bow_stats", "Show detailed bow draw statistics",
            delegate (Terminal.ConsoleEventArgs args)
            {
                var player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("No local player found");
                    return;
                }

                var weapon = player.GetCurrentWeapon();
                if (weapon == null || !weapon.m_shared.m_itemType.Equals(ItemDrop.ItemData.ItemType.Bow))
                {
                    args.Context.AddString("No bow equipped!");
                    return;
                }

                var attack = weapon.m_shared.m_attack;
                if (attack == null)
                {
                    args.Context.AddString("No attack component found");
                    return;
                }

                args.Context.AddString($"=== {weapon.m_shared.m_name} Stats ===");
                args.Context.AddString($"Draw Duration Min: {attack.m_drawDurationMin:F2}s");
                args.Context.AddString($"Speed Factor: {attack.m_speedFactor:F2}");
                args.Context.AddString($"Reload Time: {attack.m_reloadTime:F2}s");
                args.Context.AddString($"Draw Stamina Drain: {attack.m_drawStaminaDrain:F1}");
                args.Context.AddString($"Bow Draw Enabled: {attack.m_bowDraw}");

                if (player.IsDrawingBow())
                {
                    args.Context.AddString($"Currently Drawing: {player.GetAttackDrawPercentage():F1}%");
                }
            }
        );

        new Terminal.ConsoleCommand("api_help", "Show help for API discovery commands",
            delegate (Terminal.ConsoleEventArgs args)
            {
                args.Context.AddString("=== API Discovery Commands ===");
                args.Context.AddString("list_methods <class> [filter] - List methods in a class");
                args.Context.AddString("list_fields <class> [filter]  - List fields in a class");
                args.Context.AddString("list_skills                  - Show all player skills");
                args.Context.AddString("inspect_object <target>      - Inspect object values");
                args.Context.AddString("find_type <pattern>          - Find types by name");
                args.Context.AddString("test_draw_duration <mult>    - Test bow draw speed");
                args.Context.AddString("inspect_bow_stats            - Show bow statistics");
                args.Context.AddString("");
                args.Context.AddString("Class shortcuts: player, skills, character, humanoid,");
                args.Context.AddString("                 itemdrop, piece, attack");
                args.Context.AddString("");
                args.Context.AddString("Examples:");
                args.Context.AddString("  list_methods player damage");
                args.Context.AddString("  list_fields attack draw");
                args.Context.AddString("  inspect_object weapon");
                args.Context.AddString("  test_draw_duration 0.5");
            }
        );
    }

    /// <summary>
    /// Get a Type by name, including shortcuts for common Valheim classes
    /// </summary>
    private static Type GetTypeByName(string className)
    {
        // Handle shortcuts for common classes
        switch (className.ToLower())
        {
            case "player":
                return typeof(Player);
            case "skills":
                return typeof(Skills);
            case "character":
                return typeof(Character);
            case "humanoid":
                return typeof(Humanoid);
            case "itemdrop":
                return typeof(ItemDrop);
            case "piece":
                return typeof(Piece);
            case "attack":
                return typeof(Attack);
            default:
                // Try to find the type by name
                return Type.GetType(className) ??
                       AppDomain.CurrentDomain.GetAssemblies()
                           .SelectMany(a => a.GetTypes())
                           .FirstOrDefault(t => t.Name.Equals(className, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void InspectPlayerObject(Terminal.ConsoleEventArgs args)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            args.Context.AddString("No local player found");
            return;
        }

        args.Context.AddString("=== Player Object ===");
        args.Context.AddString($"Name: {player.GetPlayerName()}");
        args.Context.AddString($"Health: {player.GetHealth():F1}/{player.GetMaxHealth():F1}");
        args.Context.AddString($"Stamina: {player.GetStamina():F1}/{player.GetMaxStamina():F1}");
        args.Context.AddString($"Level: {player.GetLevel()}");

        var weapon = player.GetCurrentWeapon();
        if (weapon != null)
        {
            args.Context.AddString($"Current Weapon: {weapon.m_shared.m_name}");
        }
    }

    private static void InspectCurrentWeapon(Terminal.ConsoleEventArgs args)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            args.Context.AddString("No local player found");
            return;
        }

        var weapon = player.GetCurrentWeapon();
        if (weapon == null)
        {
            args.Context.AddString("No weapon equipped");
            return;
        }

        args.Context.AddString("=== Current Weapon ===");
        args.Context.AddString($"Name: {weapon.m_shared.m_name}");
        args.Context.AddString($"Type: {weapon.m_shared.m_itemType}");
        args.Context.AddString($"Skill Type: {weapon.m_shared.m_skillType}");
        args.Context.AddString($"Quality: {weapon.m_quality}");
        args.Context.AddString($"Durability: {weapon.m_durability:F1}/{weapon.GetMaxDurability():F1}");

        if (weapon.m_shared.m_attack != null)
        {
            args.Context.AddString($"Attack Damage: {weapon.GetDamage():F1}");
            //args.Context.AddString($"Block Power: {weapon.GetBlockPower():F1}");
        }
    }

    private static void InspectSkillsObject(Terminal.ConsoleEventArgs args)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            args.Context.AddString("No local player found");
            return;
        }

        try
        {
            // Try to find skills using reflection
            var playerType = typeof(Player);
            var skillsField = playerType.GetField("m_skills", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (skillsField == null)
            {
                args.Context.AddString("=== Skills Object ===");
                args.Context.AddString("Skills field not found in Player class");
                args.Context.AddString("Use 'list_fields player skill' to find correct field name");
                return;
            }

            var skills = skillsField.GetValue(player);
            if (skills == null)
            {
                args.Context.AddString("Skills object is null");
                return;
            }

            args.Context.AddString("=== Skills Object ===");
            args.Context.AddString($"Skills Type: {skills.GetType().Name}");
            args.Context.AddString("Skills object found - use 'list_methods skills' for exploration");
        }
        catch (Exception ex)
        {
            args.Context.AddString("=== Skills Object ===");
            args.Context.AddString($"Error accessing skills: {ex.Message}");
        }
    }

    private static void InspectCharacterObject(Terminal.ConsoleEventArgs args)
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            args.Context.AddString("No local player found");
            return;
        }

        args.Context.AddString("=== Character Object ===");
        args.Context.AddString($"Is Player: {player.IsPlayer()}");
        args.Context.AddString($"Faction: {player.GetFaction()}");
        args.Context.AddString($"Tamed: {player.IsTamed()}");
        args.Context.AddString($"Boss: {player.IsBoss()}");
        args.Context.AddString($"Flying: {player.IsFlying()}");
        args.Context.AddString($"Swimming: {player.IsSwimming()}");
        args.Context.AddString($"On Ground: {player.IsOnGround()}");
    }
}