using System;
using System.Collections.Generic;
using ProjectM.Network;
using Unity.Collections;
using Unity.Entities;

namespace RaidForge.Utils
{
    public static class OwnerIdentityQueries
    {
        public static List<OwnerIdentity> GetUniqueOwners(EntityManager em, string logContext)
        {
            var ownersByPersistentKey = new Dictionary<string, OwnerIdentity>();
            EntityQuery userQuery = default;
            NativeArray<Entity> userEntities = default;

            try
            {
                userQuery = em.CreateEntityQuery(ComponentType.ReadOnly<User>());
                userEntities = userQuery.ToEntityArray(Allocator.Temp);

                foreach (Entity userEntity in userEntities)
                {
                    if (!em.Exists(userEntity))
                    {
                        continue;
                    }

                    if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                        em,
                        userEntity,
                        out OwnerIdentity owner,
                        out _))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(owner.PersistentKey) ||
                        ownersByPersistentKey.ContainsKey(owner.PersistentKey))
                    {
                        continue;
                    }

                    ownersByPersistentKey[owner.PersistentKey] = owner;
                }
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"[{logContext}] Error building unique owner list.", ex);
            }
            finally
            {
                if (userEntities.IsCreated)
                {
                    userEntities.Dispose();
                }

                if (userQuery != default)
                {
                    userQuery.Dispose();
                }
            }

            return new List<OwnerIdentity>(ownersByPersistentKey.Values);
        }
    }
}
