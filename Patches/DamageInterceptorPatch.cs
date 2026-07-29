using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using HookDOTS.API.Attributes;
using ProjectM;
using ProjectM.CastleBuilding;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using RaidForge.Config;
using RaidForge.Data;
using RaidForge.Services;
using RaidForge.Systems;
using RaidForge.Utils;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using ProjectM.Scripting;

namespace RaidForge.Patches
{
    public static class DamageInterceptorPatch
    {
        private static readonly TimeSpan OfflineProtectedMessageCooldown = TimeSpan.FromSeconds(10);
        private static Dictionary<Entity, DateTime> _lastProtectedMessageTimes = new();

        private static readonly TimeSpan OfflineSiegeAnnouncementCooldown = TimeSpan.FromSeconds(30);
        private static Dictionary<Entity, DateTime> _lastOfflineSiegeAnnouncementTimes = new();

        private static readonly TimeSpan DecayedRaidAnnouncementCooldown = TimeSpan.FromMinutes(5);
        private static Dictionary<Entity, DateTime> _lastDecayedRaidAnnouncementTimes = new();
        private static DateTime _nextCooldownPruneUtc = DateTime.MinValue;

        private static EntityQuery _damageQuery;
        private static EntityManager _cachedEntityManager;
        private static bool _queryInitialized = false;

        private static ServerScriptMapper _serverScriptMapper;
        private static ServerGameManager _serverGameManager;
        private static bool _sgmCached = false;
        private const int MaxTntSourceDepth = 4;
        private const int MaxCachedTntSources = 512;
        private static readonly Dictionary<Entity, TntTier> _tntTierBySource = new();

        public static void ResetRuntimeState()
        {
            _queryInitialized = false;
            _damageQuery = default;
            _cachedEntityManager = default;

            _sgmCached = false;
            _serverScriptMapper = null;
            _serverGameManager = default;

            _lastProtectedMessageTimes.Clear();
            _lastOfflineSiegeAnnouncementTimes.Clear();
            _lastDecayedRaidAnnouncementTimes.Clear();
            _tntTierBySource.Clear();
            _nextCooldownPruneUtc = DateTime.MinValue;
            TntDamageDiagnosticService.ResetRuntimeState();
        }

        private sealed class DamageContext
        {
            public readonly EntityManager Em;
            public readonly Entity DamageEventEntity;
            public readonly Entity CastleHeartEntity;
            public readonly Entity SourceEntity;
            public readonly string PersistentKey;
            public readonly DateTime NowUtc;

            public readonly bool LogDebug;
            public readonly bool OptInEnabled;
            public readonly bool PurchasedOrpEnabled;
            public readonly bool OrpEnabled;
            public readonly bool AnnounceOfflineRaid;
            public readonly bool ShowOfflineRaidIcon;
            public readonly bool AnnounceDecayed;
            public readonly bool ForcedRaidDay;
            public readonly float ConfiguredGraceMinutes;
            public readonly bool WeaponRaidingEnabled;
            public readonly bool TntRaidingEnabled;
            public readonly float WeaponDamageMultiplier;
            public readonly TntTier TntTier;
            public readonly EntityTypeModifiers NativeTntModifiers;
            public readonly float TntNormalDamagePercent;
            public readonly float TntCastleWallDamagePercent;
            public readonly bool UseNormalTntDamageAfterBreach;

            public bool IsTntDamage => TntTier != RaidForge.Data.TntTier.None;

            public DealDamageEvent DamageEvent;
            public string CachedDefenderBaseName;
            public bool IsFastTrackApproved;
            public bool AttackerResolved;
            public Entity AttackerCharEntity = Entity.Null;
            public Entity AttackerUserEntity = Entity.Null;
            public string AttackerName;
            public string AttackerKey;
            public bool AllDefendersOffline;
            public bool CheckedAllDefendersOffline;
            public bool IsInGracePeriod;
            public bool WillBeFullyProtected;
            public bool IsBreached;
            public bool IsDecaying;

            public DamageContext(
                EntityManager em,
                Entity damageEventEntity,
                Entity castleHeartEntity,
                Entity sourceEntity,
                string persistentKey,
                DealDamageEvent damageEvent,
                DateTime nowUtc,
                bool logDebug,
                bool optInEnabled,
                bool purchasedOrpEnabled,
                bool orpEnabled,
                bool announceOfflineRaid,
                bool showOfflineRaidIcon,
                bool announceDecayed,
                bool forcedRaidDay,
                float configuredGraceMinutes,
                bool weaponRaidingEnabled,
                bool tntRaidingEnabled,
                float weaponDamageMultiplier,
                TntTier tntTier,
                EntityTypeModifiers nativeTntModifiers,
                float tntNormalDamagePercent,
                float tntCastleWallDamagePercent,
                bool useNormalTntDamageAfterBreach)
            {
                Em = em;
                DamageEventEntity = damageEventEntity;
                CastleHeartEntity = castleHeartEntity;
                SourceEntity = sourceEntity;
                PersistentKey = persistentKey;
                DamageEvent = damageEvent;
                NowUtc = nowUtc;
                LogDebug = logDebug;
                OptInEnabled = optInEnabled;
                PurchasedOrpEnabled = purchasedOrpEnabled;
                OrpEnabled = orpEnabled;
                AnnounceOfflineRaid = announceOfflineRaid;
                ShowOfflineRaidIcon = showOfflineRaidIcon;
                AnnounceDecayed = announceDecayed;
                ForcedRaidDay = forcedRaidDay;
                ConfiguredGraceMinutes = configuredGraceMinutes;
                WeaponRaidingEnabled = weaponRaidingEnabled;
                TntRaidingEnabled = tntRaidingEnabled;
                WeaponDamageMultiplier = weaponDamageMultiplier;
                TntTier = tntTier;
                NativeTntModifiers = nativeTntModifiers;
                TntNormalDamagePercent = tntNormalDamagePercent;
                TntCastleWallDamagePercent = tntCastleWallDamagePercent;
                UseNormalTntDamageAfterBreach = useNormalTntDamageAfterBreach;
            }
        }

        [EcsSystemUpdatePrefix(typeof(DealDamageSystem))]
        public static unsafe void Prefix(SystemState* systemState)
        {
            var em = systemState->EntityManager;

            if (!_queryInitialized || _cachedEntityManager != em)
            {
                _damageQuery = em.CreateEntityQuery(ComponentType.ReadWrite<DealDamageEvent>());
                _cachedEntityManager = em;
                _queryInitialized = true;
            }

            if (_damageQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            bool logDebug = TroubleshootingConfig.EnableVerboseLogging.Value;
            bool tntDamageLoggingEnabled = TroubleshootingConfig.EnableTntDamageLogging?.Value ?? false;
            bool orpSystemEnabled = OfflineRaidProtectionConfig.EnableOfflineRaidProtection.Value;
            bool orpEnabled = OfflineRaidProtectionConfig.IsOfflineProtectionAllowedToday();
            bool optInEnabled = OptInRaidingConfig.EnableOptInRaiding.Value && !orpSystemEnabled;
            bool purchasedOrpEnabled = PurchasedOrpConfig.IsPurchasedOrpActive();
            bool announceOfflineRaid = OfflineRaidProtectionConfig.AnnounceOfflineRaid.Value;
            bool offlineRaidMapIconEnabled = MapIconsConfig.EnableOfflineRaidMapIcon.Value;

            bool announceDecayed = OfflineRaidProtectionConfig.AnnounceDecayedBaseRaid.Value;
            bool forcedRaidDay = OptInScheduleConfig.EnableOptInSchedule.Value && !OptInScheduleConfig.IsOptInSystemAllowedToday();
            float configuredGraceMinutes = OfflineRaidProtectionConfig.GracePeriodDurationMinutes.Value;
            bool weaponRaidingEnabled = WeaponRaidingConfig.EnableWeaponRaiding.Value;
            bool tntRaidingEnabled = TntRaidingConfig.EnableTntRaiding?.Value ?? false;
            float t01NormalDamagePercent = ClampTntPercent(
                TntRaidingConfig.T01NormalDamagePercent?.Value ?? 100f);
            float t02NormalDamagePercent = ClampTntPercent(
                TntRaidingConfig.T02NormalDamagePercent?.Value ?? 100f);
            float t01CastleWallDamagePercent = ClampTntPercent(
                TntRaidingConfig.T01CastleWallDamagePercent?.Value ?? 100f);
            float t02CastleWallDamagePercent = ClampTntPercent(
                TntRaidingConfig.T02CastleWallDamagePercent?.Value ?? 100f);
            bool useNormalTntDamageAfterBreach =
                TntRaidingConfig.UseNormalTntDamageAfterBreach?.Value ?? true;
            bool normalTntScalingEnabled =
                Math.Abs(t01NormalDamagePercent - 100f) > 0.001f ||
                Math.Abs(t02NormalDamagePercent - 100f) > 0.001f;
            float weaponDamageMultiplier = WeaponRaidingConfig.WeaponDamageVsStoneMultiplier.Value;
            bool decayMapIconsEnabled = MapIconsConfig.EnableDecayRaidMapIcon.Value;

            if (!optInEnabled &&
                !purchasedOrpEnabled &&
                !orpEnabled &&
                !weaponRaidingEnabled &&
                !tntRaidingEnabled &&
                !announceOfflineRaid &&
                !offlineRaidMapIconEnabled &&
                !announceDecayed &&
                !decayMapIconsEnabled &&
                !tntDamageLoggingEnabled &&
                !normalTntScalingEnabled)
            {
                return;
            }

            var damageEventEntities = _damageQuery.ToEntityArray(Allocator.Temp);

            DateTime nowUtc = DateTime.UtcNow;
            PruneInvalidCooldownEntries(em, nowUtc);

            if (!_sgmCached && VWorld.Server != null && VWorld.Server.IsCreated)
            {
                try
                {
                    _serverScriptMapper = VWorld.Server.GetExistingSystemManaged<ServerScriptMapper>();

                    if (_serverScriptMapper != null)
                    {
                        _serverGameManager = _serverScriptMapper.GetServerGameManager();
                        _sgmCached = true;
                    }
                }
                catch (Exception ex)
                {
                    LoggingHelper.Debug($"[DamageInterceptorPatch] ServerGameManager not ready: {ex.Message}");
                }
            }

            try
            {
                foreach (var entity in damageEventEntities)
                {
                    try
                    {
                        if (!em.Exists(entity))
                        {
                            continue;
                        }

                        var damageEvent = em.GetComponentData<DealDamageEvent>(entity);

                        if (damageEvent.MainFactor <= 0 && damageEvent.RawDamage <= 0)
                        {
                            continue;
                        }

                        Entity targetEntity = damageEvent.Target;
                        Entity sourceEntity = damageEvent.SpellSource;
                        EntityTypeModifiers nativeTntModifiers = damageEvent.MaterialModifiers;
                        TntTier tntTier = TntTier.None;
                        float tntNormalDamagePercent = 100f;

                        /*
                            Ordinary TNT scaling is intentionally applied before the
                            castle filter so it also affects creatures, players,
                            resources, and non-castle structures. The source walk is
                            only paid globally when at least one tier differs from 100%.
                        */
                        if (normalTntScalingEnabled &&
                            nativeTntModifiers.Explosives > 0f)
                        {
                            tntTier = TryGetTntTier(em, sourceEntity);

                            if (tntTier != TntTier.None)
                            {
                                tntNormalDamagePercent = tntTier == TntTier.T01
                                    ? t01NormalDamagePercent
                                    : t02NormalDamagePercent;

                                if (Math.Abs(tntNormalDamagePercent - 100f) > 0.001f)
                                {
                                    damageEvent.MaterialModifiers = ScaleMaterialModifiers(
                                        nativeTntModifiers,
                                        tntNormalDamagePercent / 100f);
                                    em.SetComponentData(entity, damageEvent);
                                }
                            }
                        }

                        bool isCastleConnected = em.HasComponent<CastleHeartConnection>(targetEntity);
                        bool isCastleHeart = em.HasComponent<CastleHeart>(targetEntity);

                        if (!isCastleConnected && !isCastleHeart)
                        {
                            continue;
                        }

                        if (tntTier == TntTier.None)
                        {
                            tntTier = TryGetTntTier(em, sourceEntity);

                            if (tntTier != TntTier.None)
                            {
                                tntNormalDamagePercent = tntTier == TntTier.T01
                                    ? t01NormalDamagePercent
                                    : t02NormalDamagePercent;
                            }
                        }

                        if (tntDamageLoggingEnabled)
                        {
                            TntDamageDiagnosticService.TryLogCastleDamageEvent(
                                em,
                                entity,
                                damageEvent,
                                nowUtc);
                        }

                        if (!TryGetDefenderKeyFromDamagedEntity(em, targetEntity, out Entity castleHeartEntity, out Entity defenderKeyEntity))
                        {
                            continue;
                        }

                        string persistentKey = null;

                        if (defenderKeyEntity != Entity.Null)
                        {
                            if (em.HasComponent<ClanTeam>(defenderKeyEntity))
                            {
                                persistentKey = PersistentKeyHelper.GetClanKey(em, defenderKeyEntity);
                            }
                            else if (em.TryGetComponentData<User>(defenderKeyEntity, out User u))
                            {
                                persistentKey = PersistentKeyHelper.GetUserKey(u.PlatformId);
                            }
                        }

                        if (string.IsNullOrEmpty(persistentKey))
                        {
                            continue;
                        }

                        var context = new DamageContext(
                            em,
                            entity,
                            castleHeartEntity,
                            sourceEntity,
                            persistentKey,
                            damageEvent,
                            nowUtc,
                            logDebug,
                            optInEnabled,
                            purchasedOrpEnabled,
                            orpEnabled,
                            announceOfflineRaid,
                            offlineRaidMapIconEnabled,
                            announceDecayed,
                            forcedRaidDay,
                            configuredGraceMinutes,
                            weaponRaidingEnabled,
                            tntRaidingEnabled,
                            weaponDamageMultiplier,
                            tntTier,
                            nativeTntModifiers,
                            tntNormalDamagePercent,
                            tntTier == TntTier.T01
                                ? t01CastleWallDamagePercent
                                : t02CastleWallDamagePercent,
                            useNormalTntDamageAfterBreach);

                        context.IsBreached = em.GetComponentData<CastleHeart>(castleHeartEntity).IsSieged();
                        context.IsDecaying = OfflineProtectionService.IsBaseDecaying(castleHeartEntity, em);

                        EvaluateFastTrackRules(context);

                        if (!context.IsFastTrackApproved)
                        {
                            if (TryBlockByOptInRules(context))
                            {
                                continue;
                            }

                            if (TryBlockByPurchasedOrpRules(context))
                            {
                                continue;
                            }

                            if (TryBlockByOfflineProtection(context))
                            {
                                continue;
                            }
                        }

                        HandleAllowedRaidSignals(context);
                        ApplyConfiguredStructureDamage(context);
                    }
                    catch (Exception ex)
                    {
                        LoggingHelper.Warning("[DamageInterceptorPatch] Error processing specific damage event.", ex);
                    }
                }
            }
            finally
            {
                if (damageEventEntities.IsCreated)
                {
                    damageEventEntities.Dispose();
                }
            }
        }

        private static void EvaluateFastTrackRules(DamageContext context)
        {
            if (context.IsDecaying)
            {
                EnsureAttackerResolved(context);

                if (!IsVampireDamage(context))
                {
                    if (context.LogDebug)
                    {
                        LoggingHelper.Debug("[Damage] Decay raid announcement/icon skipped because damage source did not resolve to a vampire player.");
                    }
                }
                else
                {
                    RaidMapIconService.AddRaidIcon(context.CastleHeartEntity, isOfflineRaid: false, isDecayRaid: true);

                    if (context.AnnounceDecayed && !string.IsNullOrEmpty(context.AttackerName))
                    {
                        AnnounceDecayedBaseDamage(
                            context.Em,
                            context.CastleHeartEntity,
                            context.AttackerName,
                            EnsureDefenderBaseName(context),
                            context.NowUtc);
                    }
                }
            }

            if (context.IsBreached || context.IsDecaying)
            {
                if (context.LogDebug)
                {
                    LoggingHelper.Debug($"[Damage] Allowed: Base {EnsureDefenderBaseName(context)} is Breached({context.IsBreached}) or Decaying({context.IsDecaying}).");
                }

                context.IsFastTrackApproved = true;
            }

            if (!context.IsFastTrackApproved &&
                ShardConfig.DisableOrpForShardHolders.Value &&
                ShardVulnerabilityService.IsVulnerable(context.PersistentKey))
            {
                if (context.LogDebug)
                {
                    var shardEntries = ShardVulnerabilityService.GetEntriesForOwner(context.PersistentKey);
                    string shardDetails = string.Join(" | ", shardEntries.Select(e => $"Shard: {e.ShardPrefabGuid} (Latched By: {e.LatchedByUserKey})"));

                    LoggingHelper.Debug($"[Damage] Allowed: Owner '{context.PersistentKey}' is vulnerable. Details: {shardDetails}");
                }

                context.IsFastTrackApproved = true;
            }
        }

        private static bool IsVampireDamage(DamageContext context)
        {
            return context.AttackerUserEntity != Entity.Null &&
                context.AttackerCharEntity != Entity.Null &&
                context.Em.Exists(context.AttackerCharEntity) &&
                context.Em.HasComponent<PlayerCharacter>(context.AttackerCharEntity);
        }

        private static bool TryBlockByOptInRules(DamageContext context)
        {
            if (!context.OptInEnabled)
            {
                return false;
            }

            bool isDefenderOptedIn = context.ForcedRaidDay || OptInRaidService.IsOptedIn(context.PersistentKey);

            if (!isDefenderOptedIn)
            {
                EnsureAttackerResolved(context);
                BlockDamageAndNotify(
                    context.Em,
                    context.DamageEventEntity,
                    context.AttackerUserEntity,
                    EnsureDefenderBaseName(context),
                    "PROTECTED",
                    OptInRaidingConfig.UseDefaultOptInMode ? "(Opted-Out)" : "(Not Opted-In)",
                    context.NowUtc);

                return true;
            }

            EnsureAttackerResolved(context);

            bool isAttackerOptedIn =
                context.ForcedRaidDay ||
                (!string.IsNullOrEmpty(context.AttackerKey) && OptInRaidService.IsOptedIn(context.AttackerKey));

            if (isAttackerOptedIn)
            {
                return false;
            }

            BlockDamageAndNotify(
                context.Em,
                context.DamageEventEntity,
                context.AttackerUserEntity,
                EnsureDefenderBaseName(context),
                "PROTECTED",
                $"(Attacker {context.AttackerName ?? "Unknown"} is not Opted-In)",
                context.NowUtc);

            return true;
        }

        private static bool TryBlockByPurchasedOrpRules(DamageContext context)
        {
            if (!context.PurchasedOrpEnabled)
            {
                return false;
            }

            if (!PurchasedOrpService.HasProtectionForToday(context.PersistentKey))
            {
                return false;
            }

            if (!RaidToggleSystem.AreRaidsEnabled())
            {
                context.Em.DestroyEntity(context.DamageEventEntity);

                if (context.LogDebug)
                {
                    LoggingHelper.Debug("[Damage] Purchased ORP blocked damage quietly because raids are not currently enabled.");
                }

                return true;
            }

            EnsureAttackerResolved(context);
            BlockDamageAndNotify(
                context.Em,
                context.DamageEventEntity,
                context.AttackerUserEntity,
                EnsureDefenderBaseName(context),
                "PURCHASE PROTECTED",
                "(Purchased ORP)",
                context.NowUtc);

            return true;
        }

        private static bool TryBlockByOfflineProtection(DamageContext context)
        {
            var evaluation = OfflineProtectionService.EvaluateHeartOfflineProtection(
                context.CastleHeartEntity,
                context.Em,
                context.PersistentKey,
                context.NowUtc,
                context.ConfiguredGraceMinutes,
                context.OrpEnabled);

            context.IsInGracePeriod = evaluation.IsInGracePeriod;
            context.AllDefendersOffline = evaluation.AllDefendersOffline;
            context.CheckedAllDefendersOffline = evaluation.CheckedAllDefendersOffline;
            context.WillBeFullyProtected = evaluation.IsProtected;

            if (!context.WillBeFullyProtected)
            {
                return false;
            }

            if (!RaidToggleSystem.AreRaidsEnabled())
            {
                context.Em.DestroyEntity(context.DamageEventEntity);

                if (context.LogDebug)
                {
                    LoggingHelper.Debug("[Damage] ORP blocked damage quietly because raids are not currently enabled.");
                }

                return true;
            }

            EnsureAttackerResolved(context);
            BlockDamageAndNotify(
                context.Em,
                context.DamageEventEntity,
                context.AttackerUserEntity,
                EnsureDefenderBaseName(context),
                "OFFLINE PROTECTED",
                string.Empty,
                context.NowUtc);

            return true;
        }

        private static void HandleAllowedRaidSignals(DamageContext context)
        {
            /*
				Offline raid icons and announcements are intentionally handled after
				protection/opt-in blocking has had a chance to stop the damage.
			*/
            if ((!context.AnnounceOfflineRaid && !context.ShowOfflineRaidIcon) || context.IsDecaying)
            {
                return;
            }

            if (!RaidToggleSystem.AreRaidsEnabled())
            {
                if (context.LogDebug)
                {
                    LoggingHelper.Debug("[Damage] Offline raid announcement/icon skipped because raids are not currently enabled.");
                }

                return;
            }

            if (!IsEligibleOfflineRaidSignal(context))
            {
                return;
            }

            EnsureAttackerResolved(context);

            if (IsSiegeWeaponDamage(context))
            {
                if (context.ShowOfflineRaidIcon)
                {
                    RaidMapIconService.AddRaidIcon(context.CastleHeartEntity, isOfflineRaid: true, isDecayRaid: false);
                }

                if (context.AnnounceOfflineRaid)
                {
                    MakeOfflineSiegeAnnouncement(
                        context.Em,
                        context.CastleHeartEntity,
                        context.AttackerName,
                        EnsureDefenderBaseName(context),
                        context.NowUtc);
                }
            }
        }

        private static bool IsEligibleOfflineRaidSignal(DamageContext context)
        {
            if (context.IsBreached)
            {
                if (context.LogDebug)
                {
                    LoggingHelper.Debug("[Damage] Offline raid announcement/icon skipped because the base is already breached.");
                }

                return false;
            }

            EnsureAllDefendersOfflineChecked(context);

            if (!context.AllDefendersOffline)
            {
                if (context.LogDebug)
                {
                    LoggingHelper.Debug("[Damage] Offline raid announcement/icon skipped because at least one defender is online.");
                }

                return false;
            }

            if (OfflineGraceService.TryGetOfflineStartTime(context.PersistentKey, out DateTime offlineStartTimeUtc))
            {
                TimeSpan timeOffline = context.NowUtc - offlineStartTimeUtc;

                if (context.ConfiguredGraceMinutes > 0 && timeOffline.TotalMinutes < context.ConfiguredGraceMinutes)
                {
                    context.IsInGracePeriod = true;

                    if (context.LogDebug)
                    {
                        LoggingHelper.Debug("[Damage] Offline raid announcement/icon skipped because defenders are still in ORP grace.");
                    }

                    return false;
                }
            }

            return true;
        }

        private static void ApplyConfiguredStructureDamage(DamageContext context)
        {
            if (!context.IsTntDamage &&
                context.DamageEvent.MaterialModifiers.StoneStructure > 0f)
            {
                return;
            }

            if (context.IsTntDamage)
            {
                if (!context.TntRaidingEnabled)
                {
                    return;
                }
            }
            else if (!context.WeaponRaidingEnabled)
            {
                return;
            }

            EnsureAttackerResolved(context);

            if (context.AttackerUserEntity == Entity.Null)
            {
                if (context.LogDebug && context.IsTntDamage)
                {
                    LoggingHelper.Debug("[TNT Raiding] Stone damage was not enabled because the placing player could not be resolved.");
                }

                return;
            }

            var modifiedModifiers = context.DamageEvent.MaterialModifiers;

            if (context.IsTntDamage)
            {
                float nativeExplosiveDamage = context.NativeTntModifiers.Explosives;

                if (nativeExplosiveDamage <= 0f)
                {
                    if (context.LogDebug)
                    {
                        LoggingHelper.Debug("[TNT Raiding] Stone damage was not enabled because the native Explosives modifier was not positive.");
                    }

                    return;
                }

                float appliedPercent =
                    context.IsBreached && context.UseNormalTntDamageAfterBreach
                        ? context.TntNormalDamagePercent
                        : context.TntCastleWallDamagePercent;

                modifiedModifiers.StoneStructure =
                    nativeExplosiveDamage * (appliedPercent / 100f);

                if (modifiedModifiers.StoneStructure <= 0f)
                {
                    if (context.LogDebug)
                    {
                        LoggingHelper.Debug(
                            $"[TNT Raiding] Stone damage is disabled at {appliedPercent}% while breached={context.IsBreached}.");
                    }

                    return;
                }
            }
            else
            {
                modifiedModifiers.StoneStructure = context.WeaponDamageMultiplier;
            }

            context.DamageEvent.MaterialModifiers = modifiedModifiers;

            context.Em.SetComponentData(context.DamageEventEntity, context.DamageEvent);

            if (context.LogDebug)
            {
                if (context.IsTntDamage)
                {
                    float appliedPercent =
                        context.IsBreached && context.UseNormalTntDamageAfterBreach
                            ? context.TntNormalDamagePercent
                            : context.TntCastleWallDamagePercent;
                    string damageMode =
                        context.IsBreached && context.UseNormalTntDamageAfterBreach
                            ? "normal-after-breach"
                            : "castle-raid";

                    LoggingHelper.Debug(
                        $"[TNT Raiding] tier={context.TntTier}, StoneStructure={modifiedModifiers.StoneStructure} " +
                        $"(native={context.NativeTntModifiers.Explosives}, percent={appliedPercent}, " +
                        $"mode={damageMode}, breached={context.IsBreached}).");
                }
                else
                {
                    LoggingHelper.Debug(
                        $"[Weapon Raiding] Overwrote StoneStructure modifier to {modifiedModifiers.StoneStructure}.");
                }
            }
        }

        private static void EnsureAttackerResolved(DamageContext context)
        {
            if (context.AttackerResolved)
            {
                return;
            }

            ResolveAttacker(
                context.Em,
                context.SourceEntity,
                ref context.AttackerResolved,
                ref context.AttackerCharEntity,
                ref context.AttackerUserEntity,
                ref context.AttackerName,
                ref context.AttackerKey);
        }

        private static string EnsureDefenderBaseName(DamageContext context)
        {
            if (context.CachedDefenderBaseName == null)
            {
                context.CachedDefenderBaseName = GetDefenderBaseName(context.Em, context.CastleHeartEntity);
            }

            return context.CachedDefenderBaseName;
        }

        private static void EnsureAllDefendersOfflineChecked(DamageContext context)
        {
            if (context.CheckedAllDefendersOffline)
            {
                return;
            }

            context.AllDefendersOffline = OfflineProtectionService.AreAllDefendersActuallyOffline(context.CastleHeartEntity, context.Em);
            context.CheckedAllDefendersOffline = true;
        }

        private static void PruneInvalidCooldownEntries(EntityManager em, DateTime nowUtc)
        {
            try
            {
                if (nowUtc < _nextCooldownPruneUtc)
                {
                    return;
                }

                _nextCooldownPruneUtc = nowUtc.AddMinutes(1);
                PruneMissingEntityKeys(em, _lastProtectedMessageTimes);
                PruneMissingEntityKeys(em, _lastOfflineSiegeAnnouncementTimes);
                PruneMissingEntityKeys(em, _lastDecayedRaidAnnouncementTimes);
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[DamageInterceptorPatch] Cooldown prune failed: {ex.Message}");
            }
        }

        private static void PruneMissingEntityKeys(EntityManager em, Dictionary<Entity, DateTime> timestampsByEntity)
        {
            if (timestampsByEntity.Count == 0)
            {
                return;
            }

            var staleEntities = timestampsByEntity.Keys
                .Where(entity => entity == Entity.Null || !em.Exists(entity))
                .ToList();

            foreach (var entity in staleEntities)
            {
                timestampsByEntity.Remove(entity);
            }
        }

        private static void ResolveAttacker(
            EntityManager em,
            Entity sourceEntity,
            ref bool resolved,
            ref Entity charEntity,
            ref Entity userEntity,
            ref string name,
            ref string key)
        {
            resolved = true;

            if (!UserHelper.TryGetPlayerOwnerFromSource(em, sourceEntity, out charEntity, out userEntity))
            {
                return;
            }

            if (userEntity == Entity.Null || !em.Exists(userEntity))
            {
                return;
            }

            if (!OwnerIdentityHelper.TryResolveFromUserEntity(
                em,
                userEntity,
                out OwnerIdentity owner,
                out string ownerError))
            {
                LoggingHelper.Debug($"[DamageInterceptorPatch] Failed to resolve attacker owner identity: {ownerError}");
                return;
            }

            name = owner.CharacterName;
            key = owner.PersistentKey;
        }

        private static void BlockDamageAndNotify(
            EntityManager em,
            Entity eventEntity,
            Entity attackerUserEntity,
            string defenderBaseName,
            string protectionStatusKeyword,
            string protectionContext,
            DateTime nowUtc)
        {
            em.DestroyEntity(eventEntity);

            if (attackerUserEntity == Entity.Null)
            {
                return;
            }

            if (_lastProtectedMessageTimes.TryGetValue(attackerUserEntity, out DateTime lastTimeSent) &&
                (nowUtc - lastTimeSent) <= OfflineProtectedMessageCooldown)
            {
                return;
            }

            var messageBuilder = new StringBuilder();

            messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} is "));
            messageBuilder.Append(ChatColors.AccentText(protectionStatusKeyword));

            if (!string.IsNullOrEmpty(protectionContext))
            {
                messageBuilder.Append(ChatColors.InfoText($" {protectionContext}."));
            }
            else
            {
                messageBuilder.Append(ChatColors.InfoText("."));
            }

            if (em.TryGetComponentData<User>(attackerUserEntity, out User attackerUser))
            {
                FixedString512Bytes message = new FixedString512Bytes(messageBuilder.ToString());
                ServerChatUtils.SendSystemMessageToClient(em, attackerUser, ref message);

                _lastProtectedMessageTimes[attackerUserEntity] = nowUtc;
            }
        }

        private static bool TryGetDefenderKeyFromDamagedEntity(
            EntityManager em,
            Entity damagedEntity,
            out Entity castleHeartEntity,
            out Entity defenderKeyEntity)
        {
            castleHeartEntity = Entity.Null;
            defenderKeyEntity = Entity.Null;

            if (em.HasComponent<CastleHeartConnection>(damagedEntity))
            {
                castleHeartEntity = em.GetComponentData<CastleHeartConnection>(damagedEntity).CastleHeartEntity._Entity;
            }
            else if (em.HasComponent<CastleHeart>(damagedEntity))
            {
                castleHeartEntity = damagedEntity;
            }

            if (castleHeartEntity == Entity.Null ||
                !em.Exists(castleHeartEntity) ||
                !em.HasComponent<CastleHeart>(castleHeartEntity))
            {
                return false;
            }

            if (!OwnershipCacheService.TryGetHeartOwner(castleHeartEntity, out Entity ownerUserEntity) || ownerUserEntity == Entity.Null)
            {
                return false;
            }

            if (!em.Exists(ownerUserEntity) || !em.HasComponent<User>(ownerUserEntity))
            {
                return false;
            }

            if (OwnershipCacheService.TryGetUserClan(ownerUserEntity, out Entity ownerClanEntity) &&
                ownerClanEntity != Entity.Null &&
                em.Exists(ownerClanEntity))
            {
                defenderKeyEntity = ownerClanEntity;
            }
            else
            {
                defenderKeyEntity = ownerUserEntity;
            }

            return true;
        }

        private static string GetDefenderBaseName(EntityManager em, Entity castleHeartEntity)
        {
            if (castleHeartEntity != Entity.Null &&
                em.TryGetComponentData<UserOwner>(castleHeartEntity, out UserOwner heartOwner) &&
                em.Exists(heartOwner.Owner._Entity) &&
                em.TryGetComponentData<User>(heartOwner.Owner._Entity, out User ownerUserData))
            {
                if (em.TryGetComponentData<NameableInteractable>(castleHeartEntity, out NameableInteractable castleNameComp) &&
                    !string.IsNullOrWhiteSpace(castleNameComp.Name.ToString()))
                {
                    return $"'{castleNameComp.Name.ToString()}'";
                }

                return $"{ownerUserData.CharacterName.ToString()}'s base";
            }

            return "A base";
        }

        private static bool IsSiegeWeaponDamage(DamageContext context)
        {
            if (context.AttackerCharEntity != Entity.Null &&
                HasSiegeGolemBuff(context.Em, context.AttackerCharEntity))
            {
                return true;
            }

            if (context.IsTntDamage)
            {
                if (string.IsNullOrEmpty(context.AttackerName))
                {
                    context.AttackerName = "Explosives";
                }
                return true;
            }

            return false;
        }

        private static TntTier TryGetTntTier(EntityManager em, Entity sourceEntity)
        {
            if (sourceEntity == Entity.Null || !em.Exists(sourceEntity))
            {
                return TntTier.None;
            }

            if (_tntTierBySource.TryGetValue(sourceEntity, out TntTier cachedTier))
            {
                return cachedTier;
            }

            TntTier resolvedTier = ResolveTntTier(em, sourceEntity, 0);

            if (_tntTierBySource.Count >= MaxCachedTntSources)
            {
                _tntTierBySource.Clear();
            }

            _tntTierBySource[sourceEntity] = resolvedTier;
            return resolvedTier;
        }

        private static TntTier ResolveTntTier(EntityManager em, Entity sourceEntity, int depth)
        {
            if (depth > MaxTntSourceDepth ||
                sourceEntity == Entity.Null ||
                !em.Exists(sourceEntity))
            {
                return TntTier.None;
            }

            if (em.TryGetComponentData(sourceEntity, out PrefabGUID sourcePrefabGuid))
            {
                TntTier directTier = PrefabData.GetTntTier(sourcePrefabGuid);

                if (directTier != TntTier.None)
                {
                    return directTier;
                }
            }

            int nextDepth = depth + 1;

            if (em.TryGetComponentData(sourceEntity, out EntityOwner entityOwner) &&
                entityOwner.Owner != sourceEntity)
            {
                TntTier ownerTier = ResolveTntTier(em, entityOwner.Owner, nextDepth);

                if (ownerTier != TntTier.None)
                {
                    return ownerTier;
                }
            }

            if (em.TryGetComponentData(sourceEntity, out EntityCreator entityCreator))
            {
                Entity creatorEntity = entityCreator.Creator._Entity;

                if (creatorEntity != sourceEntity &&
                    creatorEntity != Entity.Null)
                {
                    TntTier creatorTier = ResolveTntTier(em, creatorEntity, nextDepth);

                    if (creatorTier != TntTier.None)
                    {
                        return creatorTier;
                    }
                }
            }

            return TntTier.None;
        }

        private static float ClampTntPercent(float value)
        {
            return Math.Clamp(value, 0f, 1000f);
        }

        private static EntityTypeModifiers ScaleMaterialModifiers(
            EntityTypeModifiers modifiers,
            float multiplier)
        {
            modifiers.Human *= multiplier;
            modifiers.Undead *= multiplier;
            modifiers.Demon *= multiplier;
            modifiers.Mechanical *= multiplier;
            modifiers.Beast *= multiplier;
            modifiers.CastleObject *= multiplier;
            modifiers.PlayerVampire *= multiplier;
            modifiers.PvEVampire *= multiplier;
            modifiers.ShadowVBlood *= multiplier;
            modifiers.BasicStructure *= multiplier;
            modifiers.ReinforcedStructure *= multiplier;
            modifiers.FortifiedStructure *= multiplier;
            modifiers.StoneStructure *= multiplier;
            modifiers.SiegeAltar *= multiplier;
            modifiers.Wood *= multiplier;
            modifiers.Minerals *= multiplier;
            modifiers.Vegetation *= multiplier;
            modifiers.LightArmor *= multiplier;
            modifiers.VBlood *= multiplier;
            modifiers.Magic *= multiplier;
            modifiers.Explosives *= multiplier;
            modifiers.MassiveResource *= multiplier;
            modifiers.MonsterGate *= multiplier;
            return modifiers;
        }

        private static void MakeOfflineSiegeAnnouncement(
            EntityManager em,
            Entity castleHeartEntity,
            string attackerName,
            string defenderBaseName,
            DateTime nowUtc)
        {
            if (!_lastOfflineSiegeAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) ||
                (nowUtc - lastAnnTime) > OfflineSiegeAnnouncementCooldown)
            {
                var messageBuilder = new StringBuilder();

                messageBuilder.Append(ChatColors.HighlightText(defenderBaseName ?? "Unknown"));
                messageBuilder.Append(ChatColors.InfoText(" is being "));
                messageBuilder.Append(ChatColors.WarningText("offline raided"));
                messageBuilder.Append(ChatColors.InfoText(" by "));
                messageBuilder.Append(ChatColors.HighlightText(attackerName ?? "Unknown"));
                messageBuilder.Append(ChatColors.InfoText("!"));

                FixedString512Bytes annMsg = new FixedString512Bytes(messageBuilder.ToString());
                ServerChatUtils.SendSystemMessageToAllClients(em, ref annMsg);

                _lastOfflineSiegeAnnouncementTimes[castleHeartEntity] = nowUtc;
            }
        }

        private static void AnnounceDecayedBaseDamage(
            EntityManager em,
            Entity castleHeartEntity,
            string attackerName,
            string defenderBaseName,
            DateTime nowUtc)
        {
            if (!_lastDecayedRaidAnnouncementTimes.TryGetValue(castleHeartEntity, out DateTime lastAnnTime) ||
                (nowUtc - lastAnnTime) > DecayedRaidAnnouncementCooldown)
            {
                var messageBuilder = new StringBuilder();

                messageBuilder.Append(ChatColors.InfoText($"{defenderBaseName} "));
                messageBuilder.Append(ChatColors.WarningText("is DECAYED"));
                messageBuilder.Append(ChatColors.InfoText(" and being raided by "));
                messageBuilder.Append(ChatColors.HighlightText(attackerName ?? "Unknown"));
                messageBuilder.Append(ChatColors.InfoText("!"));

                FixedString512Bytes decayedAnnMessage = new FixedString512Bytes(messageBuilder.ToString());
                ServerChatUtils.SendSystemMessageToAllClients(em, ref decayedAnnMessage);

                _lastDecayedRaidAnnouncementTimes[castleHeartEntity] = nowUtc;
            }
        }

        private static bool HasSiegeGolemBuff(EntityManager em, Entity playerCharacterEntity)
        {
            if (!em.Exists(playerCharacterEntity) || !em.HasComponent<PlayerCharacter>(playerCharacterEntity))
            {
                return false;
            }

            if (!_sgmCached)
            {
                return false;
            }

            try
            {
                return _serverGameManager.TryGetBuff(playerCharacterEntity, PrefabData.SiegeGolemBuff.Guid, out _);
            }
            catch (Exception ex)
            {
                LoggingHelper.Debug($"[DamageInterceptorPatch] Siege golem buff check failed: {ex.Message}");
                return false;
            }
        }
    }
}
