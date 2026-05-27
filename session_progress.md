# Session Progress — 2026-05-27

## Current State
- All 5 MelonLoader mods built and deployed to `E:\SteamLibrary\steamapps\common\Flashing Lights\Mods\`
- GameEventLogger **source restored** at `C:\Users\joshy\OneDrive\Documents\FL MODS\GameEventLogger\`
- Source project contains: `GameEventLoggerMod.cs`, `LoggerConfig.cs`, `PoolPatches.cs`, `ScenePatches.cs`, `CrashPatches.cs`, `GameEventLogger.csproj`, `Properties/AssemblyInfo.cs`
- DLL rebuilt from decompiled source — bit-identical (MD5 match verified)
- 4 other mod sources at `C:\Users\joshy\OneDrive\Documents\FL MODS\`: BackgroundRadio, GrammarPoliceMod, MDTMod, NPCAI
- DispatchAudio folder contains audio assets (wav files) — not a C# mod
- Game launch via `steam://rungameid/605740` (manual)

## Feature Status

### EventLogger (GameEventLogger)
- **Camera freeze**: Only sets `Time.timeScale=0` + cursor unlock when UI opens
- **Weapon/tool tracking**: Active. 3-tier scan:
  1. Recursive scan of player GameObject hierarchy (FindWeaponInHierarchy)
  2. Recursive scan of Main Camera hierarchy
  3. Fallback: scan ALL GameObjects (non-root, non-Coll_, non-mixamorig)
- **Weapon names**: IsWeaponName optimized to static arrays. Checks: weapon, firearm, holster, rifle, pistol, shotgun, taser, tazer, flashlight, stun, wep_, gun_, name, database, generation, npc (contains) + bat, knife, tool, item, equipment, gear, gun (exact)
- **False positive exclusions**: weaponwheel, weapons, weapon, pos_*, Coll_*, mixamorig*
- **Player detection**: Added CharacterController component search, Camera.main.root walk-up, expanded fallback patterns (cop, police, sheriff, ems, npc, name, database, generation, fd prefix)
- **NPC name generation tracking**: NEW — scans for "name"+"generated" GameObjects every 3s, logs pool activations tagged NPC_NAME
- **Database update tracking**: NEW — monitors Mods folder files (mdt_data.json, config.txt) for size changes every 5s, logs pool activations tagged DATABASE
- **Generation event tracking**: NEW — scans for generation/spawner/population objects every 3s, logs pool activations tagged GENERATION
- **NPC interaction tracking**: Active — checks DialogueCanvas, DialoguePanel, etc.
- **Call dispatch tracking**: Active — tracks pool activations as calls
- **In-game log viewer**: Delete key toggles — shows last 500 lines from EventLog.txt
- **Crash hooks**: AppDomain unhandled exception handler, writes CrashLog.txt
- **Freeze/Unfreeze**: Also triggers when NPCAI "npcInput" has focus (IsOtherModUIOpen)

### MDTMod
- **Camera freeze**: RESTORED — FreezeCamera/UnfreezeCamera disables non-essential Behaviours on Main Camera + parent Cam_Ply + siblings
- **Time freeze**: Active (Time.timeScale=0 when MDT open)

### NPCAI
- **Camera freeze**: Removed (only Time.timeScale=0/1)
- **System prompt**: Loaded from NPCI_GeminiPrompt.txt at runtime

### GrammarPolice
- **Camera freeze**: Removed (no timeScale or camera manipulation)
- **Game time**: Keeps running when UI is open

### BackgroundRadio
- **Camera freeze**: Removed (only Time.timeScale=0/1)
- **Build target**: net6.0

## Known Issues
- EventLog from 2026-05-27 sessions confirms Player `Cop001_M_01(Clone)001(Clone)` was never found by FindPlayer — the old name patterns didn't match. The fix adds CharacterController + Camera.root + cop/police/ems/npc patterns to resolve this.
- First session (23:45) showed false positive weapon detections ("Weapons", "WeaponWheel", "Weapon") from Tier 3 fallback — the exclusion list was added in a subsequent build.
- Camera stuck issue resolved (removed camera freeze from all mods except MDT)

## Next Steps
- Launch game via `steam://rungameid/605740` to test:
  - Player detection with new CharacterController + Camera.root + expanded name patterns
  - Weapon scan with actual weapon models (Wep_Pistol_01, gun_01, StunGun, etc.)
  - NPC name generation tracking
  - Database update tracking
  - Generation event tracking
- Build other mods from source if modifications are needed
