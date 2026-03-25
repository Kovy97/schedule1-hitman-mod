# T.H.M — The Hitman Mod

A MelonLoader mod for **Schedule I** that adds a hitman contract system to the game. Take on assassination contracts through a phone app interface, manage targets, and earn rewards for completing hits.

## Requirements

| Requirement | Version |
|---|---|
| Schedule I | latest |
| MelonLoader | 0.7.1+ |
| S1API | latest |

## Installation

1. Install [MelonLoader](https://melonwiki.xyz/) for Schedule I
2. Install [S1API](https://www.nexusmods.com/schedule1/mods/) (required dependency)
3. Download the latest `Kowyx_THM.dll` from [Releases](../../releases)
4. Place it in your `Schedule I/Mods/` folder

## Features

- Contract system with multiple target types
- Phone app UI (KowyxUI framework)
- Contract manager with target tracking
- NPC defense system
- Mysterious Man NPC integration

## Building from Source

```bash
# 1. Update GameDir in Directory.Build.props to your Schedule I install path
# 2. Build
cd HitmanMod
dotnet build --configuration Release
# Output: Schedule I/Mods/Kowyx_THM.dll
```

> **Note:** `Directory.Build.props` contains the path to your Schedule I install. Update `<GameDir>` to match your installation.

## License

All rights reserved — Kowyx
