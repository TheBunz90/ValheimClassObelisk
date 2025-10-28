using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class SEUtils
{
    /// <summary>
    /// Checks if a Character currently has a StatusEffect of type T (e.g., SE_Poison).
    /// </summary>
    public static bool HasEffect<T>(Character c) where T : StatusEffect
    {
        if (c == null) return false;
        var seMan = c.GetSEMan();
        if (seMan == null) return false;

        // Use public API instead of m_statusEffects
        return seMan.GetStatusEffects().Any(se => se is T);
    }

    public static bool IsPoisoned(Character c) => HasEffect<SE_Poison>(c);
    public static bool IsBurning(Character c) => HasEffect<SE_Burning>(c);
    public static bool IsFrosted(Character c) => HasEffect<SE_Frost>(c);

    /// <summary>
    /// Safe name-based check using the *public* API.
    /// </summary>
    public static bool HasEffectByName(Character c, string effectName)
    {
        if (c == null || string.IsNullOrWhiteSpace(effectName))
            return false;

        var seMan = c.GetSEMan();
        if (seMan == null)
            return false;

        var list = seMan.GetStatusEffects();
        return list.Any(se =>
            string.Equals(se.name, effectName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(se.m_name, effectName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(se.GetType().Name, effectName, StringComparison.OrdinalIgnoreCase));
    }

    public static class SEReflection
    {
        private static readonly FieldInfo _fiTime = typeof(StatusEffect).GetField("m_time", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fiTTL = typeof(StatusEffect).GetField("m_ttl", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo _fiStacks = typeof(StatusEffect).GetField("m_stacks", BindingFlags.Instance | BindingFlags.NonPublic);

        public static float GetElapsedTime(StatusEffect se) => (float)(_fiTime?.GetValue(se) ?? 0f);
        public static float GetDuration(StatusEffect se) => (float)(_fiTTL?.GetValue(se) ?? 0f);
        public static int GetStacks(StatusEffect se) => (int)(_fiStacks?.GetValue(se) ?? 0);
    }

    /// <summary>
    /// Dumps all current status effects (for debugging).
    /// </summary>
    public static void DumpActiveEffects(Character c, string tag = "SEUtils")
    {
        var seMan = c?.GetSEMan();
        if (seMan == null)
        {
            Debug.Log($"[{tag}] No SEMan found.");
            return;
        }

        var effects = seMan.GetStatusEffects();
        if (effects == null || effects.Count == 0)
        {
            Debug.Log($"[{tag}] No active status effects.");
            return;
        }

        foreach (var se in effects)
        {
            float elapsed = SEReflection.GetElapsedTime(se);
            float ttl = SEReflection.GetDuration(se);
            int stacks = SEReflection.GetStacks(se);

            Debug.Log($"[{tag}] Effect: {se.GetType().Name} | Name='{se.m_name}' | UnityName='{se.name}' | Elapsed={elapsed:F2} / {ttl:F2} | Stacks={stacks}");
        }
    }

}
