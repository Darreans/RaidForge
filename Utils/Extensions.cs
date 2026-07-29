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
    }
}
