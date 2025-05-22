using ProjectM;
using ProjectM.Network;
using Unity.Entities;
using BepInEx.Logging;
using RaidForge.Config;
using RaidForge.Utils;

namespace RaidForge.Services
{
    public static class OfflineProtectionService
    {
        private static bool AreAllDefendersActuallyOffline(Entity castleHeartEntity, EntityManager entityManager, ManualLogSource logger)
        {
            if (!entityManager.Exists(castleHeartEntity) || !entityManager.HasComponent<UserOwner>(castleHeartEntity))
            {
                LoggingHelper.Debug($"[OfflineProtectionServiceLogic] CH {castleHeartEntity} invalid or no UserOwner.");
                return false;
            }

            UserOwner heartOwnerComponent = entityManager.GetComponentData<UserOwner>(castleHeartEntity);
            Entity ownerUserEntity = heartOwnerComponent.Owner._Entity;

            if (!entityManager.Exists(ownerUserEntity) || !entityManager.HasComponent<User>(ownerUserEntity))
            {
                LoggingHelper.Debug($"[OfflineProtectionServiceLogic] OwnerUser {ownerUserEntity} invalid or no User comp.");
                return false;
            }

            User ownerUserData = entityManager.GetComponentData<User>(ownerUserEntity);
            Entity clanEntity = ownerUserData.ClanEntity._Entity;

            if (!entityManager.Exists(clanEntity) || !entityManager.HasComponent<ClanTeam>(clanEntity))
            {
                bool isSoloOwnerOffline = !ownerUserData.IsConnected;
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Info($"[OfflineProtectionServiceLogic] Solo defender {ownerUserData.CharacterName} IsConnected: {ownerUserData.IsConnected}. AllOffline Status: {isSoloOwnerOffline}.");
                }
                return isSoloOwnerOffline;
            }
            else
            {
                bool atLeastOneMemberOnline = UserHelper.IsAnyClanMemberOnline(entityManager, clanEntity);

                bool allClanMembersOffline = !atLeastOneMemberOnline;
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Info($"[OfflineProtectionServiceLogic] Clan {clanEntity} (CH: {castleHeartEntity}) AllOffline Status: {allClanMembersOffline} (atLeastOneOnline: {atLeastOneMemberOnline}).");
                }
                return allClanMembersOffline;
            }
        }

        public static bool ShouldProtectBase(Entity castleHeartEntity, EntityManager entityManager, ManualLogSource logger)
        {
            if (!OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value)
            {
                LoggingHelper.Debug("[OfflineProtectionService] Offline raid protection is disabled via config. Base will not be protected by this service.");
                return false; 
            }

            if (OfflineGraceService.IsBaseVulnerableDueToGracePeriodOrBreach(entityManager, castleHeartEntity))
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Info($"[OfflineProtectionService] Base {castleHeartEntity} is VULNERABLE due to grace period or active breach. Protection bypassed.");
                }
                return false;
            }

            if (AreAllDefendersActuallyOffline(castleHeartEntity, entityManager, logger))
            {
                if (TroubleshootingConfig.EnableVerboseLogging.Value)
                {
                    LoggingHelper.Info($"[OfflineProtectionService] Base {castleHeartEntity}: All defenders offline AND not in grace/breach. Base WILL be protected.");
                }
                return true;
            }

            if (TroubleshootingConfig.EnableVerboseLogging.Value)
            {
                LoggingHelper.Info($"[OfflineProtectionService] Base {castleHeartEntity}: At least one defender online AND not in grace/breach. Base will NOT be protected.");
            }
            return false;
        }
    }
}