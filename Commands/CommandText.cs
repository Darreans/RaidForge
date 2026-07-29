using System;
using System.Globalization;
using RaidForge.Utils;

namespace RaidForge.Commands
{
    internal static class CommandText
    {
        public static string ConfigLine(string key, string value)
        {
            return ChatColors.InfoText($"{key}: ") + value;
        }

        public static string StatusLine(string key, string value)
        {
            return ConfigLine(key, value);
        }

        public static string CountText(int count)
        {
            return ChatColors.AccentText(count.ToString(CultureInfo.InvariantCulture));
        }

        public static string EnabledText(bool value)
        {
            return value
                ? ChatColors.SuccessText("Enabled")
                : ChatColors.WarningText("Disabled");
        }

        public static string YesNoText(bool value)
        {
            return value
                ? ChatColors.SuccessText("Yes")
                : ChatColors.WarningText("No");
        }

        public static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            if (duration.TotalHours >= 1)
            {
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            }

            if (duration.TotalMinutes >= 1)
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }

            return $"{Math.Max(0, duration.Seconds)}s";
        }

        public static string FormatTime(TimeSpan time)
        {
            return (DateTime.MinValue + time).ToString("h:mm tt", CultureInfo.InvariantCulture);
        }
    }
}
