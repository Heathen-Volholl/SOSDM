using System;
using System.Collections.Generic;
using System.IO;

namespace SOSDM.Vintage
{
    public class RulesEngine
    {
        public string LastReason { get; private set; } = "OK";

        // Returns true = accept, false = reject.
        public bool EvaluateRule(string ruleName, IDictionary<string, object> facts)
        {
            LastReason = "OK";

            if (!string.Equals(ruleName, "DocumentQuality", StringComparison.OrdinalIgnoreCase))
                return true; // unknown rules pass-open

            var size = GetLong(facts, "FileSize");
            var name = GetString(facts, "FileName") ?? "";
            var ext  = (GetString(facts, "Extension") ?? Path.GetExtension(name)).ToLowerInvariant();

            // allow only known extensions (very small allowlist)
            if (ext != ".pdf" && ext != ".txt" && ext != ".xml")
            {
                LastReason = $"unsupported extension '{ext}'";
                return false;
            }

            // minimal size sanity (super lenient)
            if (ext == ".pdf" && size < 256) { LastReason = "pdf too small (<256 bytes)"; return false; }
            if (ext == ".txt" && size < 32)  { LastReason = "txt too small (<32 bytes)";  return false; }
            if (ext == ".xml" && size < 64)  { LastReason = "xml too small (<64 bytes)";  return false; }

            // reject obvious temp files
            if (name.StartsWith("~$", StringComparison.Ordinal))         { LastReason = "temp/lock file"; return false; }
            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) { LastReason = "tmp file";       return false; }

            // DO NOT penalize numeric-only filenames like 250820481v1.pdf
            return true;
        }

        private static long GetLong(IDictionary<string, object> dict, string key)
            => dict != null && dict.TryGetValue(key, out var v) && v is IConvertible ? Convert.ToInt64(v) : 0L;

        private static string GetString(IDictionary<string, object> dict, string key)
            => dict != null && dict.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    }
}
