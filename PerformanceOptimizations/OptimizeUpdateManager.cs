using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Optimizes UpdateManager by adding bucket tracking for O(1) removals.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeUpdateManager))]
    internal sealed class OptimizeUpdateManager : PerformanceOptimizationsMonkey<OptimizeUpdateManager>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        private static readonly FieldInfo? _updatableToBucketField = CreateTrackingField();

        /// <summary>
        /// Creates a tracking dictionary field on UpdateManager instances.
        /// </summary>
        private static FieldInfo? CreateTrackingField()
        {
            try
            {
                var updateManagerType = Type.GetType("FrooxEngine.UpdateManager, FrooxEngine");
                if (updateManagerType == null)
                    return null;

                // Check if field already exists (in case of multiple patches)
                var existingField = updateManagerType.GetField("updatableToBucket", BindingFlags.NonPublic | BindingFlags.Instance);
                if (existingField != null)
                    return existingField;

                // We can't add fields at runtime easily, so we'll use a ConditionalWeakTable
                // to track updatables to buckets externally
                return null; // Will use ConditionalWeakTable instead
            }
            catch
            {
                return null;
            }
        }

        // Use ConditionalWeakTable to track updatables to buckets without modifying the class
        private static readonly ConditionalWeakTable<object, Dictionary<object, int>> _bucketTracking = new();

        /// <summary>
        /// Patches RegisterForUpdates to track which bucket each updatable belongs to.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("FrooxEngine.UpdateManager", "RegisterForUpdates")]
        private static void PostfixRegisterForUpdates(object __instance, object updatable)
        {
            if (!Enabled || updatable == null)
                return;

            try
            {
                // Get the updatable's UpdateOrder to determine bucket
                var updatableType = updatable.GetType();
                var updateOrderProperty = updatableType.GetProperty("UpdateOrder");
                if (updateOrderProperty == null)
                    return;

                var bucket = (int)updateOrderProperty.GetValue(updatable)!;

                // Track this updatable's bucket
                if (!_bucketTracking.TryGetValue(__instance, out var tracking))
                {
                    tracking = new Dictionary<object, int>();
                    _bucketTracking.Add(__instance, tracking);
                }
                tracking[updatable] = bucket;

                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("UpdateManager.Tracked");
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to track updatable bucket: {ex}");
            }
        }

        /// <summary>
        /// Patches RemoveFromUpdateBucket to use tracking for O(1) lookup.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch("FrooxEngine.UpdateManager", "RemoveFromUpdateBucket")]
        private static bool PrefixRemoveFromUpdateBucket(
            object __instance,
            object updatable,
            ref bool __result)
        {
            if (!Enabled || updatable == null)
                return true; // Run original method

            try
            {
                if (!_bucketTracking.TryGetValue(__instance, out var tracking))
                    return true; // No tracking yet, use original method

                // Get the updatable's UpdateOrder
                var updatableType = updatable.GetType();
                var updateOrderProperty = updatableType.GetProperty("UpdateOrder");
                if (updateOrderProperty == null)
                    return true;

                var expectedOrder = (int)updateOrderProperty.GetValue(updatable)!;

                // Try to get the actual bucket from tracking
                if (!tracking.TryGetValue(updatable, out var actualBucket))
                {
                    // Not tracked, fall back to original method
                    return true;
                }

                // Get the toUpdate dictionary
                var instanceType = __instance.GetType();
                var toUpdateField = instanceType.GetField("toUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                if (toUpdateField == null)
                    return true;

                var toUpdate = toUpdateField.GetValue(__instance);
                if (toUpdate == null)
                    return true;

                // Get the bucket list
                var getUpdateBucketMethod = instanceType.GetMethod("GetUpdateBucket", BindingFlags.NonPublic | BindingFlags.Instance);
                if (getUpdateBucketMethod == null)
                    return true;

                var bucketList = getUpdateBucketMethod.Invoke(__instance, new object[] { actualBucket, false });
                if (bucketList == null)
                {
                    // Bucket doesn't exist, remove from tracking
                    tracking.Remove(updatable);
                    __result = false;
                    return false; // Skip original method
                }

                // Try to remove from the tracked bucket
                var removeMethod = bucketList.GetType().GetMethod("Remove");
                if (removeMethod == null)
                    return true;

                var removed = (bool)removeMethod.Invoke(bucketList, new[] { updatable })!;
                if (removed)
                {
                    // Update tracking
                    tracking.Remove(updatable);
                    PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("UpdateManager.RemovedO1");

                    // Check if bucket is empty and remove it
                    var countProperty = bucketList.GetType().GetProperty("Count");
                    if (countProperty != null)
                    {
                        var count = (int)countProperty.GetValue(bucketList)!;
                        if (count == 0)
                        {
                            var removeDictMethod = toUpdate.GetType().GetMethod("Remove");
                            removeDictMethod?.Invoke(toUpdate, new[] { (object)actualBucket });
                        }
                    }

                    __result = true;
                    return false; // Skip original method
                }

                // Not found in tracked bucket, fall back to original
                return true;
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to optimize RemoveFromUpdateBucket: {ex}");
                return true; // Fall back to original on error
            }
        }

        /// <summary>
        /// Patches UpdateBucketChanged to update tracking when bucket changes.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch("FrooxEngine.UpdateManager", "UpdateBucketChanged")]
        private static void PostfixUpdateBucketChanged(object __instance, object updatable)
        {
            if (!Enabled || updatable == null)
                return;

            try
            {
                if (!_bucketTracking.TryGetValue(__instance, out var tracking))
                    return;

                // Update the bucket tracking
                var updatableType = updatable.GetType();
                var updateOrderProperty = updatableType.GetProperty("UpdateOrder");
                if (updateOrderProperty == null)
                    return;

                var newBucket = (int)updateOrderProperty.GetValue(updatable)!;
                tracking[updatable] = newBucket;

                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("UpdateManager.BucketChanged");
            }
            catch
            {
                // Ignore errors in tracking update
            }
        }
    }
}

