# Building the Performance Optimizations Mod for RML

## Prerequisites

1. **.NET 10.0 SDK** - Install from https://dotnet.microsoft.com/download
2. **Visual Studio 2022** (optional, but recommended)
3. **ResoniteModLoader (RML)** - Must be installed in your Resonite installation
4. **Resonite Installation** - For automatic DLL copying

## Building

### Option 1: Command Line

```powershell
cd "T:\git\Resonite Mods\PerformanceOptimizations"
dotnet build PerformanceOptimizations.csproj -c Release
```

The DLL will be automatically copied to `Resonite/rml_mods/` if the Resonite path is detected.

### Option 2: Visual Studio

1. Open `PerformanceOptimizations.sln`
2. Set configuration to `Release`
3. Build the solution (F6 or Build → Build Solution)

## Installing

After building, the DLL will be in:
- `bin/Release/net10.0/PerformanceOptimizations.dll`

If `CopyToLibraries` is enabled (default), it will also be automatically copied to:
- `G:\SteamLibrary\steamapps\common\Resonite\rml_mods\PerformanceOptimizations.dll`

### Manual Installation

1. Copy the DLL to your Resonite RML mods folder:
   - `G:\SteamLibrary\steamapps\common\Resonite\rml_mods\PerformanceOptimizations.dll`

2. Launch Resonite - RML will automatically load the mod

## Verifying the Mod Works

1. Launch Resonite
2. Check RML logs/console for mod loading messages
3. Performance improvements should be noticeable in:
   - Multi-user worlds
   - Asset-heavy scenarios
   - High network activity

## Troubleshooting

### Mod doesn't load

- Check that the DLL is in `rml_mods` folder (not `MonkeyLoader/Mods`)
- Verify RML is installed and working
- Check Resonite logs for errors

### Performance improvements not noticeable

- The improvements are most noticeable under load
- Try testing with multiple users in a world
- Use performance profiling tools to measure actual improvements

### Build errors

- Ensure you have .NET 10.0 SDK installed
- Verify Resonite path is correct in `Directory.Build.props`
- Check that all DLL references exist in your Resonite installation

## Development

### Testing Changes

1. Make your changes to the patch files
2. Rebuild the project: `dotnet build -c Release`
3. The DLL will be automatically updated in `rml_mods` folder
4. Restart Resonite to load the updated mod

### Adding New Optimizations

1. Create a new class inheriting from `PerformanceOptimizationsMonkey<T>`
2. Add `[HarmonyPatch]` attributes to target methods
3. Implement Prefix/Postfix/Transpiler patches as needed
4. Add the class to the project

## How It Works

This mod uses Harmony patches to:
- Replace `Dictionary` with `ConcurrentDictionary` in RecordCache (eliminates locks)
- Replace `Stack` with `ConcurrentStack` in AssetGatherer (eliminates locks)
- All patches are applied at runtime using Harmony - no source code modification needed

