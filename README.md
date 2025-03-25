# RaidForge Mod - README

## Description
RaidForge is a mod for *V Rising* that allows server administrators to manage and control raid mechanics more effectively based on their server’s local time. It is part of the **Forge System**. To enable automation features, YOU MUST INSTALL ForgeScheduler. While toggle options can be used without it, activating and deactivating raids at scheduled times requires ForgeScheduler to be installed. The configuration file cannot be generated without it.

RaidForge provides commands for:
- Forcing raids on/off
- Scheduling raid windows for each day of the week
- Controlling golem HP via commands

### Discords To Join

Join our community server and Discord: **[Sanguine Reign](https://discord.gg/sanguineReign)** to explore more Forge System mods and exciting features!

For additional mods and support, join the modding community on Discord by visiting [V Rising Mods Wiki](https://wiki.vrisingmods.com/).

## Important note

This mod will NOT override the settings for raid hours. You must set your raid time to 00:00 in VS Castle Time. Failure to do so will activate your raid time, and using .raidoff will NOT override it. For the best experience, it is recommended to use .TimeRestricted and set the time to 00:00.
Even if you choose to use Always, you should still set the time to 00:00 to prevent issues. All config settings will be available under the ForgeScheduler folder. You will need to install that as it is the handler for automation for ForgeMods.


---
## Patch Notes (v1.4.0) (Current)

Thank you **Nova** for reporting the resolved bugs below.

- ** Logging off and back on when .raidoff was used would prevent issues with you being able to key castle hearts

## Patch Notes (v1.3.0) 

- Fixed an issue with being unable to key castle heart. You can now  key castle hearts.

Removed the following commands due to ForgeScheduler now handling configuration settings. RaidForge alone will now only support toggle options; for automation, you must install ForgeScheduler. All daily settings can now be configured using ForgeScheduler with RaidForge:

  - **`.golemauto on/off`** → Toggle day-based HP automation.
  - **`.golemauto start [optional-date]`** → Set “day 0” to the current or specified date/time.
  - **`.golemauto check`** → Check the current day, start date, and mapped HP.
  - **`.golemauto clear`** → Remove the start date, disabling daily HP changes until reset.
  - **`.raidmode <ForceOn|ForceOff|Normal>`**
  - **`.raidresume`**
  - **`.raidmon`**
  - **`.raidtue`**
  - **`.raidwed`**
  - **`.raidthu`**
  - **`.raidfri`**
  - **`.raidsat`**
  - **`.raidsun`**
  - **`.raidsched`**

---

## Patch Notes (v1.2.0)

- **New `.raidresume` command**: Quickly revert to the **normal day-of-week schedule** after using `.raidon` or `.raidoff`.
  - If you do not use `.raidresume` after `.raidoff`, raiding time will not resume until the following day’s schedule.
- **New Golem Automation**: Dynamically adjusts golem HP each day based on a start date.
  - **`.golemauto on/off`** → Toggle day-based HP automation.
  - **`.golemauto start [optional-date]`** → Set “day 0” to the current or specified date/time.
  - **`.golemauto check`** → Check the current day, start date, and mapped HP.
  - **`.golemauto clear`** → Remove the start date, disabling daily HP changes until reset.
- **Immediate Config Save**: When setting a golem start date with `.golemauto start`, the configuration is immediately saved to `RaidForge.cfg` in a human-readable date/time format (`yyyy-MM-dd HH:mm:ss`).

---

## Patch Notes (v1.1.0)

- Added `.raidmode <ForceOn|ForceOff|Normal>` for quick override toggling.
- Removed `RaidCheckInterval` from the user config (now fixed at 5-second internal checks).
- Enabled re-scheduling a second window on the same day after skipping a previous one.
- Introduced color-coded chat replies for clearer admin/player feedback.
- Displayed approximate golem HP after setting `SiegeWeaponHealth`.
- Improved day-of-week skip logic (no forced "off" for an entire day unless explicitly set via `.raidmode ForceOff`).
- Added the ability to manage golem configurations/HP entirely through commands.

---

## Features
- **Force Raids**: Instantly enable/disable raids, overriding the schedule.
- **Scheduled Raids**: Configure specific times/days for raids using the server’s local time (requires ForgeScheduler).
- **Server Time Display**: Quickly check the server’s current date and time.
- **Control Golem (SiegeWeaponHealth)**:
  - **Manual Control**: Set and display approximate HP using commands (e.g., `.golemhigh`, `.golemmax`, etc.).
- **Integration with ForgeScheduler**: Automate golem health settings and raid schedules based on day-of-the-week.

---

## Commands

Prefix all commands with a period (`.`). Some commands are admin-only, while `.raidt` is available to all players.

### .raidon (Admin-Only)
**Description:** Forces raids to be enabled immediately.

### .raidoff (Admin-Only)
**Description:** Forces raids to be disabled immediately.

### .raidmode (Admin-Only, requires ForgeScheduler)
**Description:** Sets the raid mode. Now includes an ignore option (`.raidmode ignore`) to bypass scheduled automation logic.

### .raidt (Player Accessible, requires ForgeScheduler)
**Description:** Displays the next scheduled raid start time within the current/upcoming days.

### Day-of-Week Window Commands (Admin-Only)
**Description:** Set daily raid windows in HH:mm format.  
*Example:* `.raidmon 19:00 21:00` sets Monday’s raid window from 7:00 PM to 9:00 PM.

### Golem / SiegeWeaponHealth Commands (Admin-Only)
**Description:** Adjusts the siege-weapon (golem) HP level via ServerGameBalanceSettings, showing approximate HP.

**Manual Commands:**
- **`.golemcurrent`** → Displays the current SiegeWeaponHealth.
- **`.golemverylow`**, **`.golemlow`**, **`.golemnormal`**, **`.golemhigh`**, **`.golemveryhigh`**, **`.golemmegahigh`**, **`.golemultrahigh`**, **`.golemcrazyhigh`**, **`.golemmax`** → Change the golems’ HP immediately.

---

## Installation Instructions

**Download the Mod Files:**  
Obtain the latest `RaidForge.dll` release from our official repository or release page.

**Extract Files:**  
Place `RaidForge.dll` in your server’s BepInEx plugins directory:

---

## Dependencies
- VampireCommandFramework
- Bloodstone
- ForgeScheduler 
---

## Support

For support, bug reports, or feature requests:
- **Discord:** Direct Message *inility#4118*
- **Discord Channel:** [Sanguine Reign](https://discord.gg/sanguineReign)

---

## Developer

Darrean (inility).

---

## License

This mod is free to use and modify.

**Disclaimer:**  

RaidForge is a third-party mod and is not affiliated with the official V Rising development team. Use at your own risk.

