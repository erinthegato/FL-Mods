# Changelog

## 2026-05-27 Session

- Added local-only MDT map support using the bundled `Map Flashing lights CLEAN.PNG` asset.
- Added centralized keybind conflict detection and optional auto-resolution.
- Added bodycam metadata/bookmark evidence and MDT import for bodycam driver license scans.
- Reworked bodycam license scan to use active-bodycam-only screen reading with bundled first/last-name dictionaries.
- Removed the bodycam post-shift record/export feature while keeping bookmarks and scan evidence.
- Added Grammar Police macro commands and an offline speech transcript bridge.
- Added Background Radio now-playing HUD and clearer live-feed playback failure reporting.
- Added shared input shielding for mod panels so mouse/keyboard input does not leak into gameplay.

## Unreleased

- Added `FLModkitBindings` conflict detection with warning logs for duplicate keys across mods.
- Added optional automatic key conflict resolution using unused fallback keys.
- Added central bodycam bookmark key binding.
- Added Grammar Police macro command support through `GrammarPolice\MacroCommands.json`.
- Added default macro examples for `request location` and `traffic stop`.
- Added offline speech bridge support through `GrammarPolice\OfflineSpeech\transcript.txt`, suitable for Vosk/Whisper.cpp helper output.
- Ensured Grammar Police loads radio code definitions before building speech constraints.
- Added bodycam metadata overlay fields for camera mode, simulated battery, simulated storage, and GPS/player-position label.
- Added bodycam bookmarks for manual marks, activation, panic, weapon draw, emergency trigger, and idle auto-stop.
- Added shared bodycam evidence storage at `UserData\FLMods\BodyCam\bookmarks.json`.
- Reworked bodycam driver license scanning to use an active-bodycam-only screen reader instead of clipboard/file-first scanning.
- Added first/last-name dictionaries extracted from `AI_First_names.pdf` and `Ai_Last_Names.pdf` for bodycam scan matching.
- Added MDT import button for the latest bodycam driver license scan.
- Removed the bodycam post-shift record/export feature; bookmark and license-scan evidence remain.
- Added shared input shielding so MDT, Background Radio, Grammar Police Radio, and NPC AI panels stop keyboard/mouse input from leaking through to the game.
- Added MDT Map tab using the bundled `Map Flashing lights CLEAN.PNG` asset.
- Removed the multiplayer nearby-units map idea from scope; MDT map markers are local incident/BOLO/evidence only.
- Added Background Radio now-playing HUD overlay.
- Improved Background Radio live-feed state handling so the selected station is tracked before playback starts.
- Added playback helper error reporting to Background Radio UI/log output.
- Kept Broadcastify direct CDN URL as fallback when a relay URL cannot be parsed from the feed page.
