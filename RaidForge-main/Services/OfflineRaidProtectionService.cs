using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using RaidForge.Config;
using RaidForge.Utils;
using RaidForge.Services;
using ProjectM.CastleBuilding;

namespace RaidForge.Services
{
    public static class OfflineProtectionService
    {
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