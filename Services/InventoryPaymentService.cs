using ProjectM;
using Stunlock.Core;
using Unity.Entities;

namespace RaidForge.Services
{
    public static class InventoryPaymentService
    {
        public static int GetItemAmount(EntityManager em, Entity characterEntity, PrefabGUID itemPrefab)
        {
            if (characterEntity == Entity.Null || !em.Exists(characterEntity))
            {
                return 0;
            }

            return InventoryUtilities.GetItemAmountInInventories(em, characterEntity, itemPrefab);
        }

        public static bool TryRemoveItems(EntityManager em, Entity characterEntity, PrefabGUID itemPrefab, int amount)
        {
            if (amount <= 0 || characterEntity == Entity.Null || !em.Exists(characterEntity))
            {
                return false;
            }

            return InventoryUtilitiesServer.TryRemoveItem(em, characterEntity, itemPrefab, amount);
        }
    }
}
