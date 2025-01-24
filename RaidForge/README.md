# RaidForge Mod - README

## Description
**RaidForge** is a mod for the game *V Rising* that lets server administrators **manage and control raid mechanics** more effectively. Part of the **Forge System**, RaidForge provides commands for **forcing raids on/off**, **scheduling raid windows** by each day-of-week, it’s easy to configure!

> **Join our community server and discord https://discord.gg/sanguineReign ** to experience more Forge System mods and exciting features!

## Features
- **Admin-Only Commands**: `.raidon`, `.raidoff`, `.raidtime` for secure server control  
- **Player Command**: `.raidt` (non-admin) to see the next scheduled raid time  
- **Force Raids**: Instantly enable or disable raids, overriding current settings  
- **Scheduled Raids**: Configure times/days for raids using your server’s local time  
- **Server Time Display**: Quickly check the server’s current date/time  
- **Customizable**: Tweak schedules, intervals, and override modes in the config  
- **Smooth Integration**: Uses VampireCommandFramework and BepInEx for easy operation  

## Part of the Forge System
RaidForge is just one mod in the Forge System. **Visit the Discord https://discord.gg/sanguineReign* for more modded and exciting content, bug reports, and updates!

---

## Commands

> **Prefix all commands with a period** (`.`). Some commands are **admin-only**, while `.raidt` is open to everyone.

### **.raidon** (Admin-Only)
- **Description**: Forces raids to be enabled immediately (one-time reflection). The normal schedule may turn raids off again if it’s outside the scheduled window on the next check.  
- **Usage**: `.raidon`  
- **Example Response**: `[RaidForge] Raids forced ON now.`

### **.raidoff** (Admin-Only)
- **Description**: Forces raids to be disabled immediately. If used during a scheduled raid window, the rest of that window is skipped.  
- **Usage**: `.raidoff`  
- **Example Response**: `[RaidForge] Raids forced OFF now.`

### **.raidtime** (Admin-Only)
- **Description**: Displays the server's **current local date/time** and **day-of-week** for reference.  
- **Usage**: `.raidtime`  
- **Example Response**:  
  ```
  [RaidForge] Server time is January 24, 2025 10:30:00 AM, DayOfWeek=Friday
  ```

### **.raidt** (Player Accessible)
- **Description**: Shows the **next scheduled raid start time** within the current or upcoming days, based on the configured schedule. If none is found in the next 7 days, it reports that.  
- **Usage**: `.raidt`  
- **Example Response**:  
  ```
  [RaidForge] Next scheduled ON time: January 24, 2025 8:00:00 PM
  
---

## Installation Instructions

1. **Download the Mod Files**  
   - Obtain the latest `RaidForge.dll` release from our official repository or release page.

2. **Extract Files**  
   - Place `RaidForge.dll` in your server’s BepInEx plugins directory, for example:  
     ```
     V_Rising_Server/
     └── BepInEx/
         └── plugins/
             └── RaidForge.dll
     ```

3. **Launch the Server**  
   - Start your V Rising dedicated server. RaidForge will initialize automatically, creating any needed config files.

---

## Configuration

A configuration file, **`RaidForge.cfg`**, is generated in:
```
V_Rising_Server/
└── BepInEx/
    └── config/
        └── RaidForge.cfg
```
You can **customize** your raid schedule, override modes, and interval checks.

### Available Configurations

- **OverrideMode**  
  - **ForceOn / AlwaysOn**: Raids are always active (disables scheduling)  
  - **ForceOff**: Raids are permanently disabled  
  - **Normal**: Raids follow the day-of-week schedule

- **RaidCheckInterval** (in seconds)  
  - How often the mod checks if raids should be toggled.  
  - Default: `5` seconds

- **Daily Raid Times** (one window per day)  
  - **MondayStart / MondayEnd**  
  - **TuesdayStart / TuesdayEnd**  
  - **WednesdayStart / WednesdayEnd**  
  - **ThursdayStart / ThursdayEnd**  
  - **FridayStart / FridayEnd**  
  - **SaturdayStart / SaturdayEnd**  
  - **SundayStart / SundayEnd**  
  - Format: `HH:MM:SS` (24-hour format)

#### Example Configuration
```ini
[RaidSchedule]
OverrideMode = Normal
RaidCheckInterval = 5

MondayStart = 20:00:00
MondayEnd = 22:00:00
TuesdayStart = 20:00:00
TuesdayEnd = 22:00:00
WednesdayStart = 20:00:00
WednesdayEnd = 22:00:00
ThursdayStart = 20:00:00
ThursdayEnd = 22:00:00
FridayStart = 20:00:00
FridayEnd = 22:00:00
SaturdayStart = 20:00:00
SaturdayEnd = 22:00:00
SundayStart = 20:00:00
SundayEnd = 22:00:00
```

---

## Dependencies
- **VampireCommandFramework**  
- **Bloodstone**  
- **BloodyCore** 


---

## Support
For support, **bug reports**, or **feature requests**:
- **Discord**: Direct Message **inility#4118**  
-  Join our Discord Channel https://discord.gg/sanguineReign

---

## Developer
**Darrean** (“inility”).  

---

## License
This mod is free to use or modify for personal use. Redistribution or commercial use without permission is prohibited.

**Disclaimer**:  
RaidForge is a *third-party mod*, not affiliated with the official V Rising development team. Use at your own risk.