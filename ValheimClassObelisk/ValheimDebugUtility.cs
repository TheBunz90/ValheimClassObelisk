using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Logger = Jotunn.Logger;

// Debug utility class for discovering method signatures and game internals
public static class ValheimDebugUtility
{
    private static string GetLogDirectory()
    {
        return Path.Combine(BepInEx.Paths.BepInExRootPath, "DebugLogs");
    }

    private static void EnsureLogDirectory()
    {
        var logDir = GetLogDirectory();
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
    }

    private static void WriteToLogFile(string filename, List<string> lines)
    {
        try
        {
            EnsureLogDirectory();
            var logPath = Path.Combine(GetLogDirectory(), filename);

            var output = new List<string>();
            output.Add($"=== Valheim Debug Output - {DateTime.Now} ===");
            output.Add($"Valheim Version: {Application.version}");
            output.Add($"Unity Version: {Application.unityVersion}");
            output.Add("");
            output.AddRange(lines);

            File.WriteAllLines(logPath, output);
            Debug.Log($"Debug output written to: {logPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to write debug log: {ex.Message}");
        }
    }

    // Console commands for method discovery and debugging
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class DebugConsoleCommands
    {
        [HarmonyPostfix]
        public static void InitTerminal_Postfix()
        {
            new Terminal.ConsoleCommand("listmethods", "List all methods in a class to file (listmethods [classname])",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    if (args.Length < 2)
                    {
                        args.Context.AddString("Usage: listmethods [classname]");
                        args.Context.AddString("Example: listmethods Projectile");
                        args.Context.AddString("Output will be written to DebugLogs/methods_[classname].txt");
                        return;
                    }

                    string className = args.Args[1];
                    var type = AccessTools.TypeByName(className);
                    if (type == null)
                    {
                        args.Context.AddString($"Could not find type: {className}");
                        args.Context.AddString("Try exact class names like 'Projectile', 'Character', 'Player', etc.");
                        return;
                    }

                    var output = new List<string>();
                    output.Add($"=== All Methods for {className} ===");
                    output.Add($"Full type name: {type.FullName}");
                    output.Add($"Assembly: {type.Assembly.GetName().Name}");
                    output.Add("");

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .OrderBy(m => m.Name)
                        .ToList();

                    foreach (var method in methods)
                    {
                        var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        var modifiers = GetMethodModifiers(method);
                        output.Add($"{modifiers} {method.ReturnType.Name} {method.Name}({parameters})");

                        // Add detailed parameter info for Harmony patches
                        if (method.GetParameters().Length > 0)
                        {
                            output.Add($"  // Parameter types for Harmony: {string.Join(", ", method.GetParameters().Select(p => $"typeof({p.ParameterType.FullName})"))}");
                        }
                        output.Add("");
                    }

                    output.Add($"Total methods found: {methods.Count}");

                    WriteToLogFile($"methods_{className}.txt", output);
                    args.Context.AddString($"Methods for {className} written to DebugLogs/methods_{className}.txt");
                    args.Context.AddString($"Found {methods.Count} total methods");
                }
            );

            new Terminal.ConsoleCommand("findmethod", "Find methods containing keyword to file (findmethod [classname] [keyword])",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: findmethod [classname] [keyword]");
                        args.Context.AddString("Example: findmethod Projectile hit");
                        args.Context.AddString("Output will be written to DebugLogs/findmethod_[classname]_[keyword].txt");
                        return;
                    }

                    string className = args.Args[1];
                    string keyword = args.Args[2].ToLower();
                    var type = AccessTools.TypeByName(className);

                    if (type == null)
                    {
                        args.Context.AddString($"Could not find type: {className}");
                        return;
                    }

                    var output = new List<string>();
                    output.Add($"=== Methods in {className} containing '{keyword}' ===");
                    output.Add($"Search keyword: {keyword}");
                    output.Add($"Full type name: {type.FullName}");
                    output.Add("");

                    var matchingMethods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => m.Name.ToLower().Contains(keyword))
                        .OrderBy(m => m.Name)
                        .ToList();

                    if (matchingMethods.Count == 0)
                    {
                        output.Add($"No methods found containing '{keyword}'");
                    }
                    else
                    {
                        foreach (var method in matchingMethods)
                        {
                            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            var fullParameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                            var modifiers = GetMethodModifiers(method);

                            output.Add($"{modifiers} {method.ReturnType.Name} {method.Name}({parameters})");
                            output.Add($"  Full signature: {method.ReturnType.FullName} {method.Name}({fullParameters})");

                            if (method.GetParameters().Length > 0)
                            {
                                output.Add($"  Harmony types: {string.Join(", ", method.GetParameters().Select(p => $"typeof({p.ParameterType.FullName})"))}");
                            }
                            output.Add("");
                        }
                    }

                    output.Add($"Total matching methods: {matchingMethods.Count}");

                    WriteToLogFile($"findmethod_{className}_{keyword}.txt", output);
                    args.Context.AddString($"Method search results written to DebugLogs/findmethod_{className}_{keyword}.txt");
                    args.Context.AddString($"Found {matchingMethods.Count} matching methods");
                }
            );

            new Terminal.ConsoleCommand("methodsig", "Get exact signatures for a method to file (methodsig [classname] [methodname])",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    if (args.Length < 3)
                    {
                        args.Context.AddString("Usage: methodsig [classname] [methodname]");
                        args.Context.AddString("Output will be written to DebugLogs/methodsig_[classname]_[methodname].txt");
                        return;
                    }

                    string className = args.Args[1];
                    string methodName = args.Args[2];
                    var type = AccessTools.TypeByName(className);

                    if (type == null)
                    {
                        args.Context.AddString($"Could not find type: {className}");
                        return;
                    }

                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .Where(m => m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var output = new List<string>();
                    output.Add($"=== Method signatures for {className}.{methodName} ===");
                    output.Add($"Class: {type.FullName}");
                    output.Add($"Method name: {methodName}");
                    output.Add("");

                    if (methods.Count == 0)
                    {
                        output.Add($"No method named '{methodName}' found in {className}");
                    }
                    else
                    {
                        for (int i = 0; i < methods.Count; i++)
                        {
                            var method = methods[i];
                            var parameters = method.GetParameters();
                            var parameterDetails = string.Join(", ", parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                            var modifiers = GetMethodModifiers(method);

                            output.Add($"=== Overload {i + 1} ===");
                            output.Add($"{modifiers} {method.ReturnType.FullName} {method.Name}({parameterDetails})");
                            output.Add("");

                            output.Add("Harmony Patch Example:");
                            if (parameters.Length > 0)
                            {
                                output.Add($"[HarmonyPatch(typeof({className}), \"{methodName}\", new Type[] {{");
                                foreach (var param in parameters)
                                {
                                    output.Add($"    typeof({param.ParameterType.FullName}),");
                                }
                                output.Add("})]");
                            }
                            else
                            {
                                output.Add($"[HarmonyPatch(typeof({className}), \"{methodName}\")]");
                            }
                            output.Add("");

                            output.Add("Method signature for patch:");
                            var patchParams = new List<string> { $"{className} __instance" };
                            patchParams.AddRange(parameters.Select(p => $"{p.ParameterType.FullName} {p.Name}"));
                            output.Add($"public static void {methodName}_Prefix({string.Join(", ", patchParams)})");
                            output.Add("");
                            output.Add("".PadRight(50, '='));
                            output.Add("");
                        }
                    }

                    WriteToLogFile($"methodsig_{className}_{methodName}.txt", output);
                    args.Context.AddString($"Method signatures written to DebugLogs/methodsig_{className}_{methodName}.txt");
                    args.Context.AddString($"Found {methods.Count} overloads");
                }
            );

            new Terminal.ConsoleCommand("debugpath", "Show debug log directory path",
                delegate (Terminal.ConsoleEventArgs args)
                {
                    var logDir = GetLogDirectory();
                    args.Context.AddString($"Debug logs are written to: {logDir}");
                    args.Context.AddString($"Directory exists: {Directory.Exists(logDir)}");
                }
            );
        }

        private static string GetMethodModifiers(MethodInfo method)
        {
            var modifiers = new List<string>();

            if (method.IsPublic) modifiers.Add("public");
            else if (method.IsPrivate) modifiers.Add("private");
            else if (method.IsFamily) modifiers.Add("protected");
            else if (method.IsAssembly) modifiers.Add("internal");

            if (method.IsStatic) modifiers.Add("static");
            if (method.IsVirtual && !method.IsAbstract) modifiers.Add("virtual");
            if (method.IsAbstract) modifiers.Add("abstract");
            //if (method.IsSealed) modifiers.Add("sealed");

            return string.Join(" ", modifiers);
        }

        private static string GetFieldModifiers(FieldInfo field)
        {
            var modifiers = new List<string>();

            if (field.IsPublic) modifiers.Add("public");
            else if (field.IsPrivate) modifiers.Add("private");
            else if (field.IsFamily) modifiers.Add("protected");
            else if (field.IsAssembly) modifiers.Add("internal");

            if (field.IsStatic) modifiers.Add("static");
            if (field.IsInitOnly) modifiers.Add("readonly");

            return string.Join(" ", modifiers);
        }
    }
}