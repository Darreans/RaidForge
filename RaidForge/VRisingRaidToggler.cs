using BepInEx.Logging;
using Unity.Entities;
using Bloodstone.API;
using ProjectM;
using System;
using System.Reflection;

namespace RaidForge
{
    public static class VrisingRaidToggler
    {
        private static bool _scanned;
        private static FieldInfo _settingsField;
        private static FieldInfo _castleDamageField;

        public static void EnableRaids(ManualLogSource logger)
        {
            bool ok1 = SetCastleDamageMode("Always");
            bool ok2 = SetServerGameBalanceCastleDamage("Always");
        }

        public static void DisableRaids(ManualLogSource logger)
        {
            bool ok1 = SetCastleDamageMode("Never");
            bool ok2 = SetServerGameBalanceCastleDamage("Never");
        }

        private static bool SetCastleDamageMode(string val)
        {
            var world = VWorld.Server;
            if (world == null) return false;

            var sgsSystem = world.GetExistingSystemManaged<ServerGameSettingsSystem>();
            if (sgsSystem == null) return false;

            if (!_scanned)
            {
                _scanned = true;
                FindCastleDamageField(sgsSystem);
            }
            if (_settingsField == null || _castleDamageField == null)
            {
                return false;
            }

            var settingsObj = _settingsField.GetValue(sgsSystem);
            if (settingsObj == null)
            {
                return false;
            }

            try
            {
                object cdmValue = Enum.Parse(_castleDamageField.FieldType, val);
                _castleDamageField.SetValue(settingsObj, cdmValue);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SetServerGameBalanceCastleDamage(string val)
        {
            var world = VWorld.Server;
            if (world == null) return false;

            var em = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadWrite<ServerGameBalanceSettings>());
            var eArray = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            try
            {
                if (eArray.Length == 0)
                {
                    return false;
                }

                var entity = eArray[0];
                var sgb = em.GetComponentData<ServerGameBalanceSettings>(entity);

                var cdmType = sgb.CastleDamageMode.GetType();
                object cdmValue = Enum.Parse(cdmType, val);

                sgb.CastleDamageMode = (CastleDamageMode)cdmValue;
                em.SetComponentData(entity, sgb);
                return true;
            }
            finally
            {
                eArray.Dispose();
            }
        }

        private static void FindCastleDamageField(ServerGameSettingsSystem sgsSystem)
        {
            var sgsType = sgsSystem.GetType();
            var fields = sgsType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var f in fields)
            {
                if (f.Name.ToLower().Contains("settings"))
                {
                    var obj = f.GetValue(sgsSystem);
                    if (obj != null)
                    {
                        var cdmField = obj.GetType().GetField("CastleDamageMode",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (cdmField != null)
                        {
                            _settingsField = f;
                            _castleDamageField = cdmField;
                            return;
                        }
                    }
                }
            }

            // fallback
            foreach (var f in fields)
            {
                var obj = f.GetValue(sgsSystem);
                if (obj != null)
                {
                    var cdmField = obj.GetType().GetField("CastleDamageMode",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (cdmField != null)
                    {
                        _settingsField = f;
                        _castleDamageField = cdmField;
                        return;
                    }
                }
            }
        }
    }
}
