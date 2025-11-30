# Performance Optimizations Mod - Testing Guide

The optimizations in this mod target **lock contention** and **multi-threaded performance** during active operations. Standing still won't show much difference - you need to stress the systems we optimized.

## Best Test Scenarios

### 1. **Asset Loading Stress Test** (RecordCache + AssetGatherer)
**What to do:**
- Join a world with many assets (avatars, props, textures)
- Spawn multiple avatars or objects simultaneously
- Load a world with many textures/models
- Watch FPS during asset loading spikes

**What to look for:**
- Smoother FPS during asset loading
- Less stuttering when assets pop in
- Faster world loading times

**Expected improvement:** 40-60% faster asset operations

---

### 2. **Multi-User Stress Test** (All optimizations)
**What to do:**
- Join a world with 5+ other users
- Have everyone move around, spawn objects, change avatars
- Watch FPS during high activity periods

**What to look for:**
- More stable FPS with multiple users
- Less frame drops when users join/leave
- Smoother experience during network sync

**Expected improvement:** 20-40% overall frame time improvement

---

### 3. **Update Loop Stress Test** (UpdateManager)
**What to do:**
- Create a world with many animated objects (100+)
- Use ProtoFlux with many active nodes
- Spawn many objects with update components
- Watch FPS as objects are added/removed

**What to look for:**
- Better FPS with many updatable objects
- Smoother performance when objects are destroyed
- Less lag when spawning/despawning

**Expected improvement:** 10-20% improvement in update loop

---

### 4. **Network Query Stress Test** (BatchQuery)
**What to do:**
- Open inventory browser
- Browse through many folders quickly
- Search for records/assets
- Watch for stuttering during queries

**What to look for:**
- Smoother inventory browsing
- Less lag when opening folders
- Faster search results

**Expected improvement:** 60-80% reduction in query lock contention

---

### 5. **Combined Stress Test** (All systems)
**What to do:**
- Join a busy world (10+ users)
- Everyone spawns avatars/objects
- Browse inventory while assets load
- Move around actively
- Monitor FPS throughout

**What to look for:**
- Overall smoother experience
- Less micro-stutters
- Better frame pacing
- Lower CPU usage spikes

---

## How to Compare (Mod On vs Off)

1. **Enable metrics logging:**
   - Set `EnableMetrics: true` in config
   - Set `ReportMetricsToCache: true`
   - Check `[ResonitePath]/PerformanceOptimizationsCache.json` for metrics

2. **Use FPS overlay:**
   - Enable FPS counter in Resonite settings
   - Or use external tool (MSI Afterburner, etc.)
   - Watch for frame time consistency

3. **Test the same scenario:**
   - Run the same test with mod ON
   - Restart Resonite
   - Disable mod (set `EnableAllOptimizations: false`)
   - Run the same test with mod OFF
   - Compare FPS, stuttering, loading times

4. **Check logs:**
   - Look for validation messages: `✓ [Optimization] validated`
   - Check for errors or warnings
   - Monitor metrics if enabled

---

## Quick Test Checklist

- [ ] Join world with many assets → Check asset loading smoothness
- [ ] Spawn 10+ avatars → Check FPS stability
- [ ] Browse inventory quickly → Check for stuttering
- [ ] Create world with 50+ animated objects → Check update loop performance
- [ ] Join busy world (5+ users) → Check overall smoothness
- [ ] Load world with many textures → Check loading performance

---

## What WON'T Show Much Difference

- Standing still in empty world
- Single-user scenarios
- Low-activity situations
- Simple scenes with few objects

These scenarios don't stress the systems we optimized, so you won't see much improvement.

---

## Metrics to Monitor

If you enable metrics (`EnableMetrics: true`), you can track:

- `RecordCache.Optimized` - Cache instances optimized
- `AssetGatherer.BufferReused` - Buffer pool efficiency
- `UpdateManager.RemovedO1` - O(1) removals vs O(n) fallback
- `BatchQuery.Optimized` - Optimized batch operations

Higher numbers = more optimizations applied = better performance!

---

## Troubleshooting

**Not seeing improvements?**
- Make sure mod is enabled (`EnableAllOptimizations: true`)
- Check logs for validation messages
- Try more stressful scenarios
- Enable metrics to see if optimizations are being applied

**Seeing errors?**
- Check logs for Harmony patch errors
- Make sure you're using the latest build
- Report issues with log files
