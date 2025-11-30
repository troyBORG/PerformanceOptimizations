using HarmonyLib;
using SkyFrost.Base;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Optimizes RecordCache by replacing Dictionary with ConcurrentDictionary to eliminate lock contention.
    /// Uses a transpiler approach to replace field initialization.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeRecordCache))]
    internal sealed class OptimizeRecordCache : PerformanceOptimizationsMonkey<OptimizeRecordCache>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        /// <summary>
        /// Patches the cached field initialization to use ConcurrentDictionary.
        /// This is done via a postfix on the constructor.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(RecordCache<>), MethodType.Constructor)]
        private static void PostfixConstructor(object __instance)
        {
            if (!Enabled)
                return;

            try
            {
                var instanceType = __instance.GetType();
                if (!instanceType.IsGenericType || instanceType.GetGenericTypeDefinition() != typeof(RecordCache<>))
                    return;

                var cachedField = instanceType.GetField("cached", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cachedField == null)
                    return;

                var currentValue = cachedField.GetValue(__instance);
                
                // If it's already a ConcurrentDictionary, skip
                if (currentValue != null && currentValue.GetType().IsGenericType && 
                    currentValue.GetType().GetGenericTypeDefinition() == typeof(ConcurrentDictionary<,>))
                    return;

                // Get the generic type arguments
                var recordType = instanceType.GetGenericArguments()[0];
                var recordIdType = typeof(RecordId);

                // Create ConcurrentDictionary type
                var concurrentDictType = typeof(ConcurrentDictionary<,>).MakeGenericType(recordIdType, recordType);
                var newDict = Activator.CreateInstance(concurrentDictType);

                // Copy existing values if any (shouldn't be any in constructor, but just in case)
                if (currentValue != null)
                {
                    var addMethod = concurrentDictType.GetMethod("TryAdd");
                    // Use reflection to iterate over the dictionary
                    var getEnumeratorMethod = currentValue.GetType().GetMethod("GetEnumerator");
                    if (getEnumeratorMethod != null)
                    {
                        var enumerator = getEnumeratorMethod.Invoke(currentValue, null);
                        var moveNextMethod = enumerator!.GetType().GetMethod("MoveNext");
                        var currentProperty = enumerator.GetType().GetProperty("Current");
                        
                        while ((bool)moveNextMethod!.Invoke(enumerator, null)!)
                        {
                            var current = currentProperty!.GetValue(enumerator);
                            var keyProperty = current!.GetType().GetProperty("Key");
                            var valueProperty = current.GetType().GetProperty("Value");
                            var key = keyProperty!.GetValue(current);
                            var value = valueProperty!.GetValue(current);
                            addMethod!.Invoke(newDict, new[] { key, value });
                        }
                    }
                }

                // Replace the field
                cachedField.SetValue(__instance, newDict);
                
                // Increment metric
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("RecordCache.Optimized");
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to optimize RecordCache: {ex}");
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("RecordCache.Failed");
            }
        }
    }
}

