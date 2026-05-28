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

`FLModkitBindings` also detects duplicate key assignments across mods. It logs a warning when two actions share the same key, and `Auto Resolve Key Conflicts` can assign unused fallback keys to duplicated actions.

Currently centralized:

- Asset Loader reload
- Background Radio toggle, navigation, select, and stop
- Bodycam toggle and emergency trigger
- Bodycam bookmark event
- Bodycam driver license screen scan
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

- Map tab using the exact bundled Flashing Lights map image at `Mods\Assets\Map Flashing lights CLEAN.PNG`.
- Local incident, BOLO, and bodycam evidence markers. Multiplayer nearby-unit tracking was intentionally removed from scope.
- Search-only known records grouped alphabetically.
- Recently improved filing subject search.
- Manual record creation from the Records tab.
- Split first/last name fields in record and search/edit flows.
- Registration, wanted, driver license, plate, and weapon license fields.
- Report/citation location fields split into ZIP and street choice.
- Searchable charges and collapsed citation/charges filing controls.
- Court interval config support.
- Bodycam evidence visibility through shared bookmark data.

The old automatic NPC sync/record creation flow was removed from the logger path.

## Dispatch And Panic Audio

Panic audio paths:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button\Code99
```

Panic behavior plays a random panic button tone first, then a random Code99 WAV when available. The previous red screen effect was removed.

Grammar Police will enter button-only mode if Windows speech recognition dependencies are missing, instead of repeatedly failing during gameplay.

Speech and command improvements:

- `GrammarPolice\MacroCommands.json` supports aliases and multi-step macros.
- Default macro examples include `request location` -> `10-20` and `traffic stop` -> `10-50`, MDT open key sequence, then `10-28`.
- `GrammarPolice\OfflineSpeech\transcript.txt` is watched as an offline speech bridge. Vosk or Whisper.cpp can be launched externally and write recognized phrases there.
- `OfflineSpeechCommand` can optionally start an external recognizer command, but no large native recognizer binary is bundled in this repo.

## Bodycam Notes

Bodycam overlay now supports:

- Metadata overlay with timestamp, unit ID, camera mode, simulated battery, simulated storage, and optional GPS/player-position label.
- Manual bookmark hotkey from `FLModkitBindings`.
- Driver license scan screen reader that only runs while the bodycam is active.
- Screen-reader name matching uses bundled first/last-name dictionaries extracted from `AI_First_names.pdf` and `Ai_Last_Names.pdf`.
- Latest scan saved to `UserData\FLMods\BodyCam\latest_license_scan.json` and importable from MDT new-record form.
- Automatic bookmarks for activation, panic, weapon draw, emergency key sequence, and idle auto-stop.
- Shared evidence log at `UserData\FLMods\BodyCam\bookmarks.json`.
- Panic-trigger activation through Grammar Police.
- Idle auto-deactivation after a configurable timeout.

Native QR/barcode decoding and direct video capture are not bundled. The current implementation uses a Windows UI Automation screen reader to inspect visible text fields while bodycam is active, then matches likely names against the bundled dictionaries.

## Background Radio

Background Radio includes an optional now-playing HUD widget showing the current scanner channel or offline track.

Live feed reliability notes:

- Feed discovery uses Broadcastify top feeds.
- Stream playback uses the feed relay URL when found, with the direct Broadcastify CDN URL as fallback.
- Playback helper failures are now surfaced in the radio UI/log instead of silently looking connected.
- Panic resume now keeps the selected station name before playback starts, so resume and now-playing state are less fragile.

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

## Input Shielding

Interactive mod panels block game-facing keyboard, mouse button, and mouse-look/movement axis input while open. MDT, Background Radio, Grammar Police Radio, and NPC AI still allow their own panel keys, including their close/toggle key, but clicks and typing should no longer pass through to the game underneath.

## Repository Helpers

- `build.ps1` - build/deploy helper.
- `SessionProgressWatcher.ps1` - writes session progress to `Downloads\FL_MODS_session_progress.txt`.
- `auto-git.ps1` - optional local automation helper.

## Latest Session Summary

- Added local-only MDT map support using the bundled map image.
- Added centralized keybind conflict detection and optional auto-resolution.
- Added bodycam metadata/bookmark evidence and MDT import for bodycam driver license scans.
- Reworked bodycam license scan to use active-bodycam-only screen reading with bundled name dictionaries.
- Added Grammar Police macro commands and an offline speech transcript bridge.
- Added Background Radio now-playing HUD and clearer live-feed playback failure reporting.
- Added shared input shielding for mod panels so mouse/keyboard input does not leak into gameplay.
