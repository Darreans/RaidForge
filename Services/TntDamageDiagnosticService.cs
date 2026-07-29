using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ProjectM;
using ProjectM.Network;
using RaidForge.Data;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Entities;

namespace RaidForge.Services
{
    internal static class TntDamageDiagnosticService
    {
        private static readonly TimeSpan SourceLogCooldown = TimeSpan.FromSeconds(2);
        private const int MaxTrackedSources = 256;
        private const int MaxGraphDepth = 4;
        private const int MaxGraphNodes = 16;

        private static readonly Dictionary<Entity, DateTime> _lastLogBySource = new();

        public static void ResetRuntimeState()
        {
            _lastLogBySource.Clear();
        }

        public static void TryLogCastleDamageEvent(
            EntityManager entityManager,
            Entity damageEventEntity,
            DealDamageEvent damageEvent,
            DateTime nowUtc)
        {
            try
            {
                Entity throttleKey = entityManager.Exists(damageEvent.SpellSource)
                    ? damageEvent.SpellSource
                    : damageEvent.Target;

                if (throttleKey != Entity.Null &&
                    _lastLogBySource.TryGetValue(throttleKey, out DateTime lastLoggedUtc) &&
                    nowUtc - lastLoggedUtc < SourceLogCooldown)
                {
                    return;
                }

                if (_lastLogBySource.Count >= MaxTrackedSources)
                {
                    _lastLogBySource.Clear();
                }

                if (throttleKey != Entity.Null)
                {
                    _lastLogBySource[throttleKey] = nowUtc;
                }

                EntityTypeModifiers modifiers = damageEvent.MaterialModifiers;
                var message = new StringBuilder(1024);

                message.Append("[TNT-DIAG] Castle DealDamage event ");
                message.Append(FormatEntityId(damageEventEntity));
                message.Append(" target=");
                AppendEntityDescription(message, entityManager, damageEvent.Target);
                message.Append(" source=");
                AppendEntityDescription(message, entityManager, damageEvent.SpellSource);
                message.Append(" damage={type=");
                message.Append(damageEvent.MainType);
                message.Append(", main=");
                message.Append(FormatFloat(damageEvent.MainFactor));
                message.Append(", raw=");
                message.Append(FormatFloat(damageEvent.RawDamage));
                message.Append(", rawPercent=");
                message.Append(FormatFloat(damageEvent.RawDamagePercent));
                message.Append(", modifier=");
                message.Append(FormatFloat(damageEvent.Modifier));
                message.Append(", flags=");
                message.Append(damageEvent.DealDamageFlags.ToString(CultureInfo.InvariantCulture));
                message.Append("} materials={castle=");
                message.Append(FormatFloat(modifiers.CastleObject));
                message.Append(", basic=");
                message.Append(FormatFloat(modifiers.BasicStructure));
                message.Append(", reinforced=");
                message.Append(FormatFloat(modifiers.ReinforcedStructure));
                message.Append(", fortified=");
                message.Append(FormatFloat(modifiers.FortifiedStructure));
                message.Append(", stone=");
                message.Append(FormatFloat(modifiers.StoneStructure));
                message.Append(", explosives=");
                message.Append(FormatFloat(modifiers.Explosives));
                message.Append(", siegeAltar=");
                message.Append(FormatFloat(modifiers.SiegeAltar));
                message.Append("} sourceGraph=");
                AppendSourceGraph(message, entityManager, damageEvent.SpellSource);

                LoggingHelper.Info(message.ToString());
            }
            catch (Exception ex)
            {
                LoggingHelper.Warning("[TNT-DIAG] Failed to describe a castle damage event.", ex);
            }
        }

        private static void AppendSourceGraph(
            StringBuilder message,
            EntityManager entityManager,
            Entity rootEntity)
        {
            if (rootEntity == Entity.Null || !entityManager.Exists(rootEntity))
            {
                message.Append("[unavailable]");
                return;
            }

            var pending = new Queue<GraphNode>();
            var visited = new HashSet<Entity>();
            pending.Enqueue(new GraphNode(rootEntity, 0, "root"));

            int writtenNodes = 0;

            while (pending.Count > 0 && writtenNodes < MaxGraphNodes)
            {
                GraphNode node = pending.Dequeue();

                if (node.Entity == Entity.Null ||
                    !entityManager.Exists(node.Entity) ||
                    !visited.Add(node.Entity))
                {
                    continue;
                }

                if (writtenNodes > 0)
                {
                    message.Append(" -> ");
                }

                message.Append('[');
                message.Append(node.Relation);
                message.Append(':');
                AppendEntityDescription(message, entityManager, node.Entity);
                message.Append(']');
                writtenNodes++;

                if (node.Depth >= MaxGraphDepth)
                {
                    continue;
                }

                int nextDepth = node.Depth + 1;

                if (entityManager.TryGetComponentData(node.Entity, out EntityOwner entityOwner))
                {
                    EnqueueIfValid(pending, entityOwner.Owner, nextDepth, "EntityOwner");
                }

                if (entityManager.TryGetComponentData(node.Entity, out EntityCreator entityCreator))
                {
                    EnqueueIfValid(pending, entityCreator.Creator._Entity, nextDepth, "EntityCreator");
                }

                if (entityManager.TryGetComponentData(node.Entity, out UserOwner userOwner))
                {
                    EnqueueIfValid(pending, userOwner.Owner._Entity, nextDepth, "UserOwner");
                }

                if (entityManager.TryGetComponentData(node.Entity, out AbilityOwner abilityOwner))
                {
                    EnqueueIfValid(pending, abilityOwner.AbilityGroup._Entity, nextDepth, "AbilityGroup");
                    EnqueueIfValid(pending, abilityOwner.Ability._Entity, nextDepth, "Ability");
                }
            }

            if (writtenNodes == 0)
            {
                message.Append("[no live source nodes]");
            }
            else if (pending.Count > 0)
            {
                message.Append(" -> [truncated]");
            }
        }

        private static void EnqueueIfValid(
            Queue<GraphNode> pending,
            Entity entity,
            int depth,
            string relation)
        {
            if (entity != Entity.Null)
            {
                pending.Enqueue(new GraphNode(entity, depth, relation));
            }
        }

        private static void AppendEntityDescription(
            StringBuilder message,
            EntityManager entityManager,
            Entity entity)
        {
            message.Append(FormatEntityId(entity));

            if (entity == Entity.Null || !entityManager.Exists(entity))
            {
                message.Append("(missing)");
                return;
            }

            if (entityManager.TryGetComponentData(entity, out PrefabGUID prefabGuid))
            {
                message.Append('(');

                if (PrefabGuidResolver.TryGetPrefabName(prefabGuid, out string prefabName))
                {
                    message.Append(prefabName);
                }
                else
                {
                    message.Append("unknown-prefab");
                }

                message.Append('#');
                message.Append(prefabGuid.GuidHash.ToString(CultureInfo.InvariantCulture));

                if (PrefabData.IsTntItemPrefab(prefabGuid))
                {
                    message.Append(",tnt-item");
                }
                else if (PrefabData.IsTntPlaceablePrefab(prefabGuid))
                {
                    message.Append(",tnt-placeable");
                }

                message.Append(')');
            }
            else
            {
                message.Append("(no-prefab)");
            }

            message.Append('{');
            bool needsSeparator = false;
            AppendTag(message, entityManager.HasComponent<Explosive>(entity), "Explosive", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<EntityOwner>(entity), "EntityOwner", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<EntityCreator>(entity), "EntityCreator", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<UserOwner>(entity), "UserOwner", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<AbilityOwner>(entity), "AbilityOwner", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<PlayerCharacter>(entity), "PlayerCharacter", ref needsSeparator);
            AppendTag(message, entityManager.HasComponent<User>(entity), "User", ref needsSeparator);
            message.Append('}');
        }

        private static void AppendTag(
            StringBuilder message,
            bool include,
            string tag,
            ref bool needsSeparator)
        {
            if (!include)
            {
                return;
            }

            if (needsSeparator)
            {
                message.Append(',');
            }

            message.Append(tag);
            needsSeparator = true;
        }

        private static string FormatEntityId(Entity entity)
        {
            return entity == Entity.Null
                ? "null"
                : $"{entity.Index.ToString(CultureInfo.InvariantCulture)}:{entity.Version.ToString(CultureInfo.InvariantCulture)}";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private readonly struct GraphNode
        {
            public Entity Entity { get; }
            public int Depth { get; }
            public string Relation { get; }

            public GraphNode(Entity entity, int depth, string relation)
            {
                Entity = entity;
                Depth = depth;
                Relation = relation;
            }
        }
    }
}
