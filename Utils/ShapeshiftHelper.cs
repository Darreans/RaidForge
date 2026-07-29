using ProjectM;
using RaidForge.Data;
using Unity.Entities;

namespace RaidForge.Utils
{
    public static class ShapeshiftHelper
    {
        public static bool IsInBearForm(EntityManager em, Entity characterEntity)
        {
            return BuffUtility.HasBuff(em, characterEntity, PrefabData.BearBuff.Guid) ||
                   BuffUtility.HasBuff(em, characterEntity, PrefabData.BearSkinBuff.Guid);
        }
    }
}
