# FL MODS

MelonLoader mod pack for **Flashing Lights** focused on MDT workflows, dispatch/audio tools, bodycam overlay, NPC interaction, asset loading, and performance-conscious shared utilities.

Default local game root used during development:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights
```

Adjust build commands if your install path is different.

## Included Mods

- `FLModkitBindings` - one centralized ModKit page for all shared hotkeys.
- `GameEventLogger` - essential event, panic, weapon, and dispatch hooks.
- `GrammarPoliceMod` - dispatch/radio commands with speech support when Windows speech dependencies are available, plus button-only fallback.
- `MDTMod` - mobile data terminal with searchable records, reports, citations, charges, court handling, and subject lookup.
- `MDTShared` - shared MDT record storage and data helpers.
- `NPCAI` - nearby NPC-gated interaction UI with cached session identities.
- `BodyCamOverlay` - bodycam overlay with configurable signal timing, manual activation, weapon-draw activation, and rapid emergency key activation.
- `AssetLoader` - loads user asset bundles from the Flashing Lights FLMods asset folder.
- `BackgroundRadio` - background/offline radio playback and panic-aware pause/resume behavior.
- `FLWatchdog` - small diagnostics/watchdog support for repeated errors and feature health.
- `Shared` - linked utilities used across mods, including keybind storage, performance settings, and shared runtime audio.

## Central Keybindings

Hotkeys are managed from the `FLModkitBindings` ModKit page. Individual mods no longer expose duplicate keybind fields in their own config pages or in-game UI panels.

The central bindings mod writes lightweight `.keybinds` files next to the deployed mod DLLs. Runtime mods reload those files about once per second, so changes made in ModKit propagate without restarting the game in normal cases.

Currently centralized:

- Asset Loader reload
- Background Radio toggle, navigation, select, and stop
- Bodycam toggle and emergency trigger
- Grammar Police PTT, radio UI, radio navigation/select, and panic trigger
- MDT toggle
- NPC AI toggle and send

Use `Escape` while rebinding to cancel the pending binding.

## Performance Mode

Shared performance settings are stored at:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\UserData\FLMods\PerformanceMode.json
```

Default profile:

```json
{
  "enabled": false,
  "disableVoiceRecognition": true,
  "disableAssetAutoload": true,
  "disableBodycamWeaponPolling": true,
  "disableRadioStreaming": true,
  "disableNpcAiScans": true
}
```

Set `"enabled": true` to suppress CPU-sensitive background features at once. Manual controls remain available where practical.

Recent optimization work includes:

- Throttled scene scans and cached NPC/weapon candidates.
- NPC AI access only when a nearby NPC/AI is detected.
- Reduced always-on logging, with debug logging kept toggleable.
- Shared in-process WAV playback for panic/bodycam audio instead of per-sound helper process launches.
- Bodycam weapon polling controlled by performance mode and configurable intervals.
- Asset autoload skipped when performance mode is active.

## MDT Notes

The MDT includes:

- Search-only known records grouped alphabetically.
- Recently improved filing subject search.
- Manual record creation from the Records tab.
- Split first/last name fields in record and search/edit flows.
- Registration, wanted, driver license, plate, and weapon license fields.
- Report/citation location fields split into ZIP and street choice.
- Searchable charges and collapsed citation/charges filing controls.
- Court interval config support.

The old automatic NPC sync/record creation flow was removed from the logger path.

## Dispatch And Panic Audio

Panic audio paths:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button\Code99
```

Panic behavior plays a random panic button tone first, then a random Code99 WAV when available. The previous red screen effect was removed.

Grammar Police will enter button-only mode if Windows speech recognition dependencies are missing, instead of repeatedly failing during gameplay.

## Asset Loading

Asset bundles are loaded from:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\UserData\FLMods\Assets
```

Startup autoload can be disabled through performance mode. Manual reload is controlled through `FLModkitBindings`.

## Build And Deploy

Build all projects with:

```powershell
.\build.ps1 -GameRoot "E:\SteamLibrary\steamapps\common\Flashing Lights"
```

Build an individual project with:

```powershell
dotnet build .\MDTMod\MDTMod.csproj -c Release "-p:GameRoot=E:\SteamLibrary\steamapps\common\Flashing Lights\\"
```

Most project files copy their DLL and dependency files into the game `Mods` folder after a successful Release build.

## Debugging

Primary runtime log:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\MelonLoader\Latest.log
```

Keep debug/verbose logging disabled during normal gameplay for best FPS stability. Enable it only while testing a specific feature, then turn it back off.

## Repository Helpers

- `build.ps1` - build/deploy helper.
- `SessionProgressWatcher.ps1` - writes session progress to `Downloads\FL_MODS_session_progress.txt`.
- `auto-git.ps1` - optional local automation helper.
