using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace UacSentinel
{
    /// <summary>规则风险引擎：先算分，模型只做解释/微调，且不能把高风险降下来。</summary>
    internal static class RiskEngine
    {
        internal const string TierLow = "low";
        internal const string TierMedium = "medium";
        internal const string TierHigh = "high";

        private static readonly string[] DestructiveOps = new string[]
        {
            "delete", "registry", "service", "startup", "boot", "download", "defender"
        };

        internal static void Assess(ElevEvent ev)
        {
            int score = 0;

            if (!ev.Signed) score += 30;
            else if (!ev.Verified) score += 20;
            else if (!KnownPublisher(ev.Publisher)) score += 12;

            string path = (ev.TargetPath ?? string.Empty).ToLowerInvariant();
            if (path.Contains("\\temp\\") || path.Contains("\\downloads\\")) score += 25;
            else if (path.Contains("\\appdata\\")) score += 12;

            string op = Classify(ev);
            ev.OpKind = op;
            score += OpWeight(op);

            if (SamePublisher(ev)) score -= 10;
            if (string.IsNullOrEmpty(ev.SourcePath)) score += 10;

            if (score < 0) score = 0;
            if (score > 100) score = 100;
            ev.RiskScore = score;
            ev.Tier = score >= 65 ? TierHigh : (score >= 30 ? TierMedium : TierLow);
            ev.FingerprintKey = Fingerprint(ev);
        }

        internal static bool IsDestructive(string op)
        {
            if (string.IsNullOrEmpty(op)) return false;
            foreach (string d in DestructiveOps)
            {
                if (string.Equals(d, op, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int OpWeight(string op)
        {
            switch (op)
            {
                case "delete": return 40;
                case "registry": return 30;
                case "service": return 30;
                case "startup": return 30;
                case "boot": return 45;
                case "download": return 40;
                case "defender": return 35;
                case "execute": return 15;
                case "install": return 5;
                default: return 0;
            }
        }

        private static string Classify(ElevEvent ev)
        {
            string name = (ev.TargetName ?? string.Empty).ToLowerInvariant();
            string cmd = (ev.CommandLine ?? string.Empty).ToLowerInvariant();
            string all = name + " " + cmd;

            if (ContainAny(all, "-encodedcommand", "-enc ", "frombase64string", "downloadstring", "invoke-webrequest",
                "invoke-expression", " iex ", "curl ", "wget ", "certutil -urlcache", "bitsadmin"))
                return "download";
            if (ContainAny(all, "vssadmin delete", "bcdedit", "wevtutil cl", "cipher /w", "format ", "diskpart"))
                return "boot";
            if (ContainAny(all, "set-mppreference", "add-mppreference", "defender", "disableantispyware", "disableantivirus"))
                return "defender";
            if (ContainAny(all, "schtasks /create", "schtasks -create", "new-scheduledtask", "reg add", "reg delete",
                "new-itemproperty", "set-itemproperty", "regedit"))
                return "registry";
            if (ContainAny(all, "sc create", "sc delete", "sc config", "new-service", "net start", "net stop"))
                return "service";
            if (ContainAny(all, "remove-item", "rmdir", " rd ", "del ", "erase ", "unlink", "rm -rf", "move-item -force"))
                return "delete";
            if (ContainAny(all, "copy-item", "move-item", "set-content", "out-file", "add-content", "icacls", "takeown"))
                return "startup";
            if (name == "cmd" || name == "powershell" || name == "pwsh" || name == "wscript" || name == "cscript" ||
                name == "mshta" || name == "rundll32" || name == "regsvr32" || name == "bash" || name == "wsl")
                return "execute";
            if (name == "msiexec" || name.Contains("setup") || name.Contains("install") || name.Contains("update") ||
                (ev.TargetPath ?? string.Empty).ToLowerInvariant().EndsWith(".msi"))
                return "install";
            return "unknown";
        }

        private static bool ContainAny(string text, params string[] needles)
        {
            foreach (string n in needles)
            {
                if (text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        private static bool KnownPublisher(string publisher)
        {
            if (string.IsNullOrEmpty(publisher)) return false;
            string p = publisher.ToLowerInvariant();
            return p.Contains("microsoft") || p.Contains("google") || p.Contains("apple") || p.Contains("adobe") ||
                   p.Contains("tencent") || p.Contains("alibaba") || p.Contains("bytedance") || p.Contains("nvidia") ||
                   p.Contains("intel") || p.Contains("amd");
        }

        private static bool SamePublisher(ElevEvent ev)
        {
            if (string.IsNullOrEmpty(ev.SourcePublisher) || string.IsNullOrEmpty(ev.Publisher)) return false;
            return string.Equals(ev.SourcePublisher.Trim(), ev.Publisher.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        internal static string Fingerprint(ElevEvent ev)
        {
            string args = NormalizeArgs(ev.CommandLine);
            return string.Join("|", new string[]
            {
                Lower(ev.SourcePath),
                Lower(ev.SourcePublisher),
                Lower(ev.TargetPath),
                Lower(ev.Publisher),
                Lower(ev.OpKind),
                args
            });
        }

        private static string NormalizeArgs(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return string.Empty;
            string s = cmd.ToLowerInvariant();
            s = System.Text.RegularExpressions.Regex.Replace(s, "\"[^\"]*\"", "\"<p>\"");
            s = System.Text.RegularExpressions.Regex.Replace(s, "[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<g>");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\d+", "<n>");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > 120) s = s.Substring(0, 120);
            return s;
        }

        private static string Lower(string s)
        {
            return (s ?? string.Empty).ToLowerInvariant();
        }

        internal static string TierText(string tier)
        {
            if (tier == TierHigh) return "高风险";
            if (tier == TierLow) return "低风险";
            return "中风险";
        }
    }

    internal sealed class TrustEntry
    {
        public string Key { get; set; }
        public int Count { get; set; }
        public DateTime Last { get; set; }
        public DateTime LastDenied { get; set; }
        public string Op { get; set; }
        public string Target { get; set; }
        public string Source { get; set; }
    }

    /// <summary>信任库：只有用户手动允许过的同一「来源+目标+操作指纹」累计 3 次才静默；破坏性操作永不静默。</summary>
    internal sealed class TrustStore
    {
        private readonly string _path;
        private readonly Dictionary<string, TrustEntry> _map = new Dictionary<string, TrustEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        internal TrustStore(string path)
        {
            _path = path;
            Load();
        }

        internal bool IsTrusted(string key, string op)
        {
            if (string.IsNullOrEmpty(key)) return false;
            TrustEntry e;
            if (!_map.TryGetValue(key, out e)) return false;
            if (RiskEngine.IsDestructive(op)) return false;
            if (e.Count < 3) return false;
            if (e.LastDenied != DateTime.MinValue && (DateTime.Now - e.LastDenied).TotalDays < 7) return false;
            return true;
        }

        internal void RecordAllowed(string key, string op, string target, string source)
        {
            if (string.IsNullOrEmpty(key)) return;
            TrustEntry e;
            if (!_map.TryGetValue(key, out e))
            {
                e = new TrustEntry();
                e.Key = key;
                _map[key] = e;
            }
            e.Count++;
            e.Last = DateTime.Now;
            e.Op = op;
            e.Target = target;
            e.Source = source;
            Save();
        }

        internal void RecordDenied(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            TrustEntry e;
            if (!_map.TryGetValue(key, out e))
            {
                e = new TrustEntry();
                e.Key = key;
                _map[key] = e;
            }
            e.Count = 0;
            e.LastDenied = DateTime.Now;
            Save();
        }

        /// <summary>预判：这个来源是否已经有稳定的、非破坏性的信任目标（用于提前静音）。</summary>
        internal bool PredictSilent(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath)) return false;
            string src = sourcePath.ToLowerInvariant();
            List<TrustEntry> mine = _map.Values.Where(x => (x.Source ?? string.Empty).ToLowerInvariant() == src).ToList();
            if (mine.Count == 0) return false;
            List<TrustEntry> trusted = mine.Where(x => x.Count >= 3 && !RiskEngine.IsDestructive(x.Op)).ToList();
            if (trusted.Count == 0) return false;
            int distinctTargets = mine.Select(x => x.Target).Distinct().Count();
            return distinctTargets == 1;
        }

        internal int Count
        {
            get { return _map.Count; }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                List<TrustEntry> list = _json.Deserialize<List<TrustEntry>>(File.ReadAllText(_path, Encoding.UTF8));
                if (list == null) return;
                foreach (TrustEntry e in list)
                {
                    if (e != null && !string.IsNullOrEmpty(e.Key)) _map[e.Key] = e;
                }
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(_path, _json.Serialize(_map.Values.ToList()), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class AgentWatch
    {
        private static readonly string[] Defaults = new string[]
        {
            "deepseek harness", "doubao", "qwen", "tongyi", "workbuddy", "xiaoshuo", "marvis", "yuanbao", "kimi", "chatglm"
        };

        internal static bool AnyRunning()
        {
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string name = (p.ProcessName ?? string.Empty).ToLowerInvariant();
                        if (name == "explorer" || name == "svchost") continue;
                        foreach (string key in Defaults)
                        {
                            if (name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return false;
        }
    }
}
