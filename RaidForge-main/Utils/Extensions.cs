using Il2CppSystem.Text; 
using ProjectM;
using Unity.Entities;

namespace RaidForge.Utils
{
    internal static class EntityExtensions
    {
        private static EntityManager Em => VWorld.EntityManager;

        public static bool Exists(this Entity entity)
        {
            return entity != Entity.Null && Em.Exists(entity);
        }
        public static bool Has<T>(this Entity entity) where T : struct
        {
            if (entity == Entity.Null) return false;
            return Em.HasComponent<T>(entity);
        }

        public static T Read<T>(this Entity entity) where T : struct
        {
            return Em.GetComponentData<T>(entity);
        }

        public static bool TryRead<T>(this Entity entity, out T component) where T : struct
        {
            if (entity.Exists() && entity.Has<T>())
            {
                component = Em.GetComponentData<T>(entity);
                return true;
            }
            component = default;
            return false;
        }

        public static void Write<T>(this Entity entity, T data) where T : struct
        {
            Em.SetComponentData(entity, data);
        }

        public static void Add<T>(this Entity entity) where T : struct
        {
            if (entity.Exists() && !entity.Has<T>())
            {
                Em.AddComponent<T>(entity);
            }
        }

        public delegate void WithRefHandler<T>(ref T item) where T : struct;

        public static void With<T>(this Entity entity, WithRefHandler<T> action) where T : struct
        {
            if (!entity.Exists() || !entity.Has<T>()) return;
            var current = entity.Read<T>();
            action(ref current);
            entity.Write(current);
        }

        public static string Explore(this Entity entity)
        {
            if (!entity.Exists()) return "Entity does not exist.";

            var sb = new StringBuilder();
            try
            {
                if (VWorld.IsServerWorldReady())
                {
                    ProjectM.EntityDebuggingUtility.DumpEntity(VWorld.Server, entity, true, sb);
                    return sb.ToString();
                }
                else
                {
                    return $"Cannot explore entity {entity}: Server world not ready.";
                }
            }
            catch (System.Exception ex)
            {
                return $"Error exploring entity {entity}: {ex.Message}";
            }
        }
    }
}