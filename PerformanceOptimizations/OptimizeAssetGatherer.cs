using HarmonyLib;
using SkyFrost.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Optimizes AssetGatherer by using ConcurrentStack stored separately to eliminate lock contention.
    /// Since we can't change the field type at runtime, we use a ConditionalWeakTable to store
    /// a ConcurrentStack separately and use it in the method patches.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeAssetGatherer))]
    internal sealed class OptimizeAssetGatherer : PerformanceOptimizationsMonkey<OptimizeAssetGatherer>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        // Store ConcurrentStack separately since we can't change the field type
        private static readonly ConditionalWeakTable<AssetGatherer, ConcurrentStack<byte[]>> ConcurrentStacks = new();

        /// <summary>
        /// Get or create the ConcurrentStack for an AssetGatherer instance.
        /// </summary>
        internal static ConcurrentStack<byte[]> GetConcurrentStack(AssetGatherer instance)
        {
            return ConcurrentStacks.GetValue(instance, _ => new ConcurrentStack<byte[]>());
        }
    }

    /// <summary>
    /// Patches BorrowBuffer to use ConcurrentStack operations.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeAssetGatherer))]
    [HarmonyPatch(typeof(AssetGatherer), "BorrowBuffer")]
    internal sealed class OptimizeAssetGathererBorrowBuffer : PerformanceOptimizationsMonkey<OptimizeAssetGathererBorrowBuffer>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        /// <summary>
        /// Prefix to handle ConcurrentStack operations without locks.
        /// </summary>
        private static bool Prefix(AssetGatherer __instance, ref byte[] __result)
        {
            if (!Enabled)
                return true;

            try
            {
                var bufferSizeField = typeof(AssetGatherer).GetField("BufferSize", BindingFlags.Public | BindingFlags.Instance);
                if (bufferSizeField == null)
                    return true;

                var bufferSize = (int)bufferSizeField.GetValue(__instance)!;

                // Use our ConcurrentStack from ConditionalWeakTable
                var concurrentStack = OptimizeAssetGatherer.GetConcurrentStack(__instance);

                // Try to pop a buffer of the right size
                var tempStack = new Stack<byte[]>();
                while (concurrentStack.TryPop(out byte[]? buffer))
                {
                    if (buffer.Length == bufferSize)
                    {
                        // Put back any buffers we popped but didn't use
                        while (tempStack.Count > 0)
                        {
                            concurrentStack.Push(tempStack.Pop());
                        }
                        __result = buffer;
                        PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("AssetGatherer.BufferReused");
                        return false; // Skip original method
                    }
                    tempStack.Push(buffer);
                }

                // Put back any buffers we popped
                while (tempStack.Count > 0)
                {
                    concurrentStack.Push(tempStack.Pop());
                }

                // No buffer available, allocate new one
                __result = new byte[bufferSize];
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("AssetGatherer.BufferAllocated");
                return false; // Skip original method
            }
            catch
            {
                // Fall back to original if anything fails
            }

            return true; // Run original method
        }
    }

    /// <summary>
    /// Patches ReturnBuffer to use ConcurrentStack operations.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeAssetGatherer))]
    [HarmonyPatch(typeof(AssetGatherer), "ReturnBuffer")]
    internal sealed class OptimizeAssetGathererReturnBuffer : PerformanceOptimizationsMonkey<OptimizeAssetGathererReturnBuffer>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        /// <summary>
        /// Prefix to handle ConcurrentStack operations without locks.
        /// </summary>
        private static bool Prefix(AssetGatherer __instance, byte[] buffer)
        {
            if (!Enabled)
                return true;

            try
            {
                var bufferSizeField = typeof(AssetGatherer).GetField("BufferSize", BindingFlags.Public | BindingFlags.Instance);
                if (bufferSizeField == null)
                    return true;

                var bufferSize = (int)bufferSizeField.GetValue(__instance)!;

                // Use our ConcurrentStack from ConditionalWeakTable
                if (buffer.Length == bufferSize)
                {
                    var concurrentStack = OptimizeAssetGatherer.GetConcurrentStack(__instance);
                    concurrentStack.Push(buffer);
                    PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("AssetGatherer.BufferReturned");
                    return false; // Skip original method
                }
            }
            catch
            {
                // Fall back to original if anything fails
            }

            return true; // Run original method
        }
    }
}

