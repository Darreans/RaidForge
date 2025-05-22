using BepInEx.Logging;
using RaidForge.Config;

namespace RaidForge.Utils
{
    public static class LoggingHelper
    {
        private static ManualLogSource _logger;

        private static bool IsVerboseLoggingGloballyEnabled => TroubleshootingConfig.EnableVerboseLogging?.Value ?? false;

        public static void Initialize(ManualLogSource logger)
        {
            _logger = logger;
            if (_logger == null)
            {
                System.Console.WriteLine("[RaidForge.LoggingHelper] CRITICAL: Logger passed to Initialize is null!");
            }
        }

        public static void Info(string message)
        {
            _logger?.LogInfo(message);
        }

        public static void Warning(string message, System.Exception ex = null) 
        {
            if (_logger == null) return;
            if (ex != null)
            {
                _logger.LogWarning($"{message} | Exception: {ex.ToString()}");
            }
            else
            {
                _logger.LogWarning(message);
            }
        }

        public static void Error(string message, System.Exception ex = null)
        {
            if (_logger == null)
            {
                System.Console.WriteLine($"[RaidForge.LoggingHelper ERROR (logger not init)]: {message} {(ex != null ? ex.ToString() : "")}");
                return;
            }

            if (ex != null)
            {
                _logger.LogError($"{message} | Exception: {ex.ToString()}");
            }
            else
            {
                _logger.LogError(message);
            }
        }

        public static void Debug(string message)
        {
            if (IsVerboseLoggingGloballyEnabled && _logger != null)
            {
                _logger.LogDebug(message);
            }
        }
    }
}