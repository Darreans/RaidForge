# Opt-in regression checks

Run with a .NET 8 or newer SDK:

```powershell
dotnet run --project tests/RaidForge.OptIn.Tests -c Release
```

The dependency-free executable links the actual opt-in service, ownership scanner,
configuration bindings, CSV parser, and file writer. Minimal test substitutes model
the Unity entity world, shard inventory helpers, BepInEx, and map icon notifications.
It uses temporary configuration directories, never a real server's configuration.

These tests cover policy, clan aggregation, offline users, pedestal ownership,
transfers, automatic opt-out, persistence, resource cleanup, cache throttling, and
scan failure. They do **not** execute Unity/IL2CPP, real inventory/equipment discovery,
damage interception, or actual map icon rendering. Those require the dedicated-server
checks in `docs/OPT_IN_GUIDE.md`.
