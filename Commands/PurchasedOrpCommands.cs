using System;
using System.Globalization;
using ProjectM;
using RaidForge.Config;
using RaidForge.Services;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;

namespace RaidForge.Commands
{
    public class PurchasedOrpCommands
    {
        [Command("buyorp", description: "Buys raid-day ORP protection for you or your clan. Usage: .buyorp <days>", adminOnly: false)]
        public void BuyOrp(ChatCommandContext ctx, int days)
        {
            try
            {
                if (TryRejectPurchasedOrpUnavailable(ctx))
                {
                    return;
                }

                int maxDays = Math.Max(1, PurchasedOrpConfig.MaxPurchaseRaidDays?.Value ?? 30);
                if (days <= 0 || days > maxDays)
                {
                    ctx.Reply(ChatColors.ErrorText(
                        $"Usage: {CommandSettingsConfig.GetInvocation("buyorp")} <days> where days is between 1 and {maxDays}."));
                    return;
                }

                int costPerDay = PurchasedOrpConfig.CurrencyAmountPerRaidDay?.Value ?? 0;
                if (costPerDay <= 0)
                {
                    ctx.Reply(ChatColors.ErrorText("Purchased ORP cost is not configured correctly. CurrencyAmountPerRaidDay must be greater than 0."));
                    return;
                }

                var em = VWorld.EntityManager;

                if (!OwnerIdentityHelper.TryResolveFromCommandSender(
                    em,
                    ctx.Event.SenderUserEntity,
                    out OwnerIdentity owner,
                    out string ownerError))
                {
                    ctx.Reply(ChatColors.ErrorText(ownerError));
                    return;
                }

                if (!TryResolveCurrency(out PrefabGUID currencyPrefab, out string currencyDisplayName, out string currencyError))
                {
                    ctx.Reply(ChatColors.ErrorText(currencyError));
                    return;
                }

                int requiredAmount;
                try
                {
                    requiredAmount = checked(days * costPerDay);
                }
                catch (OverflowException)
                {
                    ctx.Reply(ChatColors.ErrorText("The configured ORP purchase cost is too large."));
                    return;
                }

                Entity characterEntity = ctx.Event.SenderCharacterEntity;
                int availableAmount = InventoryPaymentService.GetItemAmount(em, characterEntity, currencyPrefab);

                if (availableAmount < requiredAmount)
                {
                    int missingAmount = requiredAmount - availableAmount;
                    ctx.Reply(ChatColors.WarningText($"You are missing {missingAmount.ToString(CultureInfo.InvariantCulture)} {currencyDisplayName}."));
                    ctx.Reply(ChatColors.InfoText($"Required: {requiredAmount.ToString(CultureInfo.InvariantCulture)} for {days} raid day(s). You have: {availableAmount.ToString(CultureInfo.InvariantCulture)}."));
                    return;
                }

                if (!InventoryPaymentService.TryRemoveItems(em, characterEntity, currencyPrefab, requiredAmount))
                {
                    ctx.Reply(ChatColors.ErrorText("Could not remove the required currency from your inventory. Please try again."));
                    return;
                }

                PurchasedOrpService.AddRaidDays(owner.PersistentKey, owner.ContextualName, days);
                RaidMapIconService.ProcessCleanup();

                int totalProtectedDays = PurchasedOrpService.GetRemainingRaidDays(owner.PersistentKey);

                ctx.Reply(ChatColors.SuccessText($"Purchased ORP for {days} raid day(s) for {owner.GetDisplayNameWithOwnerType()}."));
                ctx.Reply(ChatColors.InfoText($"You are protected for {totalProtectedDays.ToString(CultureInfo.InvariantCulture)} raid day(s)."));
                ctx.Reply(ChatColors.InfoText($"Spent {requiredAmount.ToString(CultureInfo.InvariantCulture)} {currencyDisplayName}."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error buying ORP protection. Check server logs."));
                LoggingHelper.Error("Error executing .buyorp", ex);
            }
        }

        [Command("buyorpstatus", shortHand: "orpamount", description: "Shows your purchased ORP protection days.", adminOnly: false)]
        public void BuyOrpStatus(ChatCommandContext ctx)
        {
            ShowOrpAmount(ctx);
        }

        [Command("givebuyorp", description: "Admin: gives purchased ORP raid days to a player/clan. Usage: .givebuyorp <PlayerName> <amount>", adminOnly: true)]
        public void GiveBuyOrp(ChatCommandContext ctx, string playerName, int amount)
        {
            try
            {
                if (TryRejectPurchasedOrpUnavailable(ctx))
                {
                    return;
                }

                if (amount <= 0)
                {
                    ctx.Reply(ChatColors.ErrorText(
                        $"Usage: {CommandSettingsConfig.GetInvocation("givebuyorp")} <PlayerName> <amount> where amount is greater than 0."));
                    return;
                }

                var em = VWorld.EntityManager;

                if (!OwnerIdentityHelper.TryResolveFromPlayerName(
                    em,
                    playerName,
                    out OwnerIdentity owner,
                    out string ownerError))
                {
                    ctx.Reply(ChatColors.ErrorText(ownerError));
                    return;
                }

                try
                {
                    PurchasedOrpService.AddRaidDays(owner.PersistentKey, owner.ContextualName, amount);
                }
                catch (OverflowException)
                {
                    ctx.Reply(ChatColors.ErrorText("That ORP amount is too large to add."));
                    return;
                }

                RaidMapIconService.ProcessCleanup();

                int remainingRaidDays = PurchasedOrpService.GetRemainingRaidDays(owner.PersistentKey);
                ctx.Reply(ChatColors.SuccessText($"Gave {amount.ToString(CultureInfo.InvariantCulture)} purchased ORP raid day(s) to {owner.GetDisplayNameWithOwnerType()}."));
                ctx.Reply(ChatColors.InfoText($"{owner.GetDisplayNameWithOwnerType()} now has {remainingRaidDays.ToString(CultureInfo.InvariantCulture)} raid day(s) of protection."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error giving purchased ORP. Check server logs."));
                LoggingHelper.Error("Error executing .givebuyorp", ex);
            }
        }

        [Command("removebuyorp", description: "Admin: removes purchased ORP raid days from a player/clan. Usage: .removebuyorp <PlayerName> <amount>", adminOnly: true)]
        public void RemoveBuyOrp(ChatCommandContext ctx, string playerName, int amount)
        {
            try
            {
                if (TryRejectPurchasedOrpUnavailable(ctx))
                {
                    return;
                }

                if (amount <= 0)
                {
                    ctx.Reply(ChatColors.ErrorText("Usage: .removebuyorp <PlayerName> <amount> where amount is greater than 0."));
                    return;
                }

                var em = VWorld.EntityManager;

                if (!OwnerIdentityHelper.TryResolveFromPlayerName(
                    em,
                    playerName,
                    out OwnerIdentity owner,
                    out string ownerError))
                {
                    ctx.Reply(ChatColors.ErrorText(ownerError));
                    return;
                }

                int before = PurchasedOrpService.GetRemainingRaidDays(owner.PersistentKey);
                int removed = PurchasedOrpService.RemoveRaidDays(owner.PersistentKey, amount);
                RaidMapIconService.ProcessCleanup();

                int remainingRaidDays = PurchasedOrpService.GetRemainingRaidDays(owner.PersistentKey);

                if (before <= 0 || removed <= 0)
                {
                    ctx.Reply(ChatColors.WarningText($"{owner.GetDisplayNameWithOwnerType()} has no purchased ORP raid days to remove."));
                    return;
                }

                ctx.Reply(ChatColors.SuccessText($"Removed {removed.ToString(CultureInfo.InvariantCulture)} purchased ORP raid day(s) from {owner.GetDisplayNameWithOwnerType()}."));
                ctx.Reply(ChatColors.InfoText($"{owner.GetDisplayNameWithOwnerType()} now has {remainingRaidDays.ToString(CultureInfo.InvariantCulture)} raid day(s) of protection."));

                if (remainingRaidDays <= 0 && PurchasedOrpService.IsRaidDate(PurchasedOrpService.GetCurrentRaidDate()))
                {
                    ctx.Reply(ChatColors.WarningText("Their purchased ORP protection for today is now removed."));
                }
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error removing purchased ORP. Check server logs."));
                LoggingHelper.Error("Error executing .removebuyorp", ex);
            }
        }

        private static void ShowOrpAmount(ChatCommandContext ctx)
        {
            try
            {
                if (TryRejectPurchasedOrpUnavailable(ctx))
                {
                    return;
                }

                var em = VWorld.EntityManager;

                if (!OwnerIdentityHelper.TryResolveFromCommandSender(
                    em,
                    ctx.Event.SenderUserEntity,
                    out OwnerIdentity owner,
                    out string ownerError))
                {
                    ctx.Reply(ChatColors.ErrorText(ownerError));
                    return;
                }

                int remainingRaidDays = PurchasedOrpService.GetRemainingRaidDays(owner.PersistentKey);
                if (remainingRaidDays <= 0)
                {
                    ctx.Reply(ChatColors.WarningText("You do not have any purchased ORP raid days."));
                    return;
                }

                ctx.Reply(ChatColors.InfoText($"You are protected for {remainingRaidDays.ToString(CultureInfo.InvariantCulture)} raid day(s)."));
            }
            catch (Exception ex)
            {
                ctx.Reply(ChatColors.ErrorText("Error reading purchased ORP status. Check server logs."));
                LoggingHelper.Error("Error executing purchased ORP amount command", ex);
            }
        }

        private static bool TryRejectPurchasedOrpUnavailable(ChatCommandContext ctx)
        {
            if (PurchasedOrpConfig.EnablePurchasedOrp?.Value != true)
            {
                ctx.Reply(ChatColors.ErrorText("Purchased ORP is not enabled on this server."));
                return true;
            }

            if (OfflineRaidProtectionConfig.EnableOfflineRaidProtection?.Value == true)
            {
                ctx.Reply(ChatColors.ErrorText("Purchased ORP is disabled because normal Offline Raid Protection is enabled. Use one ORP mode at a time."));
                return true;
            }

            if (OptInRaidingConfig.EnableOptInRaiding?.Value == true)
            {
                ctx.Reply(ChatColors.ErrorText("Purchased ORP is disabled because Opt-In Raiding is enabled. Use one raid-protection mode at a time."));
                return true;
            }

            return false;
        }

        private static bool TryResolveCurrency(out PrefabGUID currencyPrefab, out string currencyDisplayName, out string error)
        {
            currencyPrefab = default;
            currencyDisplayName = null;
            error = string.Empty;

            string configuredCurrency = PurchasedOrpConfig.CurrencyPrefab?.Value;
            if (!PrefabGuidResolver.TryResolve(configuredCurrency, out currencyPrefab, out string resolvedCurrencyName))
            {
                error = $"Could not resolve Purchased ORP CurrencyPrefab '{configuredCurrency}'. Use a valid prefab name or GUID hash.";
                return false;
            }

            currencyDisplayName = PurchasedOrpConfig.CurrencyDisplayName?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(currencyDisplayName))
            {
                currencyDisplayName = resolvedCurrencyName;
            }

            if (string.IsNullOrWhiteSpace(currencyDisplayName) &&
                !PrefabGuidResolver.TryGetPrefabName(currencyPrefab, out currencyDisplayName))
            {
                currencyDisplayName = currencyPrefab.GuidHash.ToString(CultureInfo.InvariantCulture);
            }

            return true;
        }
    }
}
