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
                // Patch only the specific class, not the entire assembly
                // This prevents conflicts when multiple patch classes exist in the same assembly
                harmony.CreateClassProcessor(typeof(TMonkey)).Patch();
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to apply {typeof(TMonkey).Name} patches: {ex}");
            }
        }
    }
}

