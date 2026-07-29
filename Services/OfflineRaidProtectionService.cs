using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using RaidForge.Config;
using RaidForge.Utils;
using RaidForge.Services;
using ProjectM.CastleBuilding;
using System;

namespace RaidForge.Services
{
    public static class OfflineProtectionService
    {
        public readonly struct OfflineProtectionEvaluation
        {
            public readonly bool IsProtected;
            public readonly bool IsInGracePeriod;
            public readonly bool AllDefendersOffline;
            public readonly bool CheckedAllDefendersOffline;
            public readonly string PersistentKey;
            public readonly string Reason;

            public OfflineProtectionEvaluation(
                bool isProtected,
                bool isInGracePeriod,
                bool allDefendersOffline,
                bool checkedAllDefendersOffline,
                string persistentKey,
                string reason)
            {
                IsProtected = isProtected;
                IsInGracePeriod = isInGracePeriod;
                AllDefendersOffline = allDefendersOffline;
                CheckedAllDefendersOffline = checkedAllDefendersOffline;
                PersistentKey = persistentKey;
                Reason = reason;
            }
        }

        public static bool IsHeartCurrentlyOfflineProtected(Entity castleHeartEntity, EntityManager entityManager)
        {
            return EvaluateHeartOfflineProtection(castleHeartEntity, entityManager).IsProtected;
        }

        public static OfflineProtectionEvaluation EvaluateHeartOfflineProtection(
            Entity castleHeartEntity,
            EntityManager entityManager,
            string persistentKey = null,
            DateTime? nowUtc = null,
            float? configuredGraceMinutes = null,
            bool? offlineProtectionAllowed = null)
        {
            if (!Plugin.SystemsInitialized)
            {
                return NotProtected(persistentKey, "RaidForge systems are not initialized.");
            }

            if (entityManager == default ||
                castleHeartEntity == Entity.Null ||
                !entityManager.Exists(castleHeartEntity) ||
                !entityManager.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return NotProtected(persistentKey, "Castle Heart does not exist.");
            }

            bool orpAllowed = offlineProtectionAllowed ?? OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday();
            if (!orpAllowed)
            {
                return NotProtected(persistentKey, "Offline Raid Protection is disabled or not allowed today.");
            }

            CastleHeart castleHeart = entityManager.GetComponentData<CastleHeart>(castleHeartEntity);
            if (castleHeart.IsSieged())
            {
                return NotProtected(persistentKey, "Castle Heart is already breached/sieged.");
            }

            if (IsBaseDecaying(castleHeartEntity, entityManager))
            {
                return NotProtected(persistentKey, "Castle Heart is decaying.");
            }

            string ownerKey = persistentKey;
            if (string.IsNullOrWhiteSpace(ownerKey) &&
                !TryResolvePersistentOwnerKey(castleHeartEntity, entityManager, out ownerKey, out string ownerError))
            {
                return NotProtected(ownerKey, ownerError);
            }

            if (ShardConfig.DisableOrpForShardHolders.Value && ShardVulnerabilityService.IsVulnerable(ownerKey))
            {
                return NotProtected(ownerKey, "Owner is shard-vulnerable.");
            }

            if (!OfflineGraceService.TryGetOfflineStartTime(ownerKey, out DateTime offlineStartTimeUtc))
            {
                return NotProtected(ownerKey, "No offline timestamp exists.");
            }

            DateTime now = nowUtc ?? DateTime.UtcNow;
            float graceMinutes = configuredGraceMinutes ?? (OfflineRaidProtectionConfig.GracePeriodDurationMinutes?.Value ?? 0f);

            if (graceMinutes > 0f && (now - offlineStartTimeUtc).TotalMinutes < graceMinutes)
            {
                bool offlineDuringGrace = AreAllDefendersActuallyOffline(castleHeartEntity, entityManager);
                return new OfflineProtectionEvaluation(
                    false,
                    true,
                    offlineDuringGrace,
                    true,
                    ownerKey,
                    "Offline timestamp exists, but grace has not elapsed.");
            }

            bool allDefendersOffline = AreAllDefendersActuallyOffline(castleHeartEntity, entityManager);
            if (!allDefendersOffline)
            {
                return new OfflineProtectionEvaluation(
                    false,
                    false,
                    false,
                    true,
                    ownerKey,
                    "At least one defender is online.");
            }

            return new OfflineProtectionEvaluation(
                true,
                false,
                true,
                true,
                ownerKey,
                "Offline timestamp is past grace and all defenders are offline.");
        }

        private static OfflineProtectionEvaluation NotProtected(string persistentKey, string reason)
        {
            return new OfflineProtectionEvaluation(
                false,
                false,
                false,
                false,
                persistentKey,
                reason);
        }

        private static bool TryResolvePersistentOwnerKey(
            Entity castleHeartEntity,
            EntityManager entityManager,
            out string persistentKey,
            out string error)
        {
            persistentKey = null;
            error = string.Empty;

            if (!OwnershipCacheService.TryGetHeartOwner(castleHeartEntity, out Entity ownerUserEntity) ||
                ownerUserEntity == Entity.Null ||
                !entityManager.Exists(ownerUserEntity))
            {
                error = "Castle Heart owner is not in the ownership cache.";
                return false;
            }

            if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                entityManager,
                ownerUserEntity,
                out OwnerIdentity owner,
                out error))
            {
                return false;
            }

            persistentKey = owner.PersistentKey;
            return !string.IsNullOrWhiteSpace(persistentKey);
        }

        public static bool AreAllDefendersActuallyOffline(Entity castleHeartEntity, EntityManager entityManager)
        {
            if (!Plugin.SystemsInitialized)
            {
                return false;
            }

            if (!entityManager.Exists(castleHeartEntity) || !entityManager.HasComponent<UserOwner>(castleHeartEntity))
            {
                return false;
            }

            Entity ownerUserEntity = entityManager.GetComponentData<UserOwner>(castleHeartEntity).Owner._Entity;

            if (!entityManager.Exists(ownerUserEntity) || !entityManager.HasComponent<User>(ownerUserEntity))
            {
                return false;
            }

            User ownerUserData = entityManager.GetComponentData<User>(ownerUserEntity);
            Entity ownerClanEntity = Entity.Null;
            OwnershipCacheService.TryGetUserClan(ownerUserEntity, out ownerClanEntity);

            if (ownerClanEntity != Entity.Null && entityManager.Exists(ownerClanEntity) && entityManager.HasComponent<ClanTeam>(ownerClanEntity))
            {
                bool anyClanMemberOnline = UserHelper.IsAnyClanMemberOnline(entityManager, ownerClanEntity);
                return !anyClanMemberOnline;
            }
            else
            {
                bool isSoloOwnerOffline = !ownerUserData.IsConnected;
                return isSoloOwnerOffline;
            }
        }

        public static bool IsBaseDecaying(Entity castleHeartEntity, EntityManager entityManager)
        {
            if (!entityManager.Exists(castleHeartEntity) || !entityManager.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return true;
            }

            CastleHeart castleHeartComponent = entityManager.GetComponentData<CastleHeart>(castleHeartEntity);

            if (castleHeartComponent.FuelQuantity <= 0 || castleHeartComponent.IsDecaying())
            {
                return true;
            }
            return false;
        }
    }
}
