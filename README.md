# FL Mods

MelonLoader mods and support code for Flashing Lights.

This repository contains a small mod suite for improving dispatch, MDT, bodycam, asset loading, NPC interaction, and in-game event handling. The current target game install path is:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights
```
*** **Adjust for your game install directory** ***
## Mods

- `GameEventLogger` - in-game event hooks, panic button behavior, weapon/fire detection, and dispatch audio sequencing.
- `GrammarPoliceMod` - voice command and dispatch audio support.
- `MDTMod` - MDT interface, record search, citation/report workflows, and known-record handling.
- `NPCAI` - nearby NPC detection and AI interaction gating.
- `BodyCamOverlay` - toggleable bodycam overlay, Axon-style signal audio, and weapon-draw activation.
- `AssetLoader` - loads user assets from the Flashing Lights mod asset folders.
- `BackgroundRadio` - background radio playback and related UI.
- `FLWatchdog` - helper/watchdog support.
- `Shared` - shared utility code linked into individual mods.

## Performance Notes

Recent optimization work focused on reducing CPU spikes and FPS stutter:

- A shared performance-mode file is created at `UserData\FLMods\PerformanceMode.json`.
- When enabled, it can centrally suppress voice recognition, asset startup autoload, bodycam weapon polling, radio streaming, and background NPCAI scans.
- Panic, dispatch, and bodycam WAV playback now uses a shared persistent audio helper instead of spawning a new PowerShell process for every sound.
- Weapon polling and weapon cache refresh intervals are configurable in the relevant mod configs.
- NPCAI nearby scans and NPC cache refreshes are configurable to reduce scene scanning overhead.
- Debug logging is toggleable and defaults to quiet behavior where possible.
- Verbose dispatch audio logs remain behind `VerboseLogging`.
- BodyCam, NPCAI, and BackgroundRadio debug output remain behind `DebugLogging`.

## Build

Use the repository build script when you want the default build flow:

```powershell
.\build.ps1 -GameRoot "E:\SteamLibrary\steamapps\common\Flashing Lights"
```

Individual projects can also be built directly:

```powershell
dotnet build .\GameEventLogger\GameEventLogger.csproj -c Release -p:GameRoot="E:\SteamLibrary\steamapps\common\Flashing Lights\"
```

## Runtime Folders

Common runtime folders used by the mods:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\UserData\FLMods\Assets
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button
E:\SteamLibrary\steamapps\common\Flashing Lights\DispatchAudio\Panic Button\Code99
```

## Performance Mode

The shared performance switch lives here after any linked mod starts:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\UserData\FLMods\PerformanceMode.json
```

Default file:

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

Set `"enabled": true` when you want the low-overhead profile. Manual controls still work where practical, but background/autoload work is suppressed.

## Debugging

Keep regular gameplay logs quiet for performance. Enable the relevant debug or verbose setting only while testing a specific issue, then turn it back off when done.

Useful log file:

```text
E:\SteamLibrary\steamapps\common\Flashing Lights\MelonLoader\Latest.log
```
