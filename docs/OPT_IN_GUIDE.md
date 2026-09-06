# Configuring Opt-In Raiding

## Recommended setup: voluntary raiding, mandatory for Soul Shard holders

Install the updated `RaidForge.dll` while the server is stopped. Configuration files
live in `BepInEx/config/RaidForge/` and are created on first startup.

In `OfflineProtection.cfg`, disable standard ORP. The file key is
**`EnableOfflineProtection`**, even though the C# field has a longer name:

```ini
[OfflineRaidProtection]
EnableOfflineProtection = false
```

In `OptInRaiding.cfg`:

```ini
[Opt-In Raiding]
EnableOptInRaiding = true
DefaultEveryoneOptedOut = true
DefaultEveryoneOptedIn = false
OptInLockDurationHours = 24
BlockOptInChangesDuringRaidHours = true
AutoOptOutAfterCooldown = false
AutoOptInShardHolders = true
```

In `PurchasedORP.cfg`, keep the separate purchased-protection mode disabled:

```ini
[Purchased ORP]
EnablePurchasedOrp = false
```

In `OptInSchedule.cfg`, keep the opt-in rules in effect every day:

```ini
[Opt-In Schedule]
EnableOptInSchedule = false
```

Standard ORP takes priority if both systems are enabled. Purchased ORP only runs
when both standard ORP and Opt-In Raiding are disabled. `DisableOrpForShardHolders`
in `SoulShards.cfg` controls the separate ORP mode; it is not needed to force shard
holders into Opt-In Raiding.

## Choose when raiding is possible

Opt-in status determines participation; the global raid schedule determines when
castle damage is enabled. For example, in `RaidScheduleAndGeneral.cfg`:

```ini
[DailyRaidSchedule]
MondayStartTime = 20:00
MondayEndTime = 22:00
TuesdayStartTime = 20:00
TuesdayEndTime = 22:00
WednesdayStartTime = 20:00
WednesdayEndTime = 22:00
ThursdayStartTime = 20:00
ThursdayEndTime = 22:00
FridayStartTime = 20:00
FridayEndTime = 22:00
SaturdayStartTime = 20:00
SaturdayEndTime = 22:00
SundayStartTime = 20:00
SundayEndTime = 22:00
```

Choose your own times. Both times at `00:00` mean no scheduled raids that day.
The schedule uses the server's local clock plus `RaidScheduleDisplayOffsetHours`.
`RaidScheduleTimeZoneForDisplay` only changes the displayed label.

In the active save's `ServerGameSettings.json`, use `CastleDamageMode` =
`"TimeRestricted"` and set all vanilla weekday/weekend castle raid start/end hours
and minutes to zero. See the full JSON example in the repository README. RaidForge
temporarily changes castle damage to `Always` during its own configured windows.

Restart after replacing the DLL. Later configuration-only changes can be applied
with `.reloadraidforge`. Use `.raidauto` if an administrator previously used
`.raidon` or `.raidoff`; those manual overrides otherwise take precedence over the
automatic schedule. Check `.raidtime` and `.raiddays` afterward.

## What players do

| Default command | Behavior |
| --- | --- |
| `.raidoptin` | Saves a manual opt-in for the player or their current clan. |
| `.raidoptout` | Saves opt-out when the time lock and raid-window restriction permit it; rejected while their owner/clan holds a shard. |
| `.raidoptstatus` | Shows effective status, the shard override when applicable, and ordinary lock information. |
| `.raidoptlist` | Lists saved manual exceptions; it is not a list of every automatically forced shard holder. |
| `.raidtime` / `.raidt` | Shows the global raid window. |

With the recommended settings, non-shard owners start protected. After opting in,
they cannot opt out for 24 hours. The 24 hours are elapsed real time, not raid hours.
Afterward they remain opted in until they use `.raidoptout`. Status changes are
blocked during an active raid window with the recommended setting.

An attacker must also be opted in to damage an opted-in defender. Shard possession
counts for both attackers and defenders. The existing breached/decaying-base
exceptions bypass the normal opt-in checks.

Command names above are defaults. If customized in `CommandSettings.cfg`, use the
configured names. Those command configuration changes require a restart.

## What shard possession does in 3.2.3

- Any member's carried or equipped tracked Soul Shard forces the shared clan owner
  in. Solo players use their individual owner identity.
- A tracked shard stored in an owned Soul Shard pedestal also counts. An empty
  pedestal does not. Ground drops and arbitrary storage introduced by other mods
  are not covered by this detector.
- Every base sharing that player/clan owner becomes eligible during raid windows,
  including while its defenders are offline. This is not limited to the castle
  containing the pedestal.
- Player `.raidoptout` and admin `.forceopt <name> out` cannot override possession.
  An administrator can remove the shards or disable `AutoOptInShardHolders`.
- Possession never writes a permanent opt-in or starts a new manual cooldown.
  When the last shard is gone, the owner's saved preference applies again.
  A holder can use `.raidoptin` outside restricted hours to save an opt-in that
  will remain after losing their shards.
- Ownership is refreshed about once per second on the server thread. Changes can
  take about a second to affect damage/status; passive map icons are maintained on
  the existing five-second cleanup cycle. Server stalls can extend these delays.
- A failed scan retains the last complete snapshot, logs an error, and retries
  after ten seconds. Check the server log if ownership stops updating.

This uses live ownership rather than the persisted ORP vulnerability latch, so
normal ORP can remain disabled and old ORP records cannot override opt-in rules.

## Alternative settings

**Everyone starts opted in:** set `DefaultEveryoneOptedOut = false` and
`DefaultEveryoneOptedIn = true`. Players then save opt-out exceptions. Their
configured time lock applies before switching back in. Shard holders override
those exceptions. If both defaults are enabled at initialization, opted-out wins.

**Automatically expire voluntary opt-ins:** set `AutoOptOutAfterCooldown = true`.
In default opted-out mode, the five-minute cooldown task removes expired manual
opt-ins. Shard holders remain effectively opted in until their shards are gone.
This option does nothing in default opted-in mode.

**Force everybody into raids on selected days:** set `EnableOptInSchedule = true`
and set the relevant `<Day>AllowOptInSystem = false`. For example,
`SaturdayAllowOptInSystem = false` makes everybody eligible during Saturday raid
windows. This does **not** disable raids on Saturday. When enabled, this schedule
defaults to normal opt-in rules Monday-Friday and forced participation on weekends.
Currently these day toggles use the unadjusted server-local date; see the review
notes before combining them with a nonzero schedule offset.

## Dedicated-server verification

The automated regression harness does not run the game engine. On a test server:

1. Apply the recommended settings, start with an opted-out solo defender and an
   opted-in attacker, and use an active raid window. Verify normal castle damage
   is blocked without a shard.
2. Test each supported shard type in inventory, equipped, and in a matching owned
   pedestal. After a scan, verify `.raidoptstatus` says forced in, damage is allowed,
   and `.raidoptout` and `.forceopt <name> out` are rejected.
3. Repeat with a different clan member carrying the shard, that member offline,
   and a different castle belonging to the same clan.
4. Check non-opted-in attackers are still blocked, then give an attacker a shard
   and verify their effective participation.
5. Transfer or remove the last shard, leave/join a clan, change pedestal ownership,
   and restart with a shard in a sleeper's inventory and in a pedestal. Verify
   the new owner gets the override and saved preferences survive.
6. Repeat in default opted-in mode with a saved opt-out. Confirm its protected map
   icon disappears while forced in and returns after losing the last shard.
7. Exercise auto opt-out expiry and configuration reload. Turning off shard auto
   opt-in must restore normal saved/default behavior. Turning on ORP must disable
   the opt-in system.
8. End the global raid window and confirm normal castle protection resumes.
   Test siege golems, weapon raiding and TNT separately if enabled.
