// Minimal substitutes for the Unity/BepInEx boundary. The opt-in service,
// ownership scan, config bindings, CSV parser and file writer are production code.
using System.Collections;
using ProjectM.Network;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Entities;

namespace BepInEx
{
    public static class Paths { public static string ConfigPath { get; set; } }
}

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public T Value { get; set; }
        public ConfigEntry(T value) => Value = value;
    }
    public sealed class ConfigFile
    {
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description) => new(value);
        public void Save() { }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Temp }
    public struct NativeArray<T> : IEnumerable<T>
    {
        private readonly T[] _items;
        public NativeArray(T[] items) { _items = items; TestWorld.ActiveArrays++; }
        public bool IsCreated => _items != null;
        public void Dispose() => TestWorld.ActiveArrays--;
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace Unity.Entities
{
    public readonly record struct Entity(int Id)
    {
        public static Entity Null => default;
    }
    public readonly record struct ComponentType(Type Type)
    {
        public static ComponentType ReadOnly<T>() => new(typeof(T));
    }
    public enum EntityQueryOptions { Default, IncludeDisabledEntities }
    public sealed class EntityQueryDesc
    {
        public ComponentType[] All;
        public EntityQueryOptions Options;
    }
    public readonly record struct EntityQuery(ComponentType[] All, EntityQueryOptions Options)
    {
        public Unity.Collections.NativeArray<Entity> ToEntityArray(Unity.Collections.Allocator allocator)
        {
            if (TestWorld.ThrowOnQuery) throw new InvalidOperationException("Simulated ECS query failure");
            var all = All;
            var options = Options;
            var entities = TestWorld.Components.Keys.Where(e =>
                all.All(c => TestWorld.Components[e].ContainsKey(c.Type)) &&
                (options == EntityQueryOptions.IncludeDisabledEntities || !TestWorld.Disabled.Contains(e))).ToArray();
            return new(entities);
        }
        public void Dispose() => TestWorld.ActiveQueries--;
    }
    public struct EntityManager
    {
        public bool Exists(Entity entity) => TestWorld.Components.ContainsKey(entity);
        public bool HasComponent<T>(Entity entity) => Exists(entity) && TestWorld.Components[entity].ContainsKey(typeof(T));
        public bool TryGetComponentData<T>(Entity entity, out T value)
        {
            value = default;
            if (!HasComponent<T>(entity)) return false;
            value = GetComponentData<T>(entity);
            return true;
        }
        public EntityQuery CreateEntityQuery(EntityQueryDesc desc)
        {
            TestWorld.QueryCount++;
            TestWorld.ActiveQueries++;
            return new(desc.All, desc.Options);
        }
        public EntityQuery CreateEntityQuery(params ComponentType[] all) => CreateEntityQuery(new EntityQueryDesc { All = all });
        public T GetComponentData<T>(Entity entity) => (T)TestWorld.Components[entity][typeof(T)];
    }
}

namespace ProjectM.Network
{
    public readonly record struct EntityReference(Entity _Entity);
    public struct User { public EntityReference LocalCharacter; }
}

namespace ProjectM
{
    public struct AttachedBuffer { }
    public struct DestroyTag { }
    public struct PlayerCharacter { public Entity UserEntity; }
    public struct InventoryConnection { public Entity InventoryOwner; }
    public struct InventoryItem { public Entity ContainerEntity; }
    public struct Attach { public Entity Parent; }
    public struct EntityOwner { public Entity Owner; }
    public struct UserOwner { public EntityReference Owner; }
    public enum EquipmentType { Weapon, MagicSource }
    public enum InventoryChangedEventType { Obtained, Removed, Moved }
    public struct InventoryChangedEvent
    {
        public PrefabGUID Item;
        public Entity ItemEntity, InventoryEntity;
        public InventoryChangedEventType ChangeType;
    }
    public struct EquipmentChangedEvent
    {
        public PrefabGUID Item;
        public Entity ItemEntity, Target;
        public EquipmentType EquipmentType;
    }
}

namespace ProjectM.CastleBuilding
{
    public struct CastleHeart { }
    public struct CastleHeartConnection { public EntityReference CastleHeartEntity; }
}

namespace Stunlock.Core
{
    public readonly record struct PrefabGUID(int GuidHash);
}

namespace RaidForge
{
    public static class Plugin { public static bool SystemsInitialized = true; }
}

namespace RaidForge.Config
{
    public static class OfflineRaidProtectionConfig
    {
        public static BepInEx.Configuration.ConfigEntry<bool> EnableOfflineRaidProtection = new(false);
    }
}

namespace RaidForge.Data
{
    public static class PrefabData
    {
        public static readonly HashSet<PrefabGUID> SoulShardPedestalPrefabGUIDs = new() { new(100) };
        public static readonly HashSet<PrefabGUID> SoulShardPrefabGUIDs = new() { new(200) };
    }
}

namespace RaidForge.Utils
{
    public readonly record struct OwnerIdentity(string PersistentKey, User UserData);
    public static class OwnerIdentityHelper
    {
        public static bool TryResolveFromUserEntity(EntityManager em, Entity user, out OwnerIdentity owner, out string error)
        {
            error = "Unknown test owner";
            owner = new(TestWorld.OwnerKeys.GetValueOrDefault(user), em.GetComponentData<User>(user));
            return owner.PersistentKey != null;
        }
    }
    public static class VWorld
    {
        public static EntityManager EntityManager => default;
        public static bool Ready = true;
        public static bool IsServerWorldReady() => Ready;
    }
    public static class LoggingHelper
    {
        public static int Errors;
        public static void Info(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message, Exception ex) => Errors++;
    }
    public static class UserHelper
    {
        public static bool TryGetSoulShardsOnPerson(EntityManager em, Entity character, out HashSet<PrefabGUID> shards, out string reason)
        {
            reason = "Test inventory/equipment";
            shards = new();
            TestWorld.PersonReads++;
            return TestWorld.CarriedShards.Contains(character) || TestWorld.EquippedShards.Contains(character);
        }
        public static bool TryGetSoulShardsInPedestal(EntityManager em, Entity pedestal, out HashSet<PrefabGUID> shards)
        {
            shards = new();
            TestWorld.PedestalReads++;
            return TestWorld.PedestalShards.Contains(pedestal);
        }
    }
}

namespace RaidForge.Services
{
    public static class RaidMapIconService
    {
        public static int DirtyCount;
        public static void MarkPersistentStateIconsDirty() => DirtyCount++;
    }
    public static class OwnershipCacheService
    {
        public static bool TryGetHeartOwner(Entity heart, out Entity user) => TestWorld.HeartOwners.TryGetValue(heart, out user);
    }
}

namespace RaidForge.Core
{
    public static class RaidForgeScheduler
    {
        private static readonly List<(Action Action, int Frames)> Pending = new();
        public static int QueuedCount => Pending.Count;
        public static void RunAfterFrames(Action action, int frames) => Pending.Add((action, frames));
        public static void Tick()
        {
            var actions = Pending.ToArray();
            Pending.Clear();
            foreach (var entry in actions)
                if (entry.Frames <= 1) entry.Action();
                else Pending.Add((entry.Action, entry.Frames - 1));
        }
        public static void Reset() => Pending.Clear();
    }
}

internal static class TestWorld
{
    public static readonly Dictionary<Entity, Dictionary<Type, object>> Components = new();
    public static readonly Dictionary<Entity, string> OwnerKeys = new();
    public static readonly Dictionary<Entity, Entity> HeartOwners = new();
    public static readonly HashSet<Entity> Disabled = new();
    public static readonly HashSet<Entity> CarriedShards = new();
    public static readonly HashSet<Entity> EquippedShards = new();
    public static readonly HashSet<Entity> PedestalShards = new();
    public static int QueryCount, ActiveQueries, ActiveArrays;
    public static int PersonReads, PedestalReads;
    public static bool ThrowOnQuery;

    public static Entity AddUser(int id, string owner, bool shard = false, bool disabled = false)
    {
        var user = new Entity(id);
        var character = new Entity(id + 1000);
        Components[user] = new() { [typeof(User)] = new User { LocalCharacter = new(character) } };
        Components[character] = new() { [typeof(ProjectM.PlayerCharacter)] = new ProjectM.PlayerCharacter { UserEntity = user } };
        Components[new Entity(id + 3000)] = new() { [typeof(ProjectM.InventoryConnection)] = new ProjectM.InventoryConnection { InventoryOwner = character } };
        OwnerKeys[user] = owner;
        if (shard) CarriedShards.Add(character);
        if (disabled) Disabled.Add(user);
        return user;
    }

    public static void AddPedestal(int id, Entity user, bool shard = true, int prefab = 100)
    {
        var pedestal = new Entity(id);
        var heart = new Entity(id + 2000);
        Components[heart] = new() { [typeof(ProjectM.CastleBuilding.CastleHeart)] = new ProjectM.CastleBuilding.CastleHeart() };
        Components[new Entity(id + 4000)] = new() { [typeof(ProjectM.InventoryConnection)] = new ProjectM.InventoryConnection { InventoryOwner = pedestal } };
        Components[pedestal] = new()
        {
            [typeof(ProjectM.AttachedBuffer)] = new ProjectM.AttachedBuffer(),
            [typeof(PrefabGUID)] = new PrefabGUID(prefab),
            [typeof(ProjectM.CastleBuilding.CastleHeartConnection)] = new ProjectM.CastleBuilding.CastleHeartConnection { CastleHeartEntity = new(heart) }
        };
        HeartOwners[heart] = user;
        if (shard) PedestalShards.Add(pedestal);
    }

    public static void Reset()
    {
        Components.Clear(); OwnerKeys.Clear(); HeartOwners.Clear(); Disabled.Clear();
        CarriedShards.Clear(); EquippedShards.Clear(); PedestalShards.Clear();
        QueryCount = ActiveQueries = ActiveArrays = 0;
        PersonReads = PedestalReads = 0;
        ThrowOnQuery = false;
    }
}
