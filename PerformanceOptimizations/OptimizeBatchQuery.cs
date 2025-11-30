using HarmonyLib;
using SkyFrost.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Optimizes BatchQuery by reducing lock scope - copy data outside lock, process outside lock.
    /// Uses a prefix patch to intercept SendBatch and optimize the lock scope.
    /// </summary>
    [HarmonyPatchCategory(nameof(OptimizeBatchQuery))]
    internal sealed class OptimizeBatchQuery : PerformanceOptimizationsMonkey<OptimizeBatchQuery>
    {
        public override IEnumerable<string> Authors { get; } = ["PerformanceOptimizations"];

        /// <summary>
        /// Prefix patch to optimize SendBatch by reducing lock scope.
        /// This intercepts the method and implements the optimized version.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(BatchQuery<,>), "SendBatch")]
        private static bool PrefixSendBatch(
            object __instance,
            ref Task __result)
        {
            if (!Enabled)
                return true; // Run original method

            try
            {
                var instanceType = __instance.GetType();
                if (!instanceType.IsGenericType || instanceType.GetGenericTypeDefinition() != typeof(BatchQuery<,>))
                    return true;

                // Get required fields and methods via reflection
                var queueField = instanceType.GetField("queue", BindingFlags.NonPublic | BindingFlags.Instance);
                var lockField = instanceType.GetField("_lock", BindingFlags.NonPublic | BindingFlags.Instance);
                var maxBatchSizeField = instanceType.GetField("MaxBatchSize", BindingFlags.Public | BindingFlags.Instance);
                var delaySecondsField = instanceType.GetField("DelaySeconds", BindingFlags.Public | BindingFlags.Instance);
                var immediateDispatchField = instanceType.GetField("immediateDispatch", BindingFlags.NonPublic | BindingFlags.Instance);
                var dispatchScheduledField = instanceType.GetField("dispatchScheduled", BindingFlags.NonPublic | BindingFlags.Instance);
                var runBatchMethod = instanceType.GetMethod("RunBatch", BindingFlags.NonPublic | BindingFlags.Instance);

                if (queueField == null || lockField == null || maxBatchSizeField == null || 
                    delaySecondsField == null || immediateDispatchField == null || 
                    dispatchScheduledField == null || runBatchMethod == null)
                {
                    return true; // Can't optimize, use original
                }

                var queue = queueField.GetValue(__instance);
                var lockObj = lockField.GetValue(__instance);
                var maxBatchSize = (int)maxBatchSizeField.GetValue(__instance)!;
                var delaySeconds = (int)delaySecondsField.GetValue(__instance)!;
                var immediateDispatch = immediateDispatchField.GetValue(__instance);

                // Create optimized async method
                __result = OptimizedSendBatch(
                    __instance,
                    queue,
                    lockObj,
                    maxBatchSize,
                    delaySeconds,
                    immediateDispatch,
                    dispatchScheduledField,
                    runBatchMethod,
                    instanceType);

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Failed to optimize BatchQuery.SendBatch: {ex}");
                return true; // Fall back to original
            }
        }

        private static async Task OptimizedSendBatch(
            object instance,
            object? queue,
            object? lockObj,
            int maxBatchSize,
            int delaySeconds,
            object? immediateDispatch,
            FieldInfo dispatchScheduledField,
            MethodInfo runBatchMethod,
            Type instanceType)
        {
            try
            {
                // Wait for immediate dispatch or delay
                if (immediateDispatch != null)
                {
                    var taskProperty = immediateDispatch.GetType().GetProperty("Task");
                    if (taskProperty != null)
                    {
                        var immediateTask = (Task)taskProperty.GetValue(immediateDispatch)!;
                        await Task.WhenAny(immediateTask, Task.Delay(TimeSpan.FromSeconds(delaySeconds))).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
                }

                // Copy keys outside lock to minimize lock duration
                List<object> queriesToProcess;
                lock (lockObj!)
                {
                    var countProperty = queue!.GetType().GetProperty("Count");
                    if (countProperty == null)
                        return;

                    var queueCount = (int)countProperty.GetValue(queue)!;
                    if (queueCount == 0)
                    {
                        dispatchScheduledField.SetValue(instance, false);
                        return;
                    }

                    // Copy up to MaxBatchSize queries
                    var keysProperty = queue.GetType().GetProperty("Keys");
                    if (keysProperty == null)
                        return;

                    var keys = keysProperty.GetValue(queue);
                    queriesToProcess = new List<object>();

                    // Use reflection to iterate keys
                    var enumerableType = typeof(System.Collections.IEnumerable);
                    if (enumerableType.IsAssignableFrom(keys!.GetType()))
                    {
                        var enumerator = ((System.Collections.IEnumerable)keys).GetEnumerator();
                        int count = 0;
                        while (enumerator.MoveNext() && count < maxBatchSize)
                        {
                            queriesToProcess.Add(enumerator.Current!);
                            count++;
                        }
                    }
                }

                // Process outside lock
                var queryResultType = instanceType.GetNestedType("QueryResult", BindingFlags.NonPublic | BindingFlags.Public);
                if (queryResultType == null)
                    return;

                var toSendList = new List<object>();
                foreach (var query in queriesToProcess)
                {
                    var queryResult = Activator.CreateInstance(queryResultType, query);
                    if (queryResult != null)
                        toSendList.Add(queryResult);
                }

                Exception? exception = null;
                try
                {
                    if (toSendList.Count > 0)
                    {
                        // Call RunBatch
                        var runBatchTask = (Task)runBatchMethod.Invoke(instance, new[] { toSendList })!;
                        await runBatchTask.ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    exception = ex;
                    PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Exception in optimized RunBatch: {ex}");
                }

                // Update results while holding lock (minimal time)
                lock (lockObj!)
                {
                    foreach (var queryResult in toSendList)
                    {
                        var queryProperty = queryResult.GetType().GetProperty("query");
                        var resultProperty = queryResult.GetType().GetProperty("result");
                        if (queryProperty == null || resultProperty == null)
                            continue;

                        var query = queryProperty.GetValue(queryResult);
                        var result = resultProperty.GetValue(queryResult);

                        // Try to get the TaskCompletionSource from queue
                        var tryGetValueMethod = queue!.GetType().GetMethod("TryGetValue");
                        if (tryGetValueMethod != null)
                        {
                            var parameters = new object[] { query!, null! };
                            if ((bool)tryGetValueMethod.Invoke(queue, parameters)!)
                            {
                                var tcs = parameters[1];
                                var setResultMethod = tcs.GetType().GetMethod("SetResult");
                                var setExceptionMethod = tcs.GetType().GetMethod("SetException", new[] { typeof(Exception) });

                                if (exception != null && setExceptionMethod != null)
                                {
                                    setExceptionMethod.Invoke(tcs, new object[] { exception });
                                }
                                else if (setResultMethod != null)
                                {
                                    setResultMethod.Invoke(tcs, new[] { result });
                                }

                                // Remove from queue
                                var removeMethod = queue.GetType().GetMethod("Remove");
                                removeMethod?.Invoke(queue, new[] { query });
                            }
                        }
                    }

                    // Check if more batches needed
                    var countProperty = queue!.GetType().GetProperty("Count");
                    if (countProperty != null)
                    {
                        var remainingCount = (int)countProperty.GetValue(queue)!;
                        if (remainingCount > 0)
                        {
                            if (remainingCount >= maxBatchSize)
                            {
                                // Trigger immediate dispatch
                                if (immediateDispatch != null)
                                {
                                    var trySetResultMethod = immediateDispatch.GetType().GetMethod("TrySetResult", new[] { typeof(bool) });
                                    trySetResultMethod?.Invoke(immediateDispatch, new object[] { true });
                                }
                            }

                            // Schedule next batch
                            var sendBatchMethod = instanceType.GetMethod("SendBatch", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (sendBatchMethod != null)
                            {
                                Task.Run(() => sendBatchMethod.Invoke(instance, null));
                            }
                        }
                        else
                        {
                            dispatchScheduledField.SetValue(instance, false);
                        }
                    }
                }

                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.IncrementMetric("BatchQuery.Optimized");
            }
            catch (Exception ex)
            {
                PerformanceOptimizationsMod.PerformanceOptimizationsModHelper.LogWarn($"Error in optimized SendBatch: {ex}");
            }
        }
    }
}

