using System;
using ProjectM;
using Unity.Entities;

namespace RaidForge 
{
    internal static class VWorldUtils
    {
        private static World? _serverWorld;

        private static EntityManager? _entityManager;
        
        public static World Server
        {
            get
            {
                if (_serverWorld != null && _serverWorld.IsCreated)
                {
                    return _serverWorld;
                }

                _serverWorld = GetWorld("Server");

                if (_serverWorld == null || !_serverWorld.IsCreated)
                {
                    _entityManager = null;
                    throw new System.Exception("Server world is not available or not created.");
                }

                _entityManager = _serverWorld.EntityManager;
                return _serverWorld;
            }
        }

        public static EntityManager EntityManager
        {
            get
            {
                if (_entityManager != null && _serverWorld != null && _serverWorld.IsCreated)
                {
                    return _entityManager.Value; 
                }

             
                return Server.EntityManager;
            }
        }
        
        private static World? GetWorld(string name)
        {
            foreach (var world in World.s_AllWorlds)
            {
                if (world.Name == name)
                {
                    return world;
                }
            }
            return null;
        }

        public static bool IsServerWorldReady()
        {
            try
            {
                return Server != null && Server.IsCreated;
            }
            catch
            {
                return false;
            }
        }

        public static bool ZDateTime(out TimeZonedDateTime dt)
        {
            dt = default;
            return true;
        }

        public static bool GameBalanceSettings(
            out ServerGameBalanceSettings settings,
            Func<ServerGameBalanceSettings, ServerGameBalanceSettings> modify = null
        )
        {
            settings = default!;
            try
            {
                if (_entityManager != null)
                {
                    var em    = _entityManager.Value;
                    var query = em.CreateEntityQuery(ComponentType.ReadWrite<ServerGameBalanceSettings>());

                    if (query.IsEmptyIgnoreFilter)
                    {
                        RaidForgePlugin.Logger.LogError("Could not find ServerGameBalanceSettings entity.");
                        return false;
                    }

                    var entity   = query.GetSingletonEntity();
                    var original = entity.Read<ServerGameBalanceSettings>();

                    if (modify is null)
                    {
                        settings = original;
                        return true;
                    }

                    var updated = modify(original);
                    
                    entity.Write(updated);
                }

                return true;
            }
            catch (Exception ex)
            {
                RaidForgePlugin.Logger.LogError($"Exception while accessing ServerGameBalanceSettings: {ex}");
                return false;
            }
        }
    }
}