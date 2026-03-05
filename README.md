# RaidForge Mod - README

For issues or support, please reach out to Darrean (inility#4118).

> **Note:** Because of the complex nature of this mod some edge cases may still exist and issues may occur. If you run into any issues or find any weird bugs, please reach out.

## Description
RaidForge is a comprehensive V Rising mod designed to give server administrators total control over raid mechanics, offline base protection, and raid participation.

### What's New in the Latest Version:
* **Overhauled Damage Detection:** We've changed the way damage is detected. RaidForge now intercepts damage directly, making the system much more accurate and reliable.
* **Weapon Raiding (No Golems Needed):** You no longer need Siege Golems to attack offline/protected bases! You can now configure regular weapons to damage stone structures.
* **Strict Opt-In Raiding:** Fully fixed. If enabled, both the attacker and the defender must be opted in. You cannot raid an opted-in player if you are not opted in yourself.
* **Map Icons:** Added map icons to instantly spot when a decayed base or an offline-protected base is being raided.
* **Raid Interference Fixes:** Third-party interference burning is fixed. Plus, Admins and players in Bear Form are now completely immune to the burn!
* **Shard Protection:** Offline Raid Protection for Soul Shard holders has been adjusted and fine tuned to catch more edge cases.

## Core Features

### Advanced Offline Protection
* **Offline Protection:** Prevents bases from damage when all associated defenders (clan members or solo owners) are offline.
* **Configurable Grace Period:** Defines a window of vulnerability after the last defender logs off. Makes it so players cannot just log off before they are being raided, they must be offline for the given grace period to obtain Offline protection.
* **Soul Shard Rules:** Choose whether holding a Soul Shard revokes a clan's Offline Raid Protection. 

### Opt-In Raiding System
* Turn your server into a consensual PvP zone. When enabled, standard Offline Raid Protection is bypassed, and bases are invincible by default.
* Players must use `.raidoptin` to become raidable.
* **Mutual Combat:** An attacker cannot damage an opted-in base unless the attacker is also opted in. Both parties must be flagged for raiding.

### Dynamic Raid Interference
* Discourages third-party from interfering in active sieges.
* Automatically applies a burning debuff to players who enter an active siege territory if they are not the attacker or the defender.
* **Exemptions:** Server Admins and players utilizing Bear Form are immune to the burn (great for spectating, and is configurable).

### Live Map Icons
* Configurable map markers will automatically appear to alert the server when a Decayed Base or an Offline Base is actively taking damage and will apply a icon over the castle heart for a given time.

###  Siege Golem Automation & Weapon Raiding
* **Weapon Raiding:** Enable regular weapons and explosives to damage walls (with a configurable damage multiplier), making Golems optional for raiding.
* **Golem Automation:** Set a server start date and let RaidForge automatically scale Siege Golem HP as the server ages (e.g., higher HP on Day 7 than Day 1).
* **Manual Golem Control:** Admins can manually set and lock Golem HP levels via commands.

###  Custom Schedules & Waygate Limits
* Define exact daily raid windows using the server's local time (supports raids that span past midnight).
* Optionally restrict Waygate teleportation while a global raid window is active.

## Important Server Settings
To prevent conflicts and ensure RaidForge can fully manage raid windows, you **MUST** disable your server's default raid hour configurations in your standard server settings. Leaving the vanilla game's raid hours active will cause overlapping schedules and break RaidForge's time.

## Commands
*(Note: All times shown by RaidForge commands are based on the server's local timezone, not the player's client.)*

### Player Commands
* `.raidt`: Shows the time until the next scheduled raid window, or if raids are currently active.
* `.raiddays`: Displays the server's weekly raid schedule.
* `.raidstatus <PlayerName>`: Shows the raid vulnerability status (Offline Protected, Grace Period, Breached, Raidable) for a specific player's base.
* `.raidoptin`: Opts you and your clan into being raidable (if Opt-In system is enabled). Includes a configurable cooldown before you can opt out.
* `.raidoptout`: Opts you and your clan out of raiding (if your time-lock has expired).
* `.raidoptstatus`: Checks your current Opt-In status and time remaining on your lock.

### Admin Commands
* `.reloadraidforge`: Live reloads all RaidForge config files from the server disk so changes can be made without rebooting.
* `.raidon` / `.raidoff`: Manually forces global raids ON or OFF, overriding the schedule.
* `.clearraidforgeicons`: Forcefully clears all active raid map icons.
* `.removeorp <PlayerName>`: Instantly strips a player/clan of their offline protection until they log back in.
* `.forceopt <PlayerName> <in|out>`: Forces a specific player/clan into or out of the Opt-In system, bypassing cooldowns.
* `.golemstartdate`: Sets the Golem Automation "server start date" to the current exact time.
* `.golemsethp <LevelName>`: Manually locks Siege Golem health (e.g., Max, Low), overriding automation.
* `.golemauto`: Clears manual Golem HP overrides and resumes day-based automation.
* `.golemlist` / `.golemcurrent`: Views available Golem HP levels and checks the server's current live Golem settings.
* `.golem <PlayerName>`: Transforms the target player (or yourself) into a Siege Golem.
* `.raidrefreshcache`: Force-rebuilds the mods internal tracking of bases and clans if things get out of sync (only use in emergency).

## Configuration Files
RaidForge generates multiple config files in your `BepInEx/config/RaidForge/` directory after the first boot.

* `RaidScheduleAndGeneral.cfg`: Set your daily raid hours and Waygate restrictions.
* `OfflineProtection.cfg`: Toggle ORP, set the Grace Period duration, and toggle global chat announcements.
* `OptInRaiding.cfg` & `OptInSchedule.cfg`: Toggle the Opt-In system, set opt-out cooldowns, and schedule specific days where Opt-In is forced/bypassed.
* `WeaponRaiding.cfg`: Enable/disable weapon damage to bases and set the damage multiplier against stone.
* `RaidInterference.cfg`: Toggle the interloper burn system, and manage exemptions for offline bases, decaying bases, and Bear Form.
* `MapIcons.cfg`: Toggle map icons for decay/offline raids and set how long they linger after the last hit.
* `ShardSettings.cfg`: Manage max shard limits and toggle whether holding a shard disables your offline protection.
* `GolemSettings.cfg`: Manage day-based automated Golem HP scaling.
* `Troubleshooting.cfg`: Enable verbose logging for debugging (Keep this OFF or you may run into tons of lagging).

## Community & Support
* **VArena Discord:** [https://discord.gg/varena](https://discord.gg/varena)
* **V Rising Modding Wiki:** [https://wiki.vrisingmods.com](https://wiki.vrisingmods.com)
* **VArena Website:** https://www.v-arena.com

## Acknowledgements
Special thanks to the V Rising Modding community, the developers of the underlying frameworks, helskog (for the Waygate restriction feature), Mitch (zfolmt) (for the Raid Interference inspiration), and Amingo for helping with finding bugs.

* **Developer:** Darrean (inility#4118)
* **Future Plans:** Add visuals for offline protected bases (WIP)

## License
This RaidForge mod is licensed under the MIT License with a non-commercial clause. You may use, modify, and distribute this software, but you may not sell copies or derivative works for profit. Use at your own risk.
