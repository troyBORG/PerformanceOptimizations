using HarmonyLib;
using System;
using System.Collections.Generic;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Base class for performance optimization patches using Harmony.
    /// For ResoniteModLoader (RML).
    /// </summary>
    internal abstract class PerformanceOptimizationsMonkey<TMonkey>
        where TMonkey : PerformanceOptimizationsMonkey<TMonkey>, new()
    {
        public abstract IEnumerable<string> Authors { get; }
        
        public static bool Enabled => PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IsOptimizationEnabled(GetOptimizationName());

        private static string GetOptimizationName()
        {
            var name = typeof(TMonkey).Name;
            if (name.StartsWith("Optimize"))
                return name.Substring(8); // Remove "Optimize" prefix
            return name;
        }

        /// <summary>
        /// Initialize Harmony patches for this optimization.
        /// </summary>
        public static void Initialize(Harmony harmony)
        {
            try
            {
                // Patch all Harmony patches in the assembly
                // HarmonyPatchCategory attributes help organize patches
                // Some warnings may appear but patches will still apply correctly
                harmony.PatchAll(typeof(TMonkey).Assembly);
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to apply {typeof(TMonkey).Name} patches: {ex}");
            }
        }
    }
}

