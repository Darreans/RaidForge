using BepInEx.Configuration;

namespace RaidForge.Config
{
    public static class PurchasedOrpConfig
    {
        private const string SECTION_MAIN = "Purchased ORP";

        public static ConfigEntry<bool> EnablePurchasedOrp { get; private set; }
        public static ConfigEntry<string> CurrencyPrefab { get; private set; }
        public static ConfigEntry<string> CurrencyDisplayName { get; private set; }
        public static ConfigEntry<int> CurrencyAmountPerRaidDay { get; private set; }
        public static ConfigEntry<int> MaxPurchaseRaidDays { get; private set; }

        public static void Initialize(ConfigFile configFile)
        {
            EnablePurchasedOrp = configFile.Bind(
                SECTION_MAIN,
                "EnablePurchasedOrp",
                false,
                "If true, players can use .buyorp <days> to buy raid-day protection. This mode only works when normal Offline Raid Protection and Opt-In Raiding are both disabled.");

            CurrencyPrefab = configFile.Bind(
                SECTION_MAIN,
                "CurrencyPrefab",
                "Item_Ingredient_Coin_Silver",
                "Prefab name or GUID hash for the item spent by .buyorp. Default: Item_Ingredient_Coin_Silver.");

            CurrencyDisplayName = configFile.Bind(
                SECTION_MAIN,
                "CurrencyDisplayName",
                "Silver Coins",
                "Player-facing item name shown in .buyorp messages. This does not affect which prefab is removed from inventory.");

            CurrencyAmountPerRaidDay = configFile.Bind(
                SECTION_MAIN,
                "CurrencyAmountPerRaidDay",
                100,
                "How many of CurrencyPrefab are required per purchased raid day.");

            MaxPurchaseRaidDays = configFile.Bind(
                SECTION_MAIN,
                "MaxPurchaseRaidDays",
                30,
                "Maximum number of raid days a player can buy in one .buyorp command.");
        }

        public static bool IsPurchasedOrpActive()
        {
            return EnablePurchasedOrp?.Value == true &&
                OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value != true &&
                OptInRaidingConfig.EnableOptInRaiding?.Value != true;
        }
    }
}
