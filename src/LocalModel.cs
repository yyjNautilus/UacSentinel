using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace UacSentinel
{
    internal sealed class ModelVerdict
    {
        internal string Tier;
        internal string Reason;
    }

    /// <summary>本地小模型：只做“解释 + 严重度建议”，永远不能把规则判定的等级降下来。</summary>
    internal static class LocalModel
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static DateTime _downUntil = DateTime.MinValue;
        private static string _lastError;

        internal static string LastError
        {
            get { return _lastError; }
        }

        internal static ModelVerdict Classify(ElevEvent ev, string endpoint, string model, int timeoutMs)
        {
            if (DateTime.Now < _downUntil) return null;
            try
            {
                string prompt = BuildPrompt(ev);
                string body = Call(endpoint, model, prompt, timeoutMs);
                if (body == null) return null;
                ModelVerdict v = Parse(body);
                if (v == null) return null;
                return v;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _downUntil = DateTime.Now.AddSeconds(60);
                return null;
            }
        }

        private static string Call(string endpoint, string model, string prompt, int timeoutMs)
        {
            string url = endpoint.TrimEnd('/') + "/api/generate";
            string body = Post(url, BuildPayload(model, prompt, true, timeoutMs), timeoutMs);
            if (body == null)
            {
                body = Post(url, BuildPayload(model, prompt, false, timeoutMs), timeoutMs);
            }
            return body;
        }

        private static string BuildPayload(string model, string prompt, bool withThink, int timeoutMs)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            payload["model"] = model;
            payload["prompt"] = prompt;
            payload["stream"] = false;
            payload["format"] = "json";
            payload["keep_alive"] = 30;
            if (withThink) payload["think"] = false;
            Dictionary<string, object> options = new Dictionary<string, object>();
            options["temperature"] = 0;
            options["num_predict"] = 96;
            options["num_ctx"] = 2048;
            payload["options"] = options;
            return Json.Serialize(payload);
        }

        private static string Post(string url, string json, int timeoutMs)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.KeepAlive = false;
                req.Proxy = null;
                byte[] data = Encoding.UTF8.GetBytes(json);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream())
                {
                    s.Write(data, 0, data.Length);
                }
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream rs = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(rs, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return null;
            }
        }

        private static ModelVerdict Parse(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            Dictionary<string, object> outer = null;
            try
            {
                outer = Json.Deserialize<Dictionary<string, object>>(body);
            }
            catch { return null; }
            if (outer == null || !outer.ContainsKey("response")) return null;
            string text = Convert.ToString(outer["response"]);
            if (string.IsNullOrEmpty(text)) return null;

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            string inner = text.Substring(start, end - start + 1);

            object node = null;
            try { node = Json.DeserializeObject(inner); }
            catch { return null; }

            string risk;
            string why;
            if (!FindRisk(node, out risk, out why)) return null;

            risk = (risk ?? string.Empty).Trim().ToLowerInvariant();
            if (risk != "low" && risk != "medium" && risk != "high") return null;

            why = string.IsNullOrEmpty(why) ? null : why.Trim();
            if (why != null && why.Length > 40) why = why.Substring(0, 40);
            if (why != null && !HasChinese(why)) why = null;

            ModelVerdict v = new ModelVerdict();
            v.Tier = risk;
            v.Reason = why;
            return v;
        }

        private static bool FindRisk(object node, out string risk, out string why)
        {
            risk = null;
            why = null;
            Dictionary<string, object> dict = node as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.ContainsKey("risk"))
                {
                    risk = Convert.ToString(dict["risk"]);
                    if (dict.ContainsKey("why")) why = Convert.ToString(dict["why"]);
                    return true;
                }
                foreach (object v in dict.Values)
                {
                    if (FindRisk(v, out risk, out why)) return true;
                }
                return false;
            }
            object[] arr = node as object[];
            if (arr != null)
            {
                foreach (object v in arr)
                {
                    if (FindRisk(v, out risk, out why)) return true;
                }
            }
            return false;
        }

        private static bool HasChinese(string s)
        {
            foreach (char c in s)
            {
                if (c >= '\u4e00' && c <= '\u9fff') return true;
            }
            return false;
        }

        private static string BuildPrompt(ElevEvent ev)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("你是 Windows UAC 提权请求的风险分级器。只输出 JSON，不要任何解释文字。");
            sb.AppendLine("输出格式：{\"risk\":\"low|medium|high\",\"why\":\"20字内的中文理由\"}");
            sb.AppendLine("标准：");
            sb.AppendLine("- low：知名厂商有效签名、系统目录、常规安装/更新、命令行无可疑动作");
            sb.AppendLine("- medium：签名未知或未签名，但用途正常（安装程序、常规命令）");
            sb.AppendLine("- high：未签名或路径可疑，或涉及删除、写注册表、装服务、改启动项、下载后执行、编码命令、关闭防护");
            sb.AppendLine("例子：");
            sb.AppendLine("来源=豆包 目标=C:\\Windows\\System32\\cmd.exe 发布者=Microsoft Windows 签名=有效 命令行=空 -> {\"risk\":\"low\",\"why\":\"微软签名的命令解释器\"}");
            sb.AppendLine("来源=千问 目标=C:\\Users\\x\\Downloads\\setup.exe 发布者=无 签名=无 -> {\"risk\":\"medium\",\"why\":\"未签名安装包\"}");
            sb.AppendLine("来源=DeepSeek 目标=powershell.exe 签名=无 命令行=powershell -enc SQBFAFgA -> {\"risk\":\"high\",\"why\":\"编码命令疑似下载执行\"}");
            sb.AppendLine("现在输入：");
            sb.AppendLine("来源=" + Safe(ev.SourceName) + " 目标=" + Safe(ev.TargetName) + " 路径=" + Safe(ev.TargetPath));
            sb.AppendLine("发布者=" + Safe(ev.Publisher) + " 签名=" + (ev.Verified ? "有效" : (ev.Signed ? "未验证" : "无")));
            sb.AppendLine("命令行=" + Safe(Truncate(ev.CommandLine, 260)));
            sb.Append("规则初判=" + RiskEngine.TierText(ev.Tier) + "(" + ev.RiskScore + "分," + Safe(ev.OpKind) + ")");
            return sb.ToString();
        }

        private static string Safe(string s)
        {
            return string.IsNullOrEmpty(s) ? "无" : s;
        }

        private static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Length <= n ? s : s.Substring(0, n) + "...";
        }
    }
}
