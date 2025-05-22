using System;
using ProjectM;
using Unity.Entities;

namespace RaidForge.Utils
{
    internal static class VWorld
    {
        private static World _serverWorld;
        private static EntityManager _entityManager; 

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
                    _entityManager = default; 
                    LoggingHelper.Error("VWorld: Server world is not available or not created. Subsequent calls may fail.");
                    return null;
                }

                _entityManager = _serverWorld.EntityManager;
                return _serverWorld;
            }
        }

        public static EntityManager EntityManager
        {
            get
            {
                if ((_entityManager == default || (_serverWorld != null && _serverWorld.IsCreated && _entityManager != _serverWorld.EntityManager))
                    && Server != null) 
                {
                }

                if (_entityManager == default)
                {
                    LoggingHelper.Error("VWorld: EntityManager is not available. Ensure Server world is initialized.");
                }
                return _entityManager;
            }
        }

        private static World GetWorld(string name)
        {
            if (World.s_AllWorlds == null) return null;
            foreach (var world in World.s_AllWorlds)
            {
                if (world.Name == name && world.IsCreated) 
                {
                    return world;
                }
            }
            return null;
        }

        public static bool IsServerWorldReady()
        {
            return Server != null && Server.IsCreated && EntityManager != default;
        }

       
        public static bool ZDateTime(out TimeZonedDateTime dt)
        {
            dt = default;
            
            return true;
        }

        public static bool GameBalanceSettings(
            out ServerGameBalanceSettings settings,
            Func<ServerGameBalanceSettings, ServerGameBalanceSettings> modify = null)
        {
            settings = default;
            try
            {
                if (!IsServerWorldReady())
                {
                    LoggingHelper.Error("VWorld.GameBalanceSettings: Server world or EntityManager not ready.");
                    return false;
                }

                var em = EntityManager;
                var query = em.CreateEntityQuery(ComponentType.ReadWrite<ServerGameBalanceSettings>());

                if (query.IsEmptyIgnoreFilter)
                {
                    LoggingHelper.Error("VWorld.GameBalanceSettings: Could not find ServerGameBalanceSettings entity.");
                    return false;
                }

                var entity = query.GetSingletonEntity();
                var original = em.GetComponentData<ServerGameBalanceSettings>(entity);

                if (modify == null)
                {
                    settings = original;
                    return true;
                }

                var updated = modify(original);
                em.SetComponentData(entity, updated);
                settings = updated; 
                return true;
            }
            catch (Exception ex)
            {
                LoggingHelper.Error($"VWorld.GameBalanceSettings: Exception while accessing/modifying ServerGameBalanceSettings.", ex);
                return false;
            }
        }
    }
}