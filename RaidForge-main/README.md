# RaidForge Mod - README

*This mod is actively maintained and will continue to receive improvements and optimizations. For issues or support, please reach out to Darrean`inility#4118` on the [VArena Discord](https://discord.gg/varena) or The Modding Community Discord(https://wiki.vrisingmods.com/).*

## Description

RaidForge is a comprehensive V Rising mod designed to give server administrators enhanced control over raid mechanics, offline base protection, and raid participation. It allows for custom raid schedules, automating Siege Golem health, protection for offline players' bases (including grace periods), and management of interference during active sieges.

With RaidForge, you can:

* Manually enable or disable raiding periods instantly.
* Configure specific daily raid windows using the server's local time.
* Automate Siege Golem health adjustments based on server runtime.
* Manually set and persist Siege Golem health levels.
* Protect offline players' bases from Golem damage with a configurable grace period system.
* Discourage third-party interference during active Golem sieges.
* Allow players to view raid schedules, upcoming raid times, and detailed base vulnerability status.
* Restrict Waygate usage during active raid windows.

## Features

* **Manual Raid Control:** Instantly enable (`.raidon`) or disable (`.raidoff`) raids, overriding any schedule.
* **Scheduled Raids:** Define custom start and end times for raiding for each day of the week via configuration files. Raids automatically toggle based on this schedule.
* **Advanced Offline Raid Protection:**
    * Shields bases from Siege Golem damage when all associated defenders (clan members or solo owner) are offline.
    * Includes a configurable grace period (default 15 minutes) that defines a window of **vulnerability** after the last defender logs off or leaves their clan. This ensures raids can still proceed for a short time if defenders log off specifically to avoid an imminent raid, preventing the offline protection mechanic from being immediately exploited.
    * The grace period is intelligently voided (and full offline protection resumes if applicable) should a defending player come back online.
    * Fully controllable via a global enable/disable setting in `OfflineProtection.cfg`.
* **Dynamic Raid Interference System:**
    * Discourages third-party interference during active Golem sieges.
    * Automatically applies a configurable debuff to players ("interlopers") who enter an active Golem siege territory if they are not part of the attacking or defending parties.
    * Can be toggled on or off globally via `RaidInterference.cfg`.
* **Automated Golem Health:**
    * Set a "server start date" for automation.
    * Configure Siege Golem health levels to activate automatically on certain days after this start date.
    * Enable/disable day-based automation (overridden by manual Golem health settings).
    * Set the start date easily using `.golemstartdate`.
* **Manual & Persistent Golem Control:**
    * Check current live and configured Golem health settings (`.golemcurrent`).
    * Manually set a specific Siege Golem health level that persists across server restarts (`.golemsethp <LevelName>`), overriding automation.
    * Clear manual overrides and revert to day-based automation with `.golemauto`.
    * List available Golem health levels and estimated HP (`.golemlist`).
* **Player Information Commands:**
    * Check time until the next raid or current raid status (`.raidt`).
    * View the weekly raid schedule (`.raiddays`).
    * Check detailed raid vulnerability status of any player's base (`.raidtimer <PlayerNameOrSteamID>`).
* **Configurable Waygate Restrictions:** Optionally prevent Waygate use during active raid windows.
* **Configurable Logging:** Enable detailed verbose logging via `Troubleshooting.cfg`.
* **Configuration Reload:** Admins can reload all mod configurations live (`.reloadraidforge`).

## Crucial Server Settings for Raid Control

To prevent conflicts and ensure RaidForge can fully manage raid windows, **you must disable your server's default raid hour configurations** found within your server settings. Leaving default game raid hours active may cause overlapping schedules and unexpected issues with when raids turn on or off.

## Commands

**Note on Displayed Times:** All times shown by RaidForge commands (e.g., `.raiddays`, `.raidt`) are based on the **server's local timezone and clock**, not your individual client's timezone.

### Admin-Only Commands:

* `.reloadraidforge`: Reloads all RaidForge configuration files from disk.
* `.raidon`: Forces raids ON immediately, overriding the schedule.
* `.raidoff`: Forces raids OFF immediately, overriding the schedule.
* `.golemstartdate`: Sets the Golem Automation "server start date" to the current date/time. Saves to config and re-evaluates automation.
* `.golemcurrent`: Shows current live Golem health, manual override status, and day-based automation status.
* `.golemsethp <LevelName>`: Manually sets and persists a Siege Golem health level (e.g., `Max`, `Low`). Overrides day-based automation. Use `.golemlist` for valid names.
* `.golemauto`: Clears any manual Golem health override. Day-based automation will apply if enabled.
* `.golemlist`: Lists available Siege Golem health levels and their estimated HP from config.

### Player-Accessible Commands:

* `.raidt`: Shows time until the next scheduled raid window or if raids are currently active by schedule.
* `.raiddays`: Displays the configured weekly raid schedule.
* `.raidtimer <PlayerNameOrSteamID>`: Shows raid vulnerability status (Offline Protected, Grace Period, In Breach, Raidable) for the specified player's clan/base.

## Configuration

All RaidForge configuration files are located in the `BepInEx/config/RaidForge/` directory (created automatically after the first server run with the mod).

### 1. `RaidScheduleAndGeneral.cfg`
* **`[DailyRaidSchedule]`**: Define raid start/end times (HH:mm format, 24-hour clock) for each day (e.g., `MondayStartTime = 20:00`, `MondayEndTime = 23:00`).
    * Use `00:00` for both start and end if no raid is scheduled for a day.
    * For raids spanning midnight (e.g., Friday 22:00 to Saturday 02:00), set the first day's end time to `00:00` (special value representing midnight end) and the next day's schedule will cover the remainder.
* **`[General]`**:
    * `AllowWaygateTeleportsDuringRaid` (true/false): Allow/disallow Waygate use during active raid windows.

### 2. `GolemSettings.cfg`
* **`[GolemMainControls]`**:
    * `EnableDayBasedAutomation` (true/false): Toggle Golem health automation based on server days.
    * `ServerStartDateForAutomation` (yyyy-MM-dd HH:mm:ss): Start date for day-based automation. Set via `.golemstartdate`.
    * `ManualOverrideSiegeLevel` (string, e.g., `Normal`, `Max`, or empty): Manually sets a Golem health level, overriding automation. Set via `.golemsethp`, cleared by `.golemauto`.
* **`[GolemDayBasedAutomationSchedule]`**: For each `SiegeWeaponHealth` level (e.g., `VeryLow`, `Normal`, `Max`):
    * `{LevelName}_EnableInSchedule` (true/false): Include this level in the day-based schedule.
    * `{LevelName}_DayToActivateInSchedule` (integer, e.g., 0, 7): Day number (0 = start date) this health level activates.

### 3. `OfflineProtection.cfg`
* **`[Offline Raid Protection]`**:
    * `EnableOfflineProtection` (true/false): Toggle the offline raid protection and grace period system for Golem damage.

### 4. `RaidInterference.cfg`
* **`[Raid Interference]`**:
    * `EnableRaidInterference` (true/false): Toggle the system that debuffs "interlopers" during active Golem sieges.

### 5. `Troubleshooting.cfg`
* **`[Logging]`**:
    * `EnableVerboseLogging` (true/false): Enable detailed logs for debugging. Can impact performance on busy servers.

## Installation Instructions

1.  Ensure BepInEx is correctly installed on your V Rising server.
2.  Download the latest `RaidForge.dll` from the releases page.
3.  Place `RaidForge.dll` into your server’s `BepInEx/plugins` directory.
4.  Start the server once. RaidForge will generate its default configuration files in the `BepInEx/config/RaidForge/` folder. You can use the .reloadraidforge to reload the config files.


## Dependencies

* **VampireCommandFramework:** Required for chat command functionality. Ensure it's installed.

## Support & Community

For support, questions, or to join the community:

* Join the **VArena Discord:** [https://discord.gg/varena](https://discord.gg/varena)
* Join **The Modding Community Discord.**
* For general V Rising modding discussions and finding other mods, visit the **V Rising Modding Wiki:** [https://wiki.vrisingmods.com](https://wiki.vrisingmods.com)

## Acknowledgements

Special thanks to the V Rising Modding community and the developers of the underlying frameworks that make mods like this possible.

## Developer

* Darrean (inility#4118)

## Contributors

* **helskog** - Feature for preventing waygate usage during raid window.
* **Mitch (zfolmt)** - Inspiration and concepts from their "Raid Guard" mod for the Raid Interference feature.

## License

This RaidForge mod is licensed under the MIT License with a non-commercial clause.

Summary:

* You **ARE free** to use, copy, modify, merge, publish, and distribute copies of this software.
* You **MUST include** the original copyright notice and this permission notice in all copies or substantial portions of the software.
* You **MAY NOT** sell copies of the Software or derivative works based on the Software for profit.
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

*Disclaimer: RaidForge is a third-party mod and is not affiliated with Stunlock Studios or the official V Rising development team. Use at your own risk.*
