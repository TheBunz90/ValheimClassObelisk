//
// AnimationSpeedManager.cs
//
// A tiny, vendorable helper for stacking animation/attack-speed multipliers in Valheim.
// Drop this file into your project, reference Harmony, and call AnimationSpeedManager.Set/Clear
// when your effect/equipment turns on/off.
//
// ─────────────────────────────────────────────────────────────────────────────
//  QUICK USAGE (in your mod code):
//
//  // When the item/affix becomes active (e.g., on equip):
//  AnimationSpeedManager.Set(player, $"EpicLoot_AttackSpeed_{itemUID}", 1.15f); // +15%
//
//  // If the value changes, just call Set again with the same key.
//
//  // When the item/affix stops applying (e.g., on unequip):
//  AnimationSpeedManager.Clear(player, $"EpicLoot_AttackSpeed_{itemUID}");
//
//  // Optional: temporarily enable/disable all scaling globally
//  AnimationSpeedManager.Enabled = true;  // default true
//
//  NOTES:
//  • Multipliers are multiplied together (1.10 * 1.15 * 0.90 = 1.1385).
//  • We only apply scaling while the character is attacking (best-effort heuristic).
//  • If you prefer “always-on” animator scaling, set APPLY_ALWAYS = true below.
// ─────────────────────────────────────────────────────────────────────────────
//
// Requirements:
//  • HarmonyX (or HarmonyLib), BepInEx
//  • Works for local player (and can affect other Characters if you call Set on them).
//
// Implementation details:
//  • We keep a per-Character map of (string key -> float multiplier).
//  • Each frame we compute the product for that Character.
//  • A Harmony postfix on CharacterAnimEvent.CustomFixedUpdate (runs during attack/animation ticks)
//    applies Animator.speed = baseSpeed * product (baseSpeed is assumed 1.0).
//  • If your Valheim version differs and this method name changes, see the #PATCH_TARGET section
//    for alternative places to patch (e.g., Character.Update, Humanoid.Update, or Player.Update).
//
// Attribution:
//  • Inspired by community “AnimationSpeedManager” helpers used across several Valheim mods.
//    This implementation is clean-room and simplified for easy vendoring in your project.
//
// MIT-style permission: Do whatever you like with this file, no warranty.
//

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Logger = Jotunn.Logger;

namespace ValheimClassObelisk
{
    /// <summary>
    /// Public API you call from your mod code.
    /// </summary>
    public static class AnimationSpeedManager
    {
        private static readonly System.Reflection.FieldInfo _animField =
            typeof(Character).GetField("m_animator", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        // ─────────────────────────────────────────────────────────────────────────
        // CONFIG TOGGLE: If true, scale Animator.speed every frame (not just in attack).
        // Recommended to keep false so walking/roll/etc. aren’t sped up unless intended.
        private const bool APPLY_ALWAYS = false;
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>Globally enables/disables speed application without touching registrations.</summary>
        public static bool Enabled { get; set; } = true;

        // Internal storage: per-character bag of (key -> multiplier).
        // Using ConditionalWeakTable so entries drop when Character is destroyed.
        private static readonly ConditionalWeakTable<Character, PerCharacterBag> _perChar
            = new ConditionalWeakTable<Character, PerCharacterBag>();

        private static readonly MethodInfo _miInAttack = AccessTools.Method(typeof(Humanoid), "InAttack");

        private static bool IsAttackActive(Character c)
        {
            // Most characters you care about are Humanoids (Player/NPCs)
            if (c is Humanoid h)
            {
                // Preferred: call Humanoid.InAttack() if present
                if (_miInAttack != null)
                {
                    try { return (bool)_miInAttack.Invoke(h, null); }
                    catch { /* fall through to animator heuristic */ }
                }
            }

            // Fallback heuristic via animator flags (names vary by controller)
            var anim = GetAnimator(c);
            if (anim != null)
            {
                try
                {
                    if (anim.GetBool("attacking") || anim.GetBool("attack") || anim.GetBool("isAttacking"))
                        return true;
                }
                catch { /* parameter may not exist */ }
            }

            return false;
        }

        /// <summary>
        /// Register or update a speed multiplier source for a given Character.
        /// Use a stable, unique <paramref name="key"/> (e.g., include your item UID).
        /// </summary>
        public static void Set(Character c, string key, float multiplier)
        {
            if (c == null || string.IsNullOrEmpty(key)) return;
            if (multiplier <= 0f)
            {
                Logger.LogInfo("Setting attack speed buffer");
                multiplier = 0.0001f; // avoid zero/negatives
            }

            var bag = _perChar.GetOrCreateValue(c);
            bag.Set(key, multiplier);
        }

        /// <summary>
        /// Remove a previously registered speed multiplier source.
        /// </summary>
        public static void Clear(Character c, string key)
        {
            Logger.LogInfo($"Clearing {key} for {c.m_name}");
            if (c == null || string.IsNullOrEmpty(key)) return;
            _perChar.TryGetValue(c, out var myBag);
            if (myBag != null) Logger.LogInfo("Found my bag");
            else Logger.LogInfo("Did not find my bag");
            if (_perChar.TryGetValue(c, out var bag))
                bag.Remove(key);
        }

        /// <summary>
        /// Remove all sources for a character (e.g., on logout/destroy safety).
        /// </summary>
        public static void ClearAll(Character c)
        {
            if (c == null) return;
            if (_perChar.TryGetValue(c, out var bag))
                bag.Clear();
        }

        /// <summary>
        /// Calculate the combined multiplier for a character (product of all entries).
        /// Returns 1.0f if none.
        /// </summary>
        public static float GetKnifeMultiplier(Character c)
        {
            if (c == null) return 1f;
            if (_perChar.TryGetValue(c, out var bag))
                return bag.CombinedKnifeAttackSpeed;
            return 1f;
        }

        public static float GetFistMultiplier(Character c)
        {
            if (c == null)
            {
                return 1f;
            }
            if (_perChar.TryGetValue(c, out var bag))
            {
                return bag.CombinedFistAttackSpeed;
            }
            return 1f;
        }

        private static Animator GetAnimator(Character c)
        {
            if (c == null) return null;
            return _animField?.GetValue(c) as Animator;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // INTERNAL STRUCTURES
        // ─────────────────────────────────────────────────────────────────────────

        private sealed class PerCharacterBag
        {
            private readonly Dictionary<string, float> _sources = new Dictionary<string, float>(StringComparer.Ordinal);
            private float _cachedProduct = 1f;
            private bool _dirty = false;

            public float CombinedKnifeAttackSpeed
            {
                get
                {
                    if (_dirty)
                    {
                        float prod = 1f;
                        foreach (var kv in _sources)
                        {
                            if (kv.Key.Contains("_Knife_AS")) prod *= kv.Value;
                        }
                        _cachedProduct = prod;
                        _dirty = false;
                    }
                    return _cachedProduct;
                }
            }

            public float CombinedFistAttackSpeed
            {
                get
                {
                    if (_dirty)
                    {
                        float prod = 1f;
                        foreach (var kv in _sources)
                        {
                            if (kv.Key.Contains("Fist")) prod *= kv.Value;
                        }
                        _cachedProduct = prod;
                        _dirty = false;
                    }
                    return _cachedProduct;
                }
            }

            public float CombinedMoveSpeed
            {
                get
                {
                    if (_dirty)
                    {
                        float prod = 1f;
                        foreach (var kv in _sources)
                        {
                            if (kv.Key.Contains("_MS")) prod *= kv.Value;
                        }
                        _cachedProduct = prod;
                        _dirty = false;
                    }
                    return _cachedProduct;
                }
            }

            public void Set(string key, float mult)
            {
                if (_sources.TryGetValue(key, out var old))
                {
                    if (Math.Abs(old - mult) < 0.0001f) return;
                    _sources[key] = mult;
                }
                else
                {
                    _sources.Add(key, mult);
                }
                _dirty = true;
            }

            public void Remove(string key)
            {
                if (_sources.Remove(key))
                    _dirty = true;
            }

            public void Clear()
            {
                if (_sources.Count == 0) return;
                _sources.Clear();
                _dirty = true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // PATCH: Apply the multiplier to the Animator during animation ticks
        // ─────────────────────────────────────────────────────────────────────────
        internal static void ApplyIfNeeded(Character c)
        {
            if (!Enabled || c == null) return;

            var anim = GetAnimator(c);       // your existing safe getter (GetComponentInChildren<Animator>())
            if (anim == null) return;

            bool attacking = IsAttackActive(c);     // <- use the helper above

            Player player = Player.m_localPlayer;
            if (player == null) return;
            var currentWeapon = player.GetCurrentWeapon();
            float mult = 0f;


            if (ClassCombatManager.IsKnifeWeapon(currentWeapon)) mult = GetKnifeMultiplier(c);  // product of Set() calls (e.g., 1.20f)
            if (ClassCombatManager.IsUnarmedAttack(currentWeapon)) mult = GetFistMultiplier(c);

            if (mult == 0f) return;

            float target = Mathf.Max(0.0001f, mult);

            if (attacking)
            {
                if (Mathf.Abs(anim.speed - target) > 0.001f) anim.speed = target;
            }
            else
            {
                // ensure locomotion/etc. stays normal when not attacking
                if (Mathf.Abs(anim.speed - 1f) > 0.001f) anim.speed = 1f;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // HARMONY PATCHES
    // ─────────────────────────────────────────────────────────────────────────
    //
    // #PATCH_TARGET:
    // We target CharacterAnimEvent.CustomFixedUpdate because it runs during
    // animation/attack processing on Characters. If your Valheim build lacks this
    // method name/signature, switch to a stable update loop, for example:
    //  • Character.Update (Postfix)
    //  • Humanoid.Update (Postfix)
    //  • Player.Update (Postfix)
    //
    // Just call AnimationSpeedManager.ApplyIfNeeded(__instance) in that postfix.
    //
    [HarmonyPatch]
    internal static class AnimationSpeedManagerPatches
    {
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> _characterRef =
        AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");

        // Try to bind to CharacterAnimEvent.CustomFixedUpdate (no args) if present.
        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        [HarmonyPostfix]
        private static void CharacterAnimEvent_CustomFixedUpdate_Postfix(CharacterAnimEvent __instance)
        {
            // CharacterAnimEvent has a m_character reference in most versions.
            Character character = null;

            try { character = _characterRef(__instance); } catch { Logger.LogError("Unable to convert instance to character"); }

            if (character != null)
            {
                AnimationSpeedManager.ApplyIfNeeded(character);
            }
        }

        // Fallback: If your build doesn’t have CharacterAnimEvent.CustomFixedUpdate,
        // uncomment this patch as a universal fallback:
        /*
        [HarmonyPatch(typeof(Character), nameof(Character.Update))]
        [HarmonyPostfix]
        private static void Character_Update_Postfix(Character __instance)
        {
            AnimationSpeedManager.ApplyIfNeeded(__instance);
        }
        */
    }

    [HarmonyPatch]
    internal static class Character_OnDestroy_Patch
    {
        // Tell Harmony explicitly which method to hook
        static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(Character), "OnDestroy");

        [HarmonyPostfix]
        private static void Postfix(Character __instance)
        {
            AnimationSpeedManager.ClearAll(__instance);
        }
    }
}
