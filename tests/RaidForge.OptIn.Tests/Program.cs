using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Utils;
using Unity.Entities;

var tests = new (string Name, Action Run)[]
{
    ("Default opted-out owner remains protected without shards", () =>
    {
        TestWorld.AddUser(1, "solo");
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Carried shard overrides default opt-out without creating a saved entry", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
        Check(!OptInRaidService.TryGetManualStateTime("solo", out _));
        Check(!File.Exists(Path.Combine(Paths.ConfigPath, "RaidForge", "Data", "RaidForge_OptInState.csv")));
    }),
    ("A second clan member's shard forces the shared clan owner in", () =>
    {
        TestWorld.AddUser(1, "clan");
        TestWorld.AddUser(2, "clan", shard: true);
        TestWorld.AddUser(3, "other");
        Check(OptInRaidService.IsOptedIn("clan"));
        Check(!OptInRaidService.IsOptedIn("other"));
    }),
    ("Disabled/offline users carrying shards are included", () =>
    {
        TestWorld.AddUser(1, "offline", shard: true, disabled: true);
        Check(OptInRaidService.IsOptedIn("offline"));
    }),
    ("Owned pedestal shard forces the owner in", () =>
    {
        var user = TestWorld.AddUser(1, "clan");
        TestWorld.AddPedestal(20, user);
        Check(OptInRaidService.IsOptedIn("clan"));
    }),
    ("Empty pedestal and unrelated prefab do not force opt-in", () =>
    {
        var user = TestWorld.AddUser(1, "solo");
        TestWorld.AddPedestal(20, user, shard: false);
        TestWorld.AddPedestal(21, user, prefab: 999);
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Manual opt-out exception remains saved underneath shard override", () =>
    {
        DefaultIn();
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptOut("solo", "Solo");
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
    }),
    ("Direct opt-out mutation cannot defeat effective shard opt-in", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptIn("solo", "Solo");
        OptInRaidService.OptOut("solo", "Solo");
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
    }),
    ("Automatic cooldown expiry cannot protect a shard holder", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptIn("solo", "Solo");
        OptInRaidService.ProcessAutoOptOut(0);
        Check(!OptInRaidService.IsSavedOptedIn("solo"));
        Check(OptInRaidService.IsOptedIn("solo"));
    }),
    ("Last shard removal restores default protection", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.CarriedShards.Clear();
        NextScan();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Losing a carried shard does not clear an owned pedestal shard", () =>
    {
        var user = TestWorld.AddUser(1, "solo", shard: true);
        TestWorld.AddPedestal(20, user);
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.CarriedShards.Clear();
        NextScan();
        Check(OptInRaidService.IsOptedIn("solo"));
    }),
    ("Last shard removal restores the saved manual opt-out", () =>
    {
        DefaultIn();
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptOut("solo", "Solo");
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.CarriedShards.Clear();
        NextScan();
        Check(!OptInRaidService.IsOptedIn("solo"));
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
    }),
    ("Last shard removal preserves an explicit manual opt-in", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptIn("solo", "Solo");
        TestWorld.CarriedShards.Clear();
        NextScan();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(!OptInRaidService.IsForcedOptInByShard("solo"));
    }),
    ("Clan departure and shard transfer move the override", () =>
    {
        var user = TestWorld.AddUser(1, "old-clan", shard: true);
        Check(OptInRaidService.IsOptedIn("old-clan"));
        TestWorld.OwnerKeys[user] = "new-clan";
        NextScan();
        Check(!OptInRaidService.IsOptedIn("old-clan"));
        Check(OptInRaidService.IsOptedIn("new-clan"));
    }),
    ("Pedestal ownership change moves the override", () =>
    {
        var first = TestWorld.AddUser(1, "first");
        var second = TestWorld.AddUser(2, "second");
        TestWorld.AddPedestal(20, first);
        Check(OptInRaidService.IsOptedIn("first"));
        TestWorld.HeartOwners[new Entity(2020)] = second;
        NextScan();
        Check(!OptInRaidService.IsOptedIn("first"));
        Check(OptInRaidService.IsOptedIn("second"));
    }),
    ("Disabling AutoOptInShardHolders restores the saved preference", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        Check(OptInRaidService.IsOptedIn("solo"));
        OptInRaidingConfig.AutoOptInShardHolders.Value = false;
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("ORP takes precedence over shard opt-in", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value = true;
        Check(!OptInRaidService.IsForcedOptInByShard("solo"));
        Check(TestWorld.QueryCount == 0);
    }),
    ("Disabled opt-in system does not scan or force holders in", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidingConfig.EnableOptInRaiding.Value = false;
        ShardOwnershipService.Refresh();
        Check(!OptInRaidService.IsForcedOptInByShard("solo"));
        Check(TestWorld.QueryCount == 0);
    }),
    ("Restart rebuilds live possession and preserves saved CSV state", () =>
    {
        DefaultIn();
        TestWorld.AddUser(1, "solo", shard: true);
        OptInRaidService.OptOut("solo", "Solo, with comma");
        SharedFileIO.FlushPendingWrites();
        OptInRaidService.ResetRuntimeState();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(OptInRaidService.IsManuallyOptedOut("solo"));
        TestWorld.CarriedShards.Clear();
        OptInRaidService.ResetRuntimeState();
        Check(!OptInRaidService.IsOptedIn("solo"));
    }),
    ("Shared snapshot bounds scans across repeated damage/status checks", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        for (int i = 0; i < 100; i++) Check(OptInRaidService.IsOptedIn("solo"));
        Check(TestWorld.QueryCount == 2);
    }),
    ("Ownership changes mark icons dirty; unchanged scans do not", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        ShardOwnershipService.Refresh();
        int dirtyCount = RaidMapIconService.DirtyCount;
        NextScan();
        ShardOwnershipService.Refresh();
        Check(RaidMapIconService.DirtyCount == dirtyCount);
        TestWorld.CarriedShards.Clear();
        NextScan();
        ShardOwnershipService.Refresh();
        Check(RaidMapIconService.DirtyCount == dirtyCount + 1);
    }),
    ("Scan failure retains the complete prior snapshot and releases queries", () =>
    {
        TestWorld.AddUser(1, "solo", shard: true);
        Check(OptInRaidService.IsOptedIn("solo"));
        TestWorld.ThrowOnQuery = true;
        NextScan();
        Check(OptInRaidService.IsOptedIn("solo"));
        Check(LoggingHelper.Errors == 1);
        Check(TestWorld.ActiveArrays == 0 && TestWorld.ActiveQueries == 0);
    }),
    ("Null owner does not scan or become opted in", () =>
    {
        Check(!OptInRaidService.IsOptedIn(null));
        Check(TestWorld.QueryCount == 0);
    }),
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
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {test.Name}: {ex}");
    }
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

static void NextScan() => typeof(ShardOwnershipService)
    .GetField("_nextScanAt", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0L);
