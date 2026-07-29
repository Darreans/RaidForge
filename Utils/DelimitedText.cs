using System.Collections.Generic;
using System.Text;

namespace RaidForge.Utils
{
    internal static class DelimitedText
    {
        public static string Escape(string value, char delimiter)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool mustQuote = value.IndexOf(delimiter) >= 0 ||
                value.IndexOf('"') >= 0 ||
                value.IndexOf('\r') >= 0 ||
                value.IndexOf('\n') >= 0;

            if (!mustQuote)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static string[] ParseLine(string line, char delimiter)
        {
            var fields = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == delimiter && !inQuotes)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
