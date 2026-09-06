using BepInEx;
using BepInEx.Configuration;
using ProjectM;
using RaidForge.Config;
using RaidForge.Core;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Entities;

var tests = new (string Name, Action Run)[]
{
    ("Bootstrap finds carried shards and preserves saved preferences", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
        Check(!OptInRaidService.TryGetManualStateTime("solo", out _));
    }),
    ("Bootstrap includes a second/offline clan member and owned pedestals", () =>
    {
        TestWorld.AddUser(1, "clan");
        TestWorld.AddUser(2, "clan", shard: true, disabled: true);
        var owner = TestWorld.AddUser(3, "pedestal-owner");
        TestWorld.AddPedestal(20, owner);
        ShardOwnershipService.Rebuild();
        Check(OptInRaidService.IsOptedIn("clan"));
        Check(OptInRaidService.IsOptedIn("pedestal-owner"));
        Check(!OptInRaidService.IsOptedIn("other"));
    }),
    ("Empty pedestals and unrelated prefabs do not force opt-in", () =>
    {
        var user = TestWorld.AddUser(1, "solo");
        TestWorld.AddPedestal(20, user, shard: false);
        TestWorld.AddPedestal(21, user, prefab: 999);
        ShardOwnershipService.Rebuild();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Pickup notification updates only the affected player after two frames", () =>
    {
        TestWorld.AddUser(1, "solo");
        TestWorld.AddUser(2, "other");
        ShardOwnershipService.Rebuild();
        int reads = TestWorld.PersonReads;
        TestWorld.CarriedShards.Add(new(1001));
        InventoryEvent(3001);
        Check(!OptInRaidService.IsOptedIn("solo"));
        RaidForgeScheduler.Tick();
        Check(!OptInRaidService.IsOptedIn("solo"));
        RaidForgeScheduler.Tick();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(TestWorld.PersonReads == reads + 1);
        Check(TestWorld.QueryCount == 2);
    }),
    ("Dropping the last shard restores protection", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Transfer updates both owners without a world scan", () =>
    {
        TestWorld.AddUser(1, "first", shard: true);
        TestWorld.AddUser(2, "second");
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        TestWorld.CarriedShards.Add(new(1002));
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        InventoryEvent(3002);
        Drain();
        Check(!OptInRaidService.IsOptedIn("first"));
        Check(OptInRaidService.IsOptedIn("second"));
        Check(TestWorld.QueryCount == 2);
    }),
    ("Equip/unequip transitions preserve forced participation", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        TestWorld.EquippedShards.Add(new(1001));
        EquipmentEvent(1001);
        Check(OptInRaidService.IsOptedIn("solo"));
        Drain();
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.EquippedShards.Clear();
        TestWorld.CarriedShards.Add(new(1001));
        EquipmentEvent(1001);
        InventoryEvent(3001);
        Drain();
        Check(OptInRaidService.IsOptedIn("solo"));
    }),
    ("Removing equipped shard updates even if item entity is already gone", () =>
    {
        TestWorld.AddUser(1, "solo");
        TestWorld.EquippedShards.Add(new(1001));
        ShardOwnershipService.Rebuild();
        TestWorld.EquippedShards.Clear();
        EquipmentEvent(1001);
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Pedestal deposit/removal updates its owner without checking other inventories", () =>
    {
        var user = TestWorld.AddUser(1, "clan");
        TestWorld.AddPedestal(20, user, shard: false);
        ShardOwnershipService.Rebuild();
        int reads = TestWorld.PersonReads;
        TestWorld.PedestalShards.Add(new(20));
        InventoryEvent(4020);
        Drain();
        Check(OptInRaidService.IsOptedIn("clan"));
        TestWorld.PedestalShards.Clear();
        InventoryEvent(4020, InventoryChangedEventType.Removed);
        Drain();
        Check(!OptInRaidService.IsOptedIn("clan"));
        Check(TestWorld.PersonReads == reads && TestWorld.QueryCount == 2);
    }),
    ("Multiple sources keep a clan forced in until the last is removed", () =>
    {
        var user = TestWorld.AddUser(1, "clan", shard: true);
        TestWorld.AddUser(2, "clan", shard: true);
        TestWorld.AddPedestal(20, user);
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        InventoryEvent(3002, InventoryChangedEventType.Removed);
        Drain();
        Check(OptInRaidService.IsOptedIn("clan"));
        TestWorld.PedestalShards.Clear();
        InventoryEvent(4020, InventoryChangedEventType.Removed);
        Drain();
        Check(!OptInRaidService.IsOptedIn("clan"));
    }),
    ("Manual opt-out cannot defeat possession in either default mode", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        OptInRaidService.OptOut("solo", "Solo");
        Check(OptInRaidService.IsOptedIn("solo"));
        DefaultIn();
        OptInRaidService.OptOut("solo", "Solo");
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
        Check(OptInRaidService.IsOptedIn("solo"));
    }),
    ("Cooldown expiry cannot protect a shard holder", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        OptInRaidService.OptIn("solo", "Solo");
        OptInRaidService.ProcessAutoOptOut(0);
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
        Check(OptInRaidService.IsOptedIn("solo"));
    }),
    ("Loss of shards restores saved manual opt-out", () =>
    {
        DefaultIn();
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptOut("solo", "Solo");
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
    }),
    ("Loss of shards preserves explicit manual opt-in", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptIn("solo", "Solo");
        ShardOwnershipService.Rebuild();
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        Drain();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsForcedOptInByShard("solo"));
    }),
    ("Clan change moves carried and pedestal sources to the new owner key", () =>
    {
        var user = TestWorld.AddUser(1, "old-clan", shard: true);
        TestWorld.AddPedestal(20, user);
        ShardOwnershipService.Rebuild();
        TestWorld.OwnerKeys[user] = "new-clan";
        ShardOwnershipService.OnUserChanged(user);
        Drain();
        Check(!OptInRaidService.IsOptedIn("old-clan"));
        Check(OptInRaidService.IsOptedIn("new-clan"));
        Check(TestWorld.QueryCount == 2);
    }),
    ("Castle claim moves pedestal ownership", () =>
    {
        var first = TestWorld.AddUser(1, "first");
        var second = TestWorld.AddUser(2, "second");
        TestWorld.AddPedestal(20, first);
        ShardOwnershipService.Rebuild();
        TestWorld.HeartOwners[new(2020)] = second;
        ShardOwnershipService.OnHeartChanged(new(2020));
        Drain();
        Check(!OptInRaidService.IsOptedIn("first"));
        Check(OptInRaidService.IsOptedIn("second"));
    }),
    ("Logout/reconnect callbacks recheck only that user's sources", () =>
    {
        var user = TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        TestWorld.Disabled.Add(user);
        ShardOwnershipService.OnUserChanged(user);
        Drain();
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.CarriedShards.Clear();
        TestWorld.Disabled.Clear();
        ShardOwnershipService.OnUserChanged(user);
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Destroyed pedestal removes its contribution", () =>
    {
        var user = TestWorld.AddUser(1, "solo");
        TestWorld.AddPedestal(20, user);
        ShardOwnershipService.Rebuild();
        ShardOwnershipService.OnDestroyed(default, new(20));
        TestWorld.Components.Remove(new(20));
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Destroyed heart removes all its pedestal contributions", () =>
    {
        var user = TestWorld.AddUser(1, "solo");
        TestWorld.AddPedestal(20, user);
        ShardOwnershipService.Rebuild();
        ShardOwnershipService.OnDestroyed(default, new(2020));
        TestWorld.Components[new(2020)][typeof(DestroyTag)] = new DestroyTag();
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Destroyed character is no longer counted as a carried source", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        ShardOwnershipService.OnDestroyed(default, new(1001));
        TestWorld.Components[new(1001)][typeof(DestroyTag)] = new DestroyTag();
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Disabling auto opt-in or enabling ORP stops event work", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        OptInRaidingConfig.AutoOptInShardHolders.Value = false;
        InventoryEvent(3001);
        Check(!OptInRaidService.IsOptedIn("solo"));
        Check(RaidForgeScheduler.QueuedCount == 0);
        OptInRaidingConfig.AutoOptInShardHolders.Value = true;
        OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value = true;
        InventoryEvent(3001);
        Check(!OptInRaidService.IsForcedOptInByShard("solo"));
        Check(RaidForgeScheduler.QueuedCount == 0);
    }),
    ("Disabled opt-in system does not bootstrap or handle events", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidingConfig.EnableOptInRaiding.Value = false;
        ShardOwnershipService.Rebuild();
        InventoryEvent(3001);
        Check(TestWorld.QueryCount == 0 && RaidForgeScheduler.QueuedCount == 0);
    }),
    ("Restart rebuilds existing shards while preserving saved CSV state", () =>
    {
        DefaultIn();
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptOut("solo", "Solo, with comma");
        SharedFileIO.FlushPendingWrites();
        OptInRaidService.ResetRuntimeState();
        ShardOwnershipService.Rebuild();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
    }),
    ("Idle damage/status lookups perform zero reads or scheduled work", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        int reads = TestWorld.PersonReads;
        for (int i = 0; i < 1000; i++)
        {
            Check(OptInRaidService.IsOptedIn("solo"));
            RaidForgeScheduler.Tick();
        }
        Check(TestWorld.QueryCount == 2 && TestWorld.PersonReads == reads);
        Check(RaidForgeScheduler.QueuedCount == 0);
    }),
    ("Duplicate inventory and equipment events coalesce into one source read", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        int reads = TestWorld.PersonReads;
        for (int i = 0; i < 100; i++) { InventoryEvent(3001); EquipmentEvent(1001); }
        Check(RaidForgeScheduler.QueuedCount == 1);
        Drain();
        Check(TestWorld.PersonReads == reads + 1);
    }),
    ("Unrelated inventory and weapon events do not enqueue rechecks", () =>
    {
        TestWorld.AddUser(1, "solo");
        ShardOwnershipService.Rebuild();
        ShardOwnershipService.OnInventoryChanged(default, new() { Item = new(999), InventoryEntity = new(3001) });
        ShardOwnershipService.OnEquipmentChanged(default, new() { Item = new(999), Target = new(1001), EquipmentType = EquipmentType.Weapon });
        Check(RaidForgeScheduler.QueuedCount == 0);
    }),
    ("Bootstrap failure retains previous ownership and releases resources", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        TestWorld.ThrowOnQuery = true;
        ShardOwnershipService.Rebuild();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(LoggingHelper.Errors == 1);
    }),
    ("Failed source update preserves it while another source still updates", () =>
    {
        var user = TestWorld.AddUser(1, "first", shard: true);
        TestWorld.AddUser(2, "second", shard: true);
        ShardOwnershipService.Rebuild();
        TestWorld.OwnerKeys.Remove(user);
        TestWorld.CarriedShards.Remove(new(1002));
        InventoryEvent(3001);
        InventoryEvent(3002, InventoryChangedEventType.Removed);
        Drain();
        Check(OptInRaidService.IsOptedIn("first"));
        Check(!OptInRaidService.IsOptedIn("second"));
        Check(LoggingHelper.Errors == 1);
    }),
    ("Runtime reset invalidates already queued callbacks", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        InventoryEvent(3001);
        ShardOwnershipService.ResetRuntimeState();
        Drain();
        Check(!ShardOwnershipService.HasShard("solo"));
        Check(TestWorld.PersonReads == 0);
    }),
    ("Unsuccessful pickup does not force participation", () =>
    {
        TestWorld.AddUser(1, "solo");
        InventoryEvent(3001);
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Equipped item destruction follows Attach when InventoryItem has no container", () =>
    {
        TestWorld.AddUser(1, "solo");
        TestWorld.EquippedShards.Add(new(1001));
        TestWorld.Components[new(7000)] = new()
        {
            [typeof(Stunlock.Core.PrefabGUID)] = new Stunlock.Core.PrefabGUID(200),
            [typeof(InventoryItem)] = new InventoryItem(),
            [typeof(Attach)] = new Attach { Parent = new(1001) }
        };
        ShardOwnershipService.Rebuild();
        ShardOwnershipService.OnDestroyed(default, new(7000));
        TestWorld.EquippedShards.Clear();
        Drain();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Unchanged source leaves icons clean; final removal marks them dirty", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Rebuild();
        int dirty = RaidMapIconService.DirtyCount;
        InventoryEvent(3001);
        Drain();
        Check(RaidMapIconService.DirtyCount == dirty);
        TestWorld.CarriedShards.Clear();
        InventoryEvent(3001, InventoryChangedEventType.Removed);
        Drain();
        Check(RaidMapIconService.DirtyCount == dirty + 1);
    }),
    ("Null owner lookup is harmless", () => Check(!OptInRaidService.IsOptedIn(null))),
    ("Auto opt-out leaves default opted-in mode unchanged", () =>
    {
        DefaultIn();
        OptInRaidService.OptOut("solo", "Solo");
        OptInRaidService.ProcessAutoOptOut(0);
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
        Check(OptInRaidService.IsSavedOptedIn("other"));
    })
};

int failed = 0;
foreach (var test in tests)
{
    SharedFileIO.FlushPendingWrites();
    Paths.ConfigPath = Path.Combine(Path.GetTempPath(), "RaidForge-OptInTests", Guid.NewGuid().ToString("N"));
    OptInRaidService.ResetRuntimeState();
    RaidForgeScheduler.Reset();
    TestWorld.Reset();
    OptInRaidingConfig.Initialize(new ConfigFile());
    OptInRaidingConfig.EnableOptInRaiding.Value = true;
    OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value = false;
    LoggingHelper.Errors = RaidMapIconService.DirtyCount = 0;
    try
    {
        test.Run();
        Check(TestWorld.ActiveArrays == 0 && TestWorld.ActiveQueries == 0);
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex}"); }
}
SharedFileIO.FlushPendingWrites();
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static void Check(bool condition)
{
    if (!condition) throw new InvalidOperationException("Assertion failed");
}

static void DefaultIn()
{
    OptInRaidingConfig.DefaultEveryoneOptedOut.Value = false;
    OptInRaidingConfig.DefaultEveryoneOptedIn.Value = true;
}

static void InventoryEvent(int inventory, InventoryChangedEventType kind = InventoryChangedEventType.Obtained) =>
    ShardOwnershipService.OnInventoryChanged(default, new() { Item = new(200), InventoryEntity = new(inventory), ChangeType = kind });

static void EquipmentEvent(int character) => ShardOwnershipService.OnEquipmentChanged(default,
    new() { EquipmentType = EquipmentType.MagicSource, Target = new(character) });

static void Drain() { RaidForgeScheduler.Tick(); RaidForgeScheduler.Tick(); }
