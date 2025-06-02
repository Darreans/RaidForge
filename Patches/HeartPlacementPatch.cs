using HarmonyLib;
using ProjectM;
using Unity.Entities;
using RaidForge.Services;
using RaidForge.Utils;
using ProjectM.CastleBuilding;

namespace RaidForge.Patches
{
    [HarmonyPatch(typeof(TeamUtility), nameof(TeamUtility.ClaimCastle))]
    public static class TeamUtility_ClaimCastle_Patch
    {
        private static void Postfix(EntityManager entityManager, Entity userEntity, Entity castleHeartEntity, CastleHeartLimitType limitType)
        {
            if (!VWorld.IsServerWorldReady())
            {
                return;
            }

            if (userEntity == Entity.Null || castleHeartEntity == Entity.Null)
            {
                return;
            }

            if (!entityManager.Exists(userEntity) || !entityManager.Exists(castleHeartEntity))
            {
                return;
            }

            if (entityManager.HasComponent<UserOwner>(castleHeartEntity) && entityManager.HasComponent<CastleHeart>(castleHeartEntity))
            {
                UserOwner ownerComponent = entityManager.GetComponentData<UserOwner>(castleHeartEntity);
                Entity actualOwnerEntity = ownerComponent.Owner._Entity;

                if (actualOwnerEntity == userEntity)
                {
                    OwnershipCacheService.UpdateHeartOwner(castleHeartEntity, userEntity, entityManager);
                }
                else
                {
                    if (actualOwnerEntity != Entity.Null && entityManager.Exists(actualOwnerEntity))
                    {
                        OwnershipCacheService.UpdateHeartOwner(castleHeartEntity, actualOwnerEntity, entityManager);
                    }
                    else
                    {
                        OwnershipCacheService.UpdateHeartOwner(castleHeartEntity, Entity.Null, entityManager);
                    }
                }
            }
            else
            {
                
            }
        }
    }
}