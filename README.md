<p align="center">
  <img src="https://raw.githubusercontent.com/Darreans/RaidForge/main/assets/raidforge-logo.png" alt="RaidForge logo" width="720">
</p>

# RaidForge

RaidForge is a server-side V Rising mod for configurable raid schedules, offline base protection, opt-in raiding, purchased protection, weapon and explosive raiding, raid interference, map alerts, Siege Golem automation, Soul Shard rules, and per-castle servant limits.

Current mod version: **3.2.2**

> [!IMPORTANT]
> RaidForge changes important combat and castle-protection rules. Back up your world and configuration files before installing or updating it, and validate changes on a test server first.

## What's New

- **Easier raid-time command:** Players can use `.raidtime`, or the shorter `.raidt`, to see when the next raid window begins.
- **Optional command customization:** Server owners can rename or disable individual RaidForge commands and choose whether they appear in normal `.help` results. Command changes apply after a full restart.
- **Separate TNT controls:** T01 and T02 explosives can each have their regular damage, castle-wall damage, and after-breach behavior adjusted independently.
- **Per-base servant limits:** Server owners can cap selected servant types separately for every castle.
- **Clan-safe counting:** Clanmates share the servant limit for the base they are using, while a different castle keeps its own count.
- **Safe servant rejection:** A servant coffin refuses an extra servant before the captured NPC is consumed.
- **Clear player feedback:** Rejection messages are simple and throttled so repeated interactions do not spam chat.
- **Lightweight troubleshooting:** Detailed diagnostic logging remains off unless a server owner intentionally enables it.

## Requirements

- A V Rising dedicated server
- BepInEx 6 for IL2CPP
- HookDOTS.API
- VampireCommandFramework

RaidForge is server-side. Players do not need to install the mod locally.

This source tree expects the following locally supplied build dependencies:

```text
libs/HookDOTS.API.dll
libs/VampireCommandFramework.dll
```

Those third-party binaries are intentionally not stored in this repository.

## Installation

1. Stop the dedicated server and back up its save and configuration directories.
2. Install BepInEx, HookDOTS.API, and VampireCommandFramework.
3. Copy `RaidForge.dll` into `BepInEx/plugins/`.
4. Start the server once so RaidForge can create its configuration files.
5. Stop the server, review `BepInEx/config/RaidForge/`, and enable only the systems you intend to use.
6. Restart the server and verify the startup log.

> [!IMPORTANT]
> If RaidForge controls scheduled castle damage, the active save's `ServerGameSettings.json` must use `"CastleDamageMode": "TimeRestricted"`. Do not use `Always` or `Never`/disabled. RaidForge switches castle damage to `Always` during its configured window and returns it to `TimeRestricted` afterward.
>
> The vanilla weekday and weekend castle raid times must also all be set to `00:00`. In JSON, that means every `StartHour`, `StartMinute`, `EndHour`, and `EndMinute` below is `0`. Put the real raid windows in `RaidScheduleAndGeneral.cfg`.

```json
{
  "CastleDamageMode": "TimeRestricted",
  "PlayerInteractionSettings": {
    "VSCastleWeekdayTime": {
      "StartHour": 0,
      "StartMinute": 0,
      "EndHour": 0,
      "EndMinute": 0
    },
    "VSCastleWeekendTime": {
      "StartHour": 0,
      "StartMinute": 0,
      "EndHour": 0,
      "EndMinute": 0
    }
  }
}
```

Merge these values into the existing file rather than replacing unrelated server settings. They are required for reliable scheduled activation. Zeroing the vanilla times prevents a second raid schedule from overlapping RaidForge.

### Server Time and Schedule Offset

The dedicated server machine's local clock is RaidForge's source of truth. `RaidScheduleDisplayOffsetHours` adds a fixed number of hours to that clock for both schedule checks and command output:

- `0` uses the server clock unchanged.
- A positive value moves RaidForge's schedule clock later.
- A negative value moves it earlier.
- Example: if the server clock is `08:00` and the offset is `2`, RaidForge treats the schedule time as `10:00`.

Use the offset when the server's clock and the community's desired timezone do not match. `RaidScheduleTimeZoneForDisplay` is only the text label shown to players; it does not convert time. The offset is fixed and does not automatically change for daylight saving time, so review it when local clocks change.

## Feature Overview

### Raid Scheduling and Administration

- Define daily raid windows, including windows that cross midnight.
- Apply a display offset to RaidForge's schedule clock.
- Allow or prevent Waygate travel during an active global raid window.
- Force raids on or off temporarily, then return control to the automatic schedule.
- Inspect loaded configuration, runtime state, and cached owner/base counts through admin commands.
- Reload RaidForge configuration files without a full server restart.

### Offline Raid Protection

- Protects a castle when all associated defenders are offline.
- Evaluates the castle owner or clan rather than only the last player who touched the base.
- Supports a configurable logout grace period so logging out during danger does not immediately protect a castle.
- Optionally restricts ORP to selected days of the week.
- Can announce eligible offline or decayed-base raids with cooldowns to prevent chat spam.
- Can make Soul Shard owners ineligible for Offline Raid Protection.

Offline Raid Protection takes priority if it and Opt-In Raiding are accidentally enabled together.

### Opt-In Raiding

- Allows players and clans to choose whether their bases are raidable.
- Supports default opted-in or opted-out server policies.
- Enforces mutual participation when configured: an attacker must also be opted in before damaging an opted-in defender.
- Includes opt-state lock durations, scheduled opt-in days, optional automatic opt-out, and automatic handling for Soul Shard holders.
- Opt status is stored by persistent owner identity so clan members share the same result.

### Purchased Offline Protection

- Players can buy protection with `.buyorp <days>`.
- Each purchased unit represents one configured raid-day credit.
- Non-raid days do not consume credits.
- The currency prefab, display name, price per raid day, and maximum purchase are configurable.
- Protection is stored by the player or clan owner key, so clan-owned bases share the same balance.
- Administrators can grant or remove credits.

Purchased ORP is intended as its own protection mode. Standard Offline Raid Protection and Opt-In Raiding should be disabled when it is used.

### Weapon and Explosive Raiding

- Regular weapon raiding and T01/T02 explosive raiding can be enabled independently.
- Regular weapons use the configured stone-structure multiplier.
- T01 and T02 normal damage can be scaled independently. `100` is native damage, `10` is 10% of native damage, and `110` is 10% above native damage.
- T01 and T02 castle-wall damage can be tuned independently from ordinary TNT damage. Castle percentages are based directly on native explosive damage, so the two settings do not multiply each other.
- After a castle is breached, TNT can either use its tier's normal-damage percentage or continue using its configured castle-wall percentage.
- TNT still respects Opt-In Raiding, Purchased ORP, and Offline Raid Protection before damage is enabled.

### Siege Golems

- Can automatically change Siege Golem health as the server ages.
- Supports a persistent manual health override and commands for inspecting available levels.
- Includes an admin command for transforming a player into a Siege Golem.

### Raid Interference

- Detects third parties entering an active siege who are neither attackers nor defenders.
- Applies an interference burn to discourage outside participation.
- Supports exemptions for administrators and Bear Form.
- Can disable interference handling for offline or decaying bases.

### Map Alerts

- Optional icons for eligible offline-base and decayed-base raids.
- Optional passive icons for opted-in and opted-out bases.
- Configurable icon prefab selection and duration after the last eligible raid hit.
- Includes an admin command to clear active RaidForge icons.

### Soul Shard Rules

- Configure the maximum allowed count for each tracked Soul Shard type.
- Optionally revoke Offline Raid Protection from an owner or clan holding a tracked shard.
- Shard ownership is refreshed against live world state.

## Per-Castle Servant Limits

Servant limits are configured in `ServantLimits.cfg`. On world initialization, RaidForge discovers every regular `CHAR_` prefab that the game marks as convertible and creates a blank entry for it.

The control settings appear at the top of the file:

```ini
[00 - General]

EnableServantLimits = false
EnableDetailedLogging = false
```

Both settings are disabled by default for new installations.

Under `[Character Limits]`, blank entries are unlimited:

```ini
CHAR_Militia_Longbowman =
```

Set a non-negative number to create a per-castle maximum:

```ini
CHAR_Militia_Longbowman = 2
```

Use `0` to block that character type completely:

```ini
CHAR_Militia_Longbowman = 0
```

Important behavior:

- Configure the regular captured character, not the final `_Servant` prefab.
- RaidForge resolves the final servant prefab from the game's conversion component.
- Counts are isolated by `CastleHeartConnection`. Every clan member using the same base shares the same count.
- A coffin connected to another castle heart belongs to a different base and has an independent count.
- Converting, alive, mission-assigned, dead/revivable, and reviving servants count because their occupied coffin remains connected to the castle.
- A short pending reservation prevents two simultaneous Insert actions from bypassing the same limit.
- Rejection occurs before the dominated NPC is consumed.
- The player sees: `This castle has reached the maximum number of <type> servants.`

The limiter is event-driven. It runs only for relevant servant coffin Insert actions, scans the server's servant-coffin set, and counts only occupied coffins connected to the interacted castle heart. It does not run every frame.

When `EnableDetailedLogging = false`, per-action mapping, coffin, count, reservation, and allow/block diagnostics are skipped. Startup summaries and genuine warnings or errors may still be logged.

## Configuration Files

RaidForge creates these files under `BepInEx/config/RaidForge/`:

- `RaidScheduleAndGeneral.cfg` — Raid windows, schedule display/offset, Waygate policy, and raid-status display options.
- `OfflineProtection.cfg` — ORP toggle, day schedule, logout grace period, and raid announcements.
- `OptInRaiding.cfg` — Opt-in defaults, locks, automatic state changes, and shard-holder behavior.
- `OptInSchedule.cfg` — Days when the Opt-In system is allowed or overridden.
- `PurchasedORP.cfg` — Purchased protection, currency, price, display name, and purchase maximum.
- `WeaponRaiding.cfg` — Weapon structure raiding and the stone-structure multiplier.
- `TntDamageAndRaiding.cfg` — T01/T02 ordinary damage, castle-wall damage, TNT raiding, and post-breach behavior.
- `CommandSettings.cfg` — Optional startup-only command names, shorthands, disabling, and normal VCF help visibility.
- `RaidInterference.cfg` — Third-party interference and exemptions.
- `MapIcons.cfg` — Offline, decay, opt-in, and opt-out map icon behavior.
- `ServantLimits.cfg` — Generated convertible-character limits per castle.
- `SoulShards.cfg` — Shard count rules and ORP eligibility.
- `GolemSettings.cfg` — Day-based Siege Golem health automation and overrides.
- `Troubleshooting.cfg` — Verbose RaidForge diagnostics. Keep this disabled unless actively troubleshooting.

Use `.reloadraidforge` after editing runtime configuration files, then review the server log to confirm that the new values were accepted. `CommandSettings.cfg` is the exception: command names, shorthands, enabled states, and help visibility are registered at startup and require a full server restart.

## Commands

RaidForge commands use the configured RaidForge schedule clock and display label, not the player's local client clock.

All names below are defaults. RaidForge uses normal VampireCommandFramework help: `.help` lists available plugins, `.help RaidForge` lists RaidForge commands, and `.help <command>` shows detailed command help.

Custom command behavior is off by default. Set `EnableCustomCommandSettings = true` in `CommandSettings.cfg` to enable the per-command names, shorthands, disabling, and VCF help visibility options. Command settings are registered once during startup, so every command change requires a full server restart; `.reloadraidforge` intentionally does not apply them. Administrator-only permissions remain fixed in code.

### Player Commands

- `.raidtime` / `.raidt` — Show whether raids are active or the time until the next raid window.
- `.raiddays` / `.raidd` — Display the weekly raid schedule.
- `.raidstatus <PlayerName>` / `.raids <PlayerName>` — Display a player or clan's raid vulnerability status.
- `.raidoptin` — Opt the player or clan into raiding when Opt-In Raiding is active.
- `.raidoptout` — Opt out after the configured lock and schedule checks pass.
- `.raidoptstatus` — Show the current opt status and remaining lock time.
- `.raidoptlist [page]` — List manually opted-in owners.
- `.buyorp <days>` — Purchase configured raid-day protection.
- `.buyorpstatus` / `.orpamount` — Show remaining purchased ORP raid days.

### Administrator Commands

- `.reloadraidforge` — Reload runtime configuration files; command settings still require a restart.
- `.raidon` / `.raidoff` — Force the global raid state on or off.
- `.raidauto` — Clear the manual override and resume the configured schedule.
- `.raidstatusreason <PlayerName>` — Explain an owner's ORP decision.
- `.removeorp <PlayerName>` — Remove ORP until that owner reconnects.
- `.forceopt <PlayerName> <in|out>` — Force a player or clan's opt state.
- `.givebuyorp <PlayerName> <amount>` — Grant purchased ORP credits.
- `.removebuyorp <PlayerName> <amount>` — Remove purchased ORP credits.
- `.clearraidforgeicons` — Clear all active RaidForge map icons.
- `.golemstartdate` — Set the Siege Golem automation start time.
- `.golemcurrent` — Display the current Siege Golem health settings.
- `.golemsethp <LevelName>` — Persist a manual Siege Golem health level.
- `.golemauto` — Clear the manual Golem override and resume automation.
- `.golemlist` — List available Siege Golem health levels.
- `.golem [PlayerName]` — Transform the target or issuing administrator into a Siege Golem.
- `.raidrefreshcache` — Rebuild RaidForge's owner, player, and castle caches.
- `.raidforge ?` / `.raidforge status` / `.raidforge cache` — Show RaidForge help, status, and cached counts.
- `.raidconfigview ?` / `.raidconfigview <number>` — Display a specific loaded configuration section.
- `.raidconfigviewall` — Display all loaded RaidForge configuration values.

## Building from Source

1. Install the .NET 6 SDK.
2. Place compatible HookDOTS.API and VampireCommandFramework DLLs in `libs/`.
3. From the repository root, run:

```powershell
dotnet build RaidForge.sln -c Release
```

The compiled mod is written to `bin/Release/net6.0/RaidForge.dll`.

## Bugs and Support

Bugs and edge cases can happen. If you find one, please report it so it can be reproduced and resolved.

Join the [VArena Discord](https://discord.gg/varena) and open a ticket in the appropriate support area. You may also contact **Darrean (inility#4118)** directly for RaidForge support. Include:

- RaidForge version
- V Rising dedicated-server version
- A clear description of what happened and what you expected
- Reproduction steps
- Relevant RaidForge configuration values
- The matching section of `BepInEx/LogOutput.log`

Do not include passwords, authentication tokens, private server credentials, or an entire save file in a public report.

## Disclaimer

RaidForge is an unofficial community-made mod. It is not affiliated with, endorsed by, sponsored by, or officially supported by Stunlock Studios or the V Rising team. V Rising and related names and marks belong to their respective owners.

Use RaidForge at your own risk. Mods can conflict, game or dependency updates can change behavior, and bugs may affect gameplay or server state. Maintain backups and test configuration changes before using them on a production server. Please report RaidForge issues through the mod-support process above rather than to official V Rising support.

## AI Assistance Disclosure

In order to align with modern standards , RaidForge was designed and hand coded at first, however  AI-assisted tools were used to help review and refactor code, troubleshoot issues, and draft portions of the setup guide and release notes. Every released change remains subject to human testing, however some issues may still arise.

## Special Thanks

Special thanks to the following players for testing, feedback, ideas, bug reports, and other help:

- **helskog**
- **Mitch (zfolmt)**
- **Amingo**
- **Thiaz**
- **Rendy**

Developer: **Darrean (inility#4118)**

## License and Use

RaidForge is provided for non-commercial use. You may use, modify, and redistribute the project, but you may not sell the mod or derivative works. The software is provided as-is and without warranty.
