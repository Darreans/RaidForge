using BepInEx.Logging;
using Unity.Entities;
using ProjectM;
using Bloodstone.API;
using System;

namespace RaidForge
{
    public static class SiegeWeaponManager
    {
        /// <summary>
        /// Set the SiegeWeaponHealth in the first ServerGameBalanceSettings entity (no reflection).
        /// </summary>
        public static bool SetSiegeWeaponHealth(SiegeWeaponHealth newValue, ManualLogSource logger)
        {
            var world = VWorld.Server;
            if (world == null)
            {
                logger.LogError("[SiegeWeaponManager] No VWorld.Server; cannot set SiegeWeaponHealth.");
                return false;
            }

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadWrite<ServerGameBalanceSettings>());
            var eArray = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            try
            {
                if (eArray.Length == 0)
                {
                    logger.LogWarning("[SiegeWeaponManager] No entity found with ServerGameBalanceSettings!");
                    return false;
                }

                var entity = eArray[0];
                var sgb = em.GetComponentData<ServerGameBalanceSettings>(entity);

                sgb.SiegeWeaponHealth = newValue;
                em.SetComponentData(entity, sgb);

                logger.LogInfo($"[SiegeWeaponManager] Updated SGB.SiegeWeaponHealth => {newValue}");
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Error updating SGB.SiegeWeaponHealth: {ex}");
                return false;
            }
            finally
            {
                eArray.Dispose();
            }
        }

        /// <summary>
        /// Read the current SiegeWeaponHealth from the first ServerGameBalanceSettings entity.
        /// Returns null if not found.
        /// </summary>
        public static SiegeWeaponHealth? GetSiegeWeaponHealth(ManualLogSource logger)
        {
            var world = VWorld.Server;
            if (world == null)
            {
                logger.LogError("[SiegeWeaponManager] No VWorld.Server; cannot read SiegeWeaponHealth.");
                return null;
            }

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadOnly<ServerGameBalanceSettings>());
            var eArray = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            try
            {
                if (eArray.Length == 0)
                {
                    logger.LogWarning("[SiegeWeaponManager] No entity found with ServerGameBalanceSettings!");
                    return null;
                }

                var entity = eArray[0];
                var sgb = em.GetComponentData<ServerGameBalanceSettings>(entity);
                return sgb.SiegeWeaponHealth;
            }
            catch (Exception ex)
            {
                logger.LogError($"[SiegeWeaponManager] Error reading SGB.SiegeWeaponHealth: {ex}");
                return null;
            }
            finally
            {
                eArray.Dispose();
            }
        }
    }
}
