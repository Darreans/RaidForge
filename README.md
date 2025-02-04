# RaidForge Mod - README

## Description
RaidForge is a mod for the game *V Rising* that allows server administrators to manage and control raid mechanics more effectively based on their server time. As part of the **Forge System**, RaidForge provides commands for:
- Forcing raids on/off
- Scheduling raid windows by each day of the week
- Controlling golem HP via commands

It’s easy to configure and fully integrated with **VampireCommandFramework**!

Join our community server and Discord: **[Sanguine Reign](https://discord.gg/sanguineReign)** to explore more Forge System mods and exciting features!

---

## Patch Notes (v1.1.0)
- Added `.raidmode <ForceOn|ForceOff|Normal>` command for quick override toggling.
- Removed `RaidCheckInterval` from user config (now fixed at 5s internal checks).
- Enabled re-scheduling a second window on the same day after skipping a previous one.
- Color-coded chat replies for clearer admin/player feedback.
- Approximate Golem HP shown after setting `SiegeWeaponHealth`.
- Improved day-of-week skip logic (no forced "off" for an entire day unless explicitly set via `.raidmode ForceOff`).
- Added ability to manage Golem configurations/HP purely through commands.

---

## Features
- **Admin-Only Commands**: `.raidon`, `.raidoff`, `.raidmode`, `.raidtime` for secure server control
- **Player Command**: `.raidt` for viewing the next scheduled raid time
- **Force Raids**: Instantly enable/disable raids, overriding the schedule
- **Scheduled Raids**: Configure times/days for raids using the server’s local time
- **Server Time Display**: Quickly check the server’s current date/time
- **Control Golem (SiegeWeaponHealth)**: Set & show approximate HP entirely via commands
- **Color-Coded Chat**: All replies are color-coded for clarity
- **Smooth Integration**: Uses *VampireCommandFramework*, *Bloodstone*, & *BepInEx*

---

## Commands
Prefix all commands with a period (`.`). Some commands are admin-only, while `.raidt` is open to everyone.

### `.raidmode` (Admin-Only)
**Description**: Dynamically sets the override mode for raids.
- `ForceOn` → Raids always enabled, ignoring schedule.
- `ForceOff` → Raids always disabled.
- `Normal` → Uses the day-of-week schedule.

**Usage**: `.raidmode ForceOn` *(or ForceOff, Normal)*

**Example Response:**  
`[RaidForge] OverrideMode set to ForceOn. (If Normal, day-of-week scheduling is active.)`

---

### `.raidon` (Admin-Only)
**Description**: Forces raids to be enabled immediately for the rest of today (manual override).

**Usage**: `.raidon`

**Example Response:**  
`[RaidForge] Raids turned ON now and will remain ON (unless manually turned OFF).`

---

### `.raidoff` (Admin-Only)
**Description**: Forces raids to be disabled immediately. If used during a scheduled window, the rest of that window is skipped, but the schedule can still resume if re-scheduled.

**Usage**: `.raidoff`

**Example Response:**  
`[RaidForge] Raids turned OFF. If inside today's window, skip remainder of that window.`

---

### `.raidtime` (Admin-Only)
**Description**: Displays the server's current local date/time and day-of-week for reference.

**Usage**: `.raidtime`

**Example Response:**  
`[RaidForge] Server time: January 24, 2025 10:30:00 AM (DayOfWeek=Friday)`

---

### `.raidt` (Player Accessible)
**Description**: Shows the next scheduled raid start time within the current/upcoming days.

**Usage**: `.raidt`

**Example Response:**  
`[RaidForge] Next scheduled ON time: January 24, 2025 8:00:00 PM`

---

### Day-of-Week Window Commands (Admin-Only)
**Description**: Set daily raid windows in HH:mm format. Example: `.raidmon 19:00 21:00` for Monday 7pm–9pm.

**Commands:**
- `.raidmon`
- `.raidtue`
- `.raidwed`
- `.raidthu`
- `.raidfri`
- `.raidsat`
- `.raidsun`

**Example:**  
`.raidfri 19:00 22:00`  
`[RaidForge] Friday window set to 19:00:00 - 22:00:00. Updated schedule loaded.`

---

### `.raidsched` (Admin-Only)
**Description**: Displays the entire weekly raid schedule based on the server time.

**Usage**: `.raidsched`

**Example Response:**  
```
[RaidForge] Current Weekly Raid Schedule:
 - Monday: 20:00 - 22:00
 - Tuesday: 20:00 - 22:00
 ...
```

---

### Golem / SiegeWeaponHealth Commands (Admin-Only)
**Description**: Sets the siege-weapon (golem) HP level via `ServerGameBalanceSettings`, showing approximate HP.

**Commands:**
- `.golemcurrent` → Show current `SiegeWeaponHealth`
- `.golemverylow`, `.golemlow`, `.golemnormal`, `.golemhigh`, `.golemveryhigh`, `.golemmegahigh`, `.golemultrahigh`, `.golemcrazyhigh`, `.golemmax` → Set HP

**Example:**  
`.golemmax`  
`[RaidForge] SiegeWeaponHealth updated to Max (~999999 HP).`

---

## Installation Instructions
### Download the Mod Files
Obtain the latest **RaidForge.dll** release from our official repository or release page.

### Extract Files
Place **RaidForge.dll** in your server’s BepInEx plugins directory:
```
V_Rising_Server/
└── BepInEx/
    └── plugins/
        └── RaidForge.dll
```

### Launch the Server
Start your *V Rising* dedicated server. RaidForge will initialize automatically, creating any needed config files.

---

## Configuration
A configuration file, `RaidForge.cfg`, is generated in:
```
V_Rising_Server/
└── BepInEx/
    └── config/
        └── RaidForge.cfg
```

### Available Configurations
- **OverrideMode**
  - `ForceOn` (AlwaysOn): Raids are always active (disables scheduling)
  - `ForceOff`: Raids are permanently disabled
  - `Normal`: Raids follow the day-of-week schedule
- **Daily Raid Times (one window per day)**
  - `MondayStart` / `MondayEnd`
  - `TuesdayStart` / `TuesdayEnd`
  - etc.

---

## Dependencies
- **VampireCommandFramework**
- **Bloodstone**

---

## Support
For support, bug reports, or feature requests:
- Discord: Direct Message `inility#4118`
- Or join our **[Discord Channel](https://discord.gg/sanguineReign)**

---

## Developer
Darrean (*inility*).

---

## License
This mod is free to use or modify.

**Disclaimer:**  
RaidForge is a third-party mod, not affiliated with the official *V Rising* development team. Use at your own risk.
