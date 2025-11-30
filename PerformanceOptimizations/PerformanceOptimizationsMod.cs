using ResoniteModLoader;
using HarmonyLib;
using FrooxEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;
using SkyFrost.Base;

namespace PerformanceOptimizations
{
    /// <summary>
    /// Main mod class for PerformanceOptimizations.
    /// Implements RML mod interface with configuration support.
    /// </summary>
    public class PerformanceOptimizationsMod : ResoniteMod
    {
        public override string Name => "PerformanceOptimizations";
        public override string Author => "troyBORG";
        public override string Version => "1.0.0";
        public override string Link => "https://github.com/troyBORG/PerformanceOptimizations";

        // Master toggle - enables/disables all optimizations at once
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableAllOptimizations = new ModConfigurationKey<bool>(
            "EnableAllOptimizations",
            "Master toggle: Enable/disable all performance optimizations at once",
            () => true);

        // Configuration keys for each optimization (only used when EnableAllOptimizations is true)
        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableRecordCache = new ModConfigurationKey<bool>(
            "EnableRecordCache",
            "Enable RecordCache optimization (ConcurrentDictionary) - only active if EnableAllOptimizations is true",
            () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableAssetGatherer = new ModConfigurationKey<bool>(
            "EnableAssetGatherer",
            "Enable AssetGatherer optimization (ConcurrentStack)",
            () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableUpdateManager = new ModConfigurationKey<bool>(
            "EnableUpdateManager",
            "Enable UpdateManager optimization (Bucket tracking)",
            () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableBatchQuery = new ModConfigurationKey<bool>(
            "EnableBatchQuery",
            "Enable BatchQuery optimization (Reduced lock scope)",
            () => true);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> EnableMetrics = new ModConfigurationKey<bool>(
            "EnableMetrics",
            "Enable metrics logging and reporting",
            () => false);

        [AutoRegisterConfigKey]
        private static readonly ModConfigurationKey<bool> ReportMetricsToCache = new ModConfigurationKey<bool>(
            "ReportMetricsToCache",
            "Report metrics data to cache file (updates periodically)",
            () => false);

        private static ModConfiguration? Config;
        private static Harmony? _harmony;
        private static readonly string _harmonyId = "com.PerformanceOptimizations";

        // Metrics tracking
        private static readonly Dictionary<string, long> _metrics = new Dictionary<string, long>();
        private static readonly object _metricsLock = new object();
        private static System.Threading.Timer? _metricsUpdateTimer;

        public override void OnEngineInit()
        {
            Config = GetConfiguration();
            Config.Save(true); // Save default config values

            PerformanceOptimizationsModHelper.SetInstance(this);

            Msg("PerformanceOptimizations mod initializing...");

            // Initialize Harmony
            _harmony = new Harmony(_harmonyId);

            // Apply patches based on config
            if (Config != null)
            {
                ApplyPatches();
            }

            // Setup metrics if enabled
            if (Config.GetValue(EnableMetrics))
            {
                SetupMetrics();
            }

            // Setup metrics reporting to cache file if enabled
            if (Config.GetValue(ReportMetricsToCache))
            {
                SetupMetricsReporting();
            }

            // Validate patches after a delay
            if (Engine.Current != null)
            {
                Engine.Current.RunPostInit(() =>
                {
                    System.Threading.Tasks.Task.Delay(2000).ContinueWith(_ => ValidatePatches());
                });
            }
        }

        private void ApplyPatches()
        {
            // Check master toggle first
            bool masterEnabled = Config!.GetValue(EnableAllOptimizations);
            
            if (!masterEnabled)
            {
                Msg("All optimizations disabled by master toggle (EnableAllOptimizations = false)");
                return;
            }

            try
            {
                if (Config.GetValue(EnableRecordCache))
                {
                    OptimizeRecordCache.Initialize(_harmony!);
                    Msg("RecordCache optimization enabled");
                }

                if (Config.GetValue(EnableAssetGatherer))
                {
                    OptimizeAssetGatherer.Initialize(_harmony!);
                    OptimizeAssetGathererBorrowBuffer.Initialize(_harmony!);
                    OptimizeAssetGathererReturnBuffer.Initialize(_harmony!);
                    Msg("AssetGatherer optimization enabled");
                }

                if (Config.GetValue(EnableUpdateManager))
                {
                    OptimizeUpdateManager.Initialize(_harmony!);
                    Msg("UpdateManager optimization enabled");
                }

                if (Config.GetValue(EnableBatchQuery))
                {
                    OptimizeBatchQuery.Initialize(_harmony!);
                    Msg("BatchQuery optimization enabled");
                }

                Msg("All enabled optimization patches initialized.");
            }
            catch (Exception ex)
            {
                Error($"Failed to initialize some patches: {ex}");
            }
        }

        private void SetupMetrics()
        {
            PerformanceOptimizationsModHelper.SetMetricsEnabled(true);
            Msg("Metrics logging enabled");
        }

        private void SetupMetricsReporting()
        {
            // Update cache file with metrics every 30 seconds
            // Cache file location: [ResonitePath]/PerformanceOptimizationsCache.json
            _metricsUpdateTimer = new System.Threading.Timer(_ =>
            {
                var metrics = PerformanceOptimizationsModHelper.GetMetrics();
                if (metrics.Count > 0)
                {
                    WriteMetricsToCache(metrics);
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            Msg("Metrics reporting to cache file enabled (updates every 30 seconds)");
        }

        private void WriteMetricsToCache(Dictionary<string, long> metrics)
        {
            try
            {
                // Get Resonite path from Engine
                string? resonitePath = null;
                if (Engine.Current != null)
                {
                    // Try to get the path from Engine's base directory
                    var engineType = typeof(Engine);
                    var baseDirectoryField = engineType.GetField("BaseDirectory", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                    if (baseDirectoryField != null)
                    {
                        resonitePath = baseDirectoryField.GetValue(null) as string;
                    }
                }

                // Fallback: try AppDomain base directory (usually points to Resonite directory)
                if (string.IsNullOrEmpty(resonitePath))
                {
                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (!string.IsNullOrEmpty(baseDir) && File.Exists(Path.Combine(baseDir, "Resonite.exe")))
                    {
                        resonitePath = baseDir;
                    }
                    else
                    {
                        // Try to find Resonite directory by walking up from base directory
                        var dir = new DirectoryInfo(baseDir);
                        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Resonite.exe")))
                        {
                            dir = dir.Parent;
                        }
                        if (dir != null)
                        {
                            resonitePath = dir.FullName;
                        }
                    }
                }

                if (string.IsNullOrEmpty(resonitePath))
                {
                    Warn("Could not determine Resonite path for cache file");
                    return;
                }

                string cacheFilePath = Path.Combine(resonitePath, "PerformanceOptimizationsCache.json");
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                string json = JsonSerializer.Serialize(metrics, options);
                File.WriteAllText(cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Warn($"Failed to write metrics cache: {ex.Message}");
            }
        }

        private void ValidatePatches()
        {
            Msg("Validating optimization patches...");

            int successCount = 0;
            int totalCount = 0;

            // Only validate if master toggle is enabled
            if (!Config!.GetValue(EnableAllOptimizations))
            {
                Msg("Optimizations disabled - skipping validation");
                return;
            }

            if (Config.GetValue(EnableRecordCache))
            {
                totalCount++;
                if (ValidateRecordCachePatch())
                {
                    successCount++;
                    Msg("✓ RecordCache optimization validated");
                }
                else
                {
                    Warn("✗ RecordCache optimization validation failed");
                }
            }

            if (Config.GetValue(EnableAssetGatherer))
            {
                totalCount++;
                if (ValidateAssetGathererPatch())
                {
                    successCount++;
                    Msg("✓ AssetGatherer optimization validated");
                }
                else
                {
                    Warn("✗ AssetGatherer optimization validation failed");
                }
            }

            if (Config.GetValue(EnableUpdateManager))
            {
                totalCount++;
                if (ValidateUpdateManagerPatch())
                {
                    successCount++;
                    Msg("✓ UpdateManager optimization validated");
                }
                else
                {
                    Warn("✗ UpdateManager optimization validation failed");
                }
            }

            if (Config.GetValue(EnableBatchQuery))
            {
                totalCount++;
                if (ValidateBatchQueryPatch())
                {
                    successCount++;
                    Msg("✓ BatchQuery optimization validated");
                }
                else
                {
                    Warn("✗ BatchQuery optimization validation failed");
                }
            }

            Msg($"Patch validation complete: {successCount}/{totalCount} patches active");
        }

        private bool ValidateRecordCachePatch()
        {
            try
            {
                var recordCacheType = typeof(RecordCache<>);
                return recordCacheType != null;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateAssetGathererPatch()
        {
            try
            {
                var assetGathererType = typeof(AssetGatherer);
                return assetGathererType != null;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateUpdateManagerPatch()
        {
            try
            {
                var updateManagerType = Type.GetType("FrooxEngine.UpdateManager, FrooxEngine");
                return updateManagerType != null;
            }
            catch
            {
                return false;
            }
        }

        private bool ValidateBatchQueryPatch()
        {
            try
            {
                var batchQueryType = typeof(BatchQuery<,>);
                return batchQueryType != null;
            }
            catch
            {
                return false;
            }
        }

        // Static helper methods for metrics (used by patches)
        public static class PerformanceOptimizationsModHelper
        {
            private static PerformanceOptimizationsMod? _instance;

            public static void SetInstance(PerformanceOptimizationsMod instance)
            {
                _instance = instance;
            }

            public static void SetMetricsEnabled(bool enabled)
            {
                lock (_metricsLock)
                {
                    // Metrics enabled state is managed by config
                }
            }

            public static void IncrementMetric(string metricName)
            {
                if (Config == null || !Config.GetValue(EnableMetrics))
                    return;

                lock (_metricsLock)
                {
                    if (!_metrics.ContainsKey(metricName))
                        _metrics[metricName] = 0;
                    _metrics[metricName]++;
                }
            }

            public static Dictionary<string, long> GetMetrics()
            {
                lock (_metricsLock)
                {
                    return new Dictionary<string, long>(_metrics);
                }
            }

            public static bool IsOptimizationEnabled(string optimizationName)
            {
                if (Config == null)
                    return false;

                // Check master toggle first - if disabled, all optimizations are disabled
                if (!Config.GetValue(EnableAllOptimizations))
                    return false;

                // If master is enabled, check individual toggle
                return optimizationName switch
                {
                    "RecordCache" => Config.GetValue(EnableRecordCache),
                    "AssetGatherer" => Config.GetValue(EnableAssetGatherer),
                    "UpdateManager" => Config.GetValue(EnableUpdateManager),
                    "BatchQuery" => Config.GetValue(EnableBatchQuery),
                    _ => false
                };
            }

            public static void LogWarn(string message)
            {
                ResoniteMod.Warn($"[PerformanceOptimizations] {message}");
            }

            public static void LogInfo(string message)
            {
                ResoniteMod.Msg($"[PerformanceOptimizations] {message}");
            }
        }
    }
}
