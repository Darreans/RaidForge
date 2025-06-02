using Unity.Entities;
using Unity.Collections;
using ProjectM;         
using ProjectM.Network;

namespace RaidForge.Utils
{
    public static class ClanUtilities
    {
        public static bool TryGetClanEntityByNetworkId(EntityManager em, NetworkId clanNetworkId, out Entity foundClanEntity)
        {
            foundClanEntity = Entity.Null;
            if (clanNetworkId.Equals(default(NetworkId)))
            {
                LoggingHelper.Debug("[ClanUtilities] TryGetClanEntityByNetworkId: provided clanNetworkId is default. Cannot find clan.");
                return false;
            }

            EntityQuery clanQuery = default;
            NativeArray<Entity> clanEntities = default;
            try
            {
                clanQuery = em.CreateEntityQuery(ComponentType.ReadOnly<ClanTeam>(), ComponentType.ReadOnly<NetworkId>());
                clanEntities = clanQuery.ToEntityArray(Allocator.TempJob);

                foreach (Entity clanEntity in clanEntities)
                {
                    if (em.GetComponentData<NetworkId>(clanEntity).Equals(clanNetworkId)) 
                    {
                        foundClanEntity = clanEntity;
                        return true;
                    }
                }
            }
            catch (System.Exception ex)
            {
                LoggingHelper.Error($"[ClanUtilities] Exception in TryGetClanEntityByNetworkId for NetID {clanNetworkId}: {ex}");
            }
            finally
            {
                if (clanEntities.IsCreated) clanEntities.Dispose();
                if (clanQuery != default) clanQuery.Dispose();
            }

            LoggingHelper.Debug($"[ClanUtilities] ClanEntity with NetworkId {clanNetworkId} not found.");
            return false;
        }
    }
}