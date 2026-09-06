# Opt-in regression checks

Run with a .NET 8 or newer SDK:

```powershell
dotnet run --project tests/RaidForge.OptIn.Tests -c Release
```

The dependency-free executable links the actual opt-in service, event-driven ownership tracker,
configuration bindings, CSV parser, and file writer. Minimal test substitutes model
the Unity entity world, shard inventory helpers, BepInEx, frame-delayed scheduling,
and map icon notifications.
It uses temporary configuration directories, never a real server's configuration.

These tests cover policy, clan aggregation, offline users, pedestal ownership,
pickup/drop/transfer/equipment events, source destruction, automatic opt-out,
persistence, resource cleanup, duplicate-event coalescing, and failure handling.
They also assert that idle lookups and event updates do not run world queries.
They do **not** execute Unity/IL2CPP, actual Harmony/HookDOTS delivery, real inventory/equipment discovery,
damage interception, or actual map icon rendering. Those require the dedicated-server
checks in `docs/OPT_IN_GUIDE.md`.
