# RaidForge Mod - README

## Description
RaidForge is a mod for *V Rising* designed to give server administrators enhanced control over raid mechanics. It allows setting custom raid schedules for any day of the week and automating Siege Golem health based on server runtime duration.

This mod allows you to:
- Manually enable or disable raiding periods instantly.
- Configure specific daily raid windows using the server's local time.
- Automate Siege Golem health adjustments based on the number of days the server has been running since a specified start date.
- Manually set Siege Golem health levels.
- View the raid schedule and upcoming raid times.

## Features
- **Manual Raid Control**: Instantly enable (`.raidon`) or disable (`.raidoff`) raids, overriding any schedule.
- **Scheduled Raids**: Define custom start and end times for raiding for each day of the week via configuration file.
- **Automated Golem Health**:
    - Set a "server start date".
    - Configure specific Siege Golem health levels to activate automatically on certain days after the start date.
    - Enable/disable this automation via config.
    - Set the start date easily using the `.golemstartdate` command.
- **Manual Golem Control**:
    - Check the current golem health level (`.golemcurrent`).
    - Manually set specific golem health levels using `.golemset <LevelName>`.
    - List available golem health levels and their estimated HP (`.golemlist`).
- **Player Commands**: Players can check the time until the next raid (`.raidt`) and view the weekly schedule (`.raiddays`).
- **Configurable Logging**: Enable verbose logging via config for easier troubleshooting.

## Important Note on Server Settings
For the scheduled raid times to work correctly, you should configure your server's base raid settings appropriately in the `ServerHostSettings.json`:
- Set `CastleDamageMode` to `TimeRestricted`.
- Set the start and end times for **all** days within `GameTimeModifiers` to cover the entire 24-hour period (e.g., StartTime 0, EndTime 24) or simply set them both to 00:00. This allows the mod to fully control the active raid windows based on its own schedule. If you leave default server raid times active, they might conflict with the mod's schedule.

## Commands


**Admin-Only Commands:**
- **`.reloadraidforge`**: Reloads all RaidForge configurations (Raid Schedule & Golem Automation).
- **`.raidon`**: Forces raids ON immediately, overriding the schedule.
- **`.raidoff`**: Forces raids OFF immediately, overriding the schedule.
- **`.golemstartdate`**: Sets the Golem Automation start date/time to the moment the command is run. Saves to config.
- **`.golemcurrent`**: Shows the current `SiegeWeaponHealth` setting.
- **`.golemset <LevelName>`**: Manually sets the `SiegeWeaponHealth`. Example: `.golemset High`. Use `.golemlist` to see valid LevelNames.
- **`.golemlist`**: Lists all available Siege Golem health levels and their estimated HP.

**Player-Accessible Commands:**
- **`.raidt`**: Shows the time remaining until the next scheduled raid window begins.
- **`.raiddays`**: Displays the configured weekly raid schedule.

## Configuration
Configuration is done via the BepInEx config file, typically located at `BepInEx/config/RaidForge.cfg`.

Key sections:
- **`[Daily Schedule]`**: Configure raid start/end times (HH:mm format) for each day. Set `EnableVerboseLogging` to `true` or `false`.
- **`[GolemAutomation]`**:
    - `EnableGolemAutomation`: `true` or `false` to turn the feature on/off.
    - `ServerStartDate`: Set the reference start date/time (`yyyy-MM-dd HH:mm:ss`) for day counting. Can be set via `.golemstartdate`.
- **`[GolemAutomation.Levels]`**:
    - `{LevelName}_Enable`: `true` or `false` for each health level (e.g., `High_Enable = true`).
    - `{LevelName}_Day`: Day number (0+) when the corresponding level should activate if enabled (e.g., `High_Day = 1`). Use `-1` to effectively disable a level even if `_Enable` is true.

## Installation Instructions
1. Ensure BepInEx IL2CPP is installed correctly on your server.
2. Download the latest `RaidForge.dll` release.
3. Place `RaidForge.dll` into your server’s `BepInEx/plugins` directory.

## Dependencies
- **VampireCommandFramework**: Required for chat command handling.

## Support & Community

- Join the **VArena Discord**: **[https://discord.gg/varena](https://discord.gg/varena)**

For general V Rising modding discussions and finding other mods:
- Visit the **V Rising Modding Wiki**: [https://wiki.vrisingmods.com/](https://wiki.vrisingmods.com/)

## Acknowledgements
Special thanks to the V Rising Modding community and the developers of the underlying frameworks. Collaboration and open code sharing make mods like this possible.

## Developer
Darrean (inility)

## License
This RaidForge mod is licensed under the **MIT License** with a non-commercial clause.

**Summary:**
- You **ARE free** to use, copy, modify, merge, publish, and distribute copies of this software.
- You **MUST include** the original copyright notice and this permission notice in all copies or substantial portions of the software.
- You **MAY NOT** sell copies of the Software or derivative works based on the Software for profit.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

**Disclaimer:**
RaidForge is a third-party mod and is not affiliated with Stunlock Studios or the official V Rising development team. Use at your own risk.
