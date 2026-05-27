# Flashing Lights Mods

Five MelonLoader mods for Flashing Lights, built with FL-ModKit.

## Mods

### NPC AI (F9)
AI-powered NPC interactions via OpenAI-compatible API (DeepSeek, OpenRouter, OpenAI).
- Full NPC roleplay with memory, personality, and TTS
- Configurable API endpoint, model, key
- System prompt loaded from `NPCI_GeminiPrompt.txt`

### Background Radio (F10)
Stream police scanners from Broadcastify or play local audio files.
- Top feeds from Broadcastify with category browsing
- Offline Scanner mode for local MP3/WAV playback
- Auto-resume after Grammar Police panic cooldown
- Volume ducking with Grammar Police dispatch

### Grammar Police (F11 / PTT)
Voice-activated dispatch system with code recognition.
- Push-to-talk voice recognition
- Trigger 10-code signals and panic alerts
- Custom panic key sequences and audio
- Configurable confidence threshold

### MDT Terminal (F11)
In-game mobile data terminal for police roleplay.
- License and warrant checks
- NPC data storage with photos
- Court session scheduler
- Blocks game input while open

### Event Logger (Delete)
Developer tool for logging game events.
- Scene load/unload tracking
- Dispatch call pool monitoring
- Weapon state scanning
- NPC interaction detection
- In-game log viewer with auto-refresh

## Install

Run `install_mods.ps1` or manually copy all `.dll` files to `Flashing Lights\Mods\`.

## Build from Source

Each mod is a .NET 6 project. Open the `.csproj` in Visual Studio or run:
```
dotnet build -c Release
```

All mods auto-deploy to `Flashing Lights\Mods\` on successful Release build.

## Requirements
- MelonLoader v0.7.3 Open-Beta
- FL-ModKit (FlashingLights.ModKit.Core.dll included)
- .NET 6 SDK (for building from source)
