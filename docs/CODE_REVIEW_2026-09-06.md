# Focused code review — September 6, 2026

Reviewed the `main` source starting at `b939385378ea00c71b52065cb23fb16744770c45`,
with emphasis on opt-in/ORP damage decisions, ownership, commands, map status,
scheduling, and persistence. This is a source review and build/regression check,
not an exhaustive security audit or a live-server test.

## Corrected in 3.2.3

- `AutoOptInShardHolders` existed only as a configuration value and display field.
  Effective opt-in now checks actual shard possession independently of standard
  ORP. Both attacker and defender checks already call the shared opt-in service.
- A saved manual opt-out or automatic cooldown expiry could leave a shard holder
  protected. Live possession now overrides that saved state without erasing it.
- `.raidoptout`, `.forceopt ... out`, `.raidoptstatus`, and passive opt-out icons
  now account for the shard override. Manual `.raidoptin` remains available to save
  a lasting preference even when possession is already forcing participation.
- The old damage fast path used persisted ORP shard records even with ORP disabled.
  It now requires active ORP, preventing stale records from bypassing opt-in and
  its attacker-participation check.
- Shard inventory checks now ignore zero-count slots; pedestal checks share one
  helper and also recognize the item's prefab when the inventory type is absent.
- The auto-opt-out configuration description now states the actual five-minute
  check interval and its restriction to default opted-out mode.

## Follow-up in 3.2.4: replace polling with events

Removed the one-second world scan and all lazy scan calls from damage/status/map
lookups. Initial state is rebuilt at startup or an explicit configuration/cache
reload. Inventory/equipment notifications identify one player or pedestal; only
that source is re-read, after two server frames. Multiple source contributions
are counted per player/clan. Lifecycle callbacks cover login/logout, clan changes,
castle claims and destruction. Duplicate events are coalesced into one pending job.

The hook choices were checked against the MCP decompiled game definitions for
`ReactToInventoryChangedSystem` / `InventoryChangedEvent`,
`ReactToEquipmentChangedSystem` / `EquipmentChangedEvent`, and
`ProcessDestroyEventSystem` / `DestroyTagEvent`, plus inventory/equipment hook
examples in ModCore and pickup/transfer examples in VArenaShards. `InventoryConnection`
connects an external inventory to its owner. `PlayerCharacter.UserEntity` identifies
the player; equipped-item ownership can also follow `InventoryItem`, `Attach`, or
`EntityOwner`. No ModCore or VArena dependency was added.

These are actual change notifications rather than pickup requests, so a failed
pickup cannot force opt-in merely because it was attempted. The game still calls
the patched event systems during updates; an empty event query returns immediately
without allocating an entity array or reading inventories. No timer polls shards.

## Remaining findings

### P1 — CSV state writes are not atomic

`Utils/SharedFileIO.cs`, `WriteAllTextWithRetry`, opens the destination with
`FileMode.Create`. A crash or interrupted write after truncation can leave an empty
or partial opt-state, shard, or purchased-protection file. Opt-state loading clears
the in-memory dictionaries before reading and accepts valid rows from a partial
file; in default opted-in mode, missing opt-out rows make those owners raidable.

Recommended follow-up: write a complete temporary file in the same directory,
flush it, then atomically replace the destination, retaining a last-good backup.
Parse reloads into temporary dictionaries and only publish a validated result.
Flush queued writes before a forced reload to avoid reading an older saved state.

### P2 — Day-based opt-in/ORP rules use a different clock from raid windows

`Config/RaidConfig.cs`, `GetRaidScheduleNow`, applies
`RaidScheduleDisplayOffsetHours`. `Config/OptInScheduleConfig.cs`,
`IsOptInSystemAllowedToday`, and `Config/OfflineRaidProtectionConfig.cs`,
`IsOfflineProtectionAllowedToday`, use the unadjusted `DateTime.Now.DayOfWeek`.
For example, with server time Friday 23:30 and an offset of +2 hours, raid windows
use Saturday 01:30 while the opt-in day rule still uses Friday.

Recommended follow-up: define one clock for all raid-related day policies and add
offset/midnight/overnight tests. Until then, keep the offset at zero when relying
on day overrides, or account explicitly for this difference in the configuration.

### P2 — General raid status and passive icons omit the forced-day override

`Patches/DamageInterceptorPatch.cs`, `TryBlockByOptInRules`, treats both sides as
participating on forced raid days. `Commands/OptCommands.cs`, `StatusCommand`, also
reports that override. However, the opt-in branch of `.raidstatus` in
`Commands/RaidCommands.cs` and passive icon decisions in
`Services/RaidMapIconService.cs` use the ordinary effective opt-in state without
the forced-day rule. An opted-out non-shard owner can therefore appear protected
in those views while damage checks permit raiding that day.

Recommended follow-up: centralize effective raid eligibility, including a reason
such as manual choice, shard possession, or forced day, and use it consistently
for damage, commands, and icons. The recommended setup guide leaves the optional
day override disabled; `.raidoptstatus` is the appropriate opt-in status command.

### P2 — Floating dependencies make builds harder to reproduce

`RaidForge.csproj` references `BepInEx.PluginInfoProps` with `1.*` and
`VRising.Unhollowed.Client` with `1.1.8.*`, while HookDOTS.API and
VampireCommandFramework are locally supplied binaries. The same source can resolve
different dependencies on a later build or another machine.

Recommended follow-up: pin the tested package versions, use a restore lock file,
and document compatible versions/checksums for the local DLLs.

## Validation and limitations

- The original source compiled successfully before changes with locally available
  HookDOTS.API and VampireCommandFramework dependencies.
- The 3.2.4 Release build succeeds with zero warnings and zero errors.
- All 34 opt-in regression checks pass. The tests link production opt-in policy,
  ownership event handling, configuration, CSV parsing and writing. They cover both
  default modes, clan aggregation, offline/disabled users, pedestal ownership,
  transfers, saved preferences, cooldown expiry, restart behavior, duplicate-event
  batching, source destruction, failure handling, and query/array disposal. Idle
  lookups and gameplay event updates are checked for zero world-query creation.
- Unity/BepInEx world access and inventory helpers are substituted in the harness.
  It does not prove native event delivery, real equipped-slot/pedestal detection, combat hooks, or
  rendered map icons. Use the dedicated-server checklist in `OPT_IN_GUIDE.md`.
- Updates are deferred two server frames to let inventory/equipment mutations
  settle. A failed source update keeps its prior state and logs the failure;
  `.raidrefreshcache` rebuilds explicitly. Mods that directly alter inventories
  without emitting game change events may require this manual recovery. There is
  no periodic scan fallback. Passive icons use the existing five-second cleanup.
- No running game server was modified or used for these checks.
