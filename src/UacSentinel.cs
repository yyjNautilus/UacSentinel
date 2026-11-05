using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace UacSentinel
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Store.Init();
            if (args != null && args.Length >= 3 && args[0] == "--render")
            {
                Render.Run(args);
                return;
            }
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                LogCrash(e.ExceptionObject as Exception);
            };
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                LogCrash(e.Exception);
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Native.TryEnableDpi();
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\UacSentinel.Instance", out created))
            {
                if (!created)
                {
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }

        internal static void LogCrash(Exception ex)
        {
            try
            {
                File.AppendAllText(Path.Combine(Store.Dir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + (ex == null ? "unknown" : ex.ToString()) + Environment.NewLine);
            }
            catch { }
        }
    }

    internal static class Render
    {
        internal static void Run(string[] args)
        {
            string mode = args[1];
            string path = args[2];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (mode == "main" || mode == "demo")
            {
                MainForm form = new MainForm();
                if (mode == "demo") form.LoadDemoData();
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                form.Close();
            }
            else if (mode == "detail")
            {
                Config cfg = Config.Load(Store.ConfigFile);
                DetailForm form = new DetailForm(Models.DemoEvent(), cfg);
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                form.Close();
            }
            else if (mode == "history")
            {
                List<ElevEvent> list = Models.DemoList();
                HistoryForm form = new HistoryForm(list);
                form.Show();
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                Bitmap bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                bmp.Dispose();
                form.Close();
            }

            Application.Exit();
        }
    }

    internal static class Models
    {
        internal static ElevEvent DemoEvent()
        {
            ElevEvent ev = new ElevEvent();
            ev.Id = "demo";
            ev.Time = DateTime.Now.AddSeconds(-12);
            ev.SourceName = "Doubao";
            ev.SourcePath = @"C:\Windows\System32\notepad.exe";
            ev.TargetName = "cmd";
            ev.TargetPath = @"C:\Windows\System32\cmd.exe";
            ev.TargetDesc = "Windows 命令处理程序";
            ev.TargetPid = 1234;
            ev.Publisher = "Microsoft Windows";
            ev.Signed = true;
            ev.Verified = true;
            ev.CommandLine = "cmd.exe /c \"C:\\Users\\nautilus\\AppData\\Local\\Temp\\install_helper.bat\"";
            ev.Intent = "以管理员身份执行命令";
            ev.Status = "allowed";
            ev.Note = "UAC 弹窗中已允许";
            return ev;
        }

        internal static List<ElevEvent> DemoList()
        {
            List<ElevEvent> list = new List<ElevEvent>();
            ElevEvent a = DemoEvent();
            list.Add(a);

            ElevEvent b = new ElevEvent();
            b.Id = "demo2";
            b.Time = DateTime.Now.AddMinutes(-32);
            b.SourceName = "Qwen";
            b.SourcePath = @"C:\Windows\System32\notepad.exe";
            b.TargetName = "setup";
            b.TargetPath = @"C:\Users\nailus\Downloads\setup.exe";
            b.Publisher = "Beijing ByteDance Technology Co., Ltd.";
            b.Signed = true;
            b.Verified = true;
            b.Intent = "安装或更新程序";
            b.Status = "denied";
            b.Note = "弹窗被取消或拒绝";
            list.Add(b);

            ElevEvent c = new ElevEvent();
            c.Id = "demo3";
            c.Time = DateTime.Now.AddMinutes(-74);
            c.SourceName = "DeepSeek";
            c.SourcePath = @"C:\Windows\System32\notepad.exe";
            c.TargetName = "powershell";
            c.TargetPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
            c.Publisher = "Microsoft Windows";
            c.Signed = true;
            c.Verified = true;
            c.Intent = "以管理员身份执行命令";
            c.Status = "allowed";
            c.Note = "UAC 弹窗中已允许";
            list.Add(c);

            return list;
        }
    }

    internal static class Native
    {
        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        internal const uint TOKEN_QUERY = 0x0008;
        internal const int TokenElevation = 20;
        internal const int WM_NCLBUTTONDOWN = 0xA1;
        internal const int HTCAPTION = 0x2;

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool SetProcessDPIAware();

        [DllImport("shcore.dll")]
        internal static extern int SetProcessDpiAwareness(int value);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        internal static extern bool GetTokenInformation(IntPtr token, int infoClass, out int info, int length, out int returnLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        internal static void TryEnableDpi()
        {
            try
            {
                SetProcessDpiAwareness(1);
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        internal static bool? IsElevated(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                IntPtr token;
                if (!OpenProcessToken(h, TOKEN_QUERY, out token))
                {
                    return null;
                }
                try
                {
                    int info;
                    int ret;
                    if (!GetTokenInformation(token, TokenElevation, out info, 4, out ret))
                    {
                        return null;
                    }
                    return info != 0;
                }
                finally
                {
                    CloseHandle(token);
                }
            }
            finally
            {
                CloseHandle(h);
            }
        }

        internal static string GetProcessPath(int pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size))
                {
                    return sb.ToString(0, size);
                }
                return null;
            }
            finally
            {
                CloseHandle(h);
            }
        }
    }

    internal static class Theme
    {
        internal static readonly Color Bg = Color.FromArgb(22, 26, 33);
        internal static readonly Color Card = Color.FromArgb(30, 36, 45);
        internal static readonly Color CardHover = Color.FromArgb(37, 45, 56);
        internal static readonly Color Border = Color.FromArgb(44, 51, 64);
        internal static readonly Color Text = Color.FromArgb(242, 245, 250);
        internal static readonly Color Sub = Color.FromArgb(138, 148, 166);
        internal static readonly Color Dim = Color.FromArgb(96, 105, 121);
        internal static readonly Color Green = Color.FromArgb(53, 208, 127);
        internal static readonly Color Amber = Color.FromArgb(255, 176, 32);
        internal static readonly Color Red = Color.FromArgb(255, 92, 96);
        internal static readonly Color Blue = Color.FromArgb(111, 168, 255);
        internal static readonly Color DangerBg = Color.FromArgb(45, 27, 31);
        internal static readonly Color DangerBorder = Color.FromArgb(94, 47, 52);

        internal static readonly Font Title = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        internal static readonly Font Body = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        internal static readonly Font BodyBold = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        internal static readonly Font Small = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        internal static readonly Font Tiny = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        internal static readonly Font Mono = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
    }

    internal static class Draw
    {
        internal static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Fill(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = Round(r, radius))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPath(b, p);
            }
        }

        internal static void Stroke(Graphics g, Rectangle r, int radius, Color color)
        {
            using (GraphicsPath p = Round(r, radius))
            using (Pen pen = new Pen(color, 1f))
            {
                g.DrawPath(pen, p);
            }
        }
    }

    internal static class Gfx
    {
        internal static void Text(Graphics g, string text, Font font, Color color, int x, int y)
        {
            TextRenderer.DrawText(g, text, font, new Point(x, y), color,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        internal static void Text(Graphics g, string text, Font font, Color color, Rectangle rect, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, rect, color, flags | TextFormatFlags.NoPrefix);
        }

        internal static string Fit(Graphics g, string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            Size s = TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (s.Width <= width) return text;
            string cut = text;
            while (cut.Length > 1)
            {
                cut = cut.Substring(0, cut.Length - 1);
                Size cs = TextRenderer.MeasureText(g, cut + "\u2026", font, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (cs.Width <= width) return cut + "\u2026";
            }
            return "\u2026";
        }

        internal static int Width(Graphics g, string text, Font font)
        {
            return TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }
    }

    internal sealed class ElevEvent
    {
        public string Id { get; set; }
        public DateTime Time { get; set; }
        public string SourceName { get; set; }
        public string SourcePath { get; set; }
        public int ConsentPid { get; set; }
        public string TargetName { get; set; }
        public string TargetPath { get; set; }
        public int TargetPid { get; set; }
        public string Publisher { get; set; }
        public string TargetDesc { get; set; }
        public string SourcePublisher { get; set; }
        public int RiskScore { get; set; }
        public string Tier { get; set; }
        public string OpKind { get; set; }
        public string FingerprintKey { get; set; }
        public bool Silent { get; set; }
        public string ModelReason { get; set; }
        public bool Signed { get; set; }
        public bool Verified { get; set; }
        public string CommandLine { get; set; }
        public string Intent { get; set; }
        public string Status { get; set; }
        public string Note { get; set; }

        public string ResultText()
        {
            if (Status == "allowed") return "已允许";
            if (Status == "denied") return "已拒绝";
            if (Status == "pending") return "待确认";
            return "未知";
        }

        public string DisplayTarget()
        {
            if (!string.IsNullOrEmpty(TargetName)) return TargetName;
            return "目标未识别";
        }
    }

    internal sealed class Config
    {
        public bool Sound { get; set; }
        public int MaxEvents { get; set; }
        public bool StartInTray { get; set; }
        public bool FollowAgents { get; set; }
        public bool UseModel { get; set; }
        public string ModelEndpoint { get; set; }
        public string ModelName { get; set; }
        public Dictionary<string, string> AppNames { get; set; }

        internal Config()
        {
            Sound = true;
            MaxEvents = 200;
            StartInTray = true;
            FollowAgents = true;
            UseModel = true;
            ModelEndpoint = "http://127.0.0.1:11434";
            ModelName = "qwen3.5:0.8b";
            AppNames = new Dictionary<string, string>();
        }

        internal static Config Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    JavaScriptSerializer ser = new JavaScriptSerializer();
                    Config c = ser.Deserialize<Config>(File.ReadAllText(path, Encoding.UTF8));
                    if (c != null)
                    {
                        if (c.AppNames == null) c.AppNames = new Dictionary<string, string>();
                        if (c.MaxEvents <= 0 || c.MaxEvents > 2000) c.MaxEvents = 200;
                        return c;
                    }
                }
            }
            catch { }

            Config d = new Config();
            d.AppNames["doubao"] = "豆包";
            d.AppNames["qwen"] = "千问";
            d.AppNames["tongyi"] = "通义千问";
            d.AppNames["deepseek"] = "DeepSeek";
            d.AppNames["workbuddy"] = "WorkBuddy";
            d.AppNames["marvis"] = "Marvis";
            d.AppNames["yuanbao"] = "腾讯元宝";
            d.AppNames["kimi"] = "Kimi";
            d.AppNames["chatglm"] = "智谱清言";
            return d;
        }

        internal void Save(string path)
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(this), new UTF8Encoding(false));
            }
            catch { }
        }

        internal string Friendly(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return "未知程序";
            string key = exeName.ToLowerInvariant();
            foreach (KeyValuePair<string, string> kv in AppNames)
            {
                if (!string.IsNullOrEmpty(kv.Key) && key.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return kv.Value;
                }
            }
            return exeName;
        }
    }

    internal static class Store
    {
        internal static string Dir { get; private set; }

        internal static string EventsFile
        {
            get { return Path.Combine(Dir, "events.json"); }
        }

        internal static string ConfigFile
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        internal static void Init()
        {
            try
            {
                string over = Environment.GetEnvironmentVariable("UACSENTINEL_DATA");
                if (!string.IsNullOrEmpty(over))
                {
                    Directory.CreateDirectory(over);
                    Dir = over;
                    return;
                }
            }
            catch { }

            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            try
            {
                Directory.CreateDirectory(baseDir);
                Dir = baseDir;
            }
            catch
            {
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UacSentinel");
                Directory.CreateDirectory(local);
                Dir = local;
            }
        }

        internal static List<ElevEvent> LoadEvents()
        {
            try
            {
                if (!File.Exists(EventsFile)) return new List<ElevEvent>();
                JavaScriptSerializer ser = new JavaScriptSerializer();
                List<ElevEvent> list = ser.Deserialize<List<ElevEvent>>(File.ReadAllText(EventsFile, Encoding.UTF8));
                if (list == null) return new List<ElevEvent>();
                foreach (ElevEvent ev in list)
                {
                    if (ev.Time.Kind == DateTimeKind.Utc) ev.Time = ev.Time.ToLocalTime();
                }
                return list.OrderByDescending(x => x.Time).ToList();
            }
            catch
            {
                return new List<ElevEvent>();
            }
        }

        internal static void SaveEvents(List<ElevEvent> events)
        {
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                File.WriteAllText(EventsFile, ser.Serialize(events), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class SignatureInfo
    {
        internal static void Inspect(string path, out string publisher, out bool signed, out bool verified)
        {
            publisher = null;
            signed = false;
            verified = false;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
                signed = true;
                X509Certificate2 c2 = new X509Certificate2(cert);
                try
                {
                    publisher = c2.GetNameInfo(X509NameType.SimpleName, false);
                }
                catch { }
                try
                {
                    using (X509Chain chain = new X509Chain())
                    {
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                        verified = chain.Build(c2);
                    }
                }
                catch { }
            }
            catch { }
        }
    }

    internal static class Intent
    {
        internal static string Infer(string path, string name, string cmd)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            string p = (path ?? string.Empty).ToLowerInvariant();

            if (n == "msiexec" || p.EndsWith(".msi")) return "安装 / 卸载软件包";
            if (n == "cmd" || n == "powershell" || n == "pwsh" || n == "wscript" || n == "cscript" ||
                n == "bash" || n == "wsl" || n == "wt") return "以管理员身份执行命令";
            if (n == "regedit" || n == "reg") return "修改系统注册表";
            if (n == "mmc" || n == "gpedit" || n == "secpol" || n == "services" || n == "compmgmt" ||
                n == "diskmgmt" || n == "devmgmt") return "打开系统管理工具";
            if (n == "sc" || n == "net" || n == "net1") return "管理系统服务或账户";
            if (n == "schtasks") return "创建或修改计划任务";
            if (n == "bcdedit") return "修改系统启动配置";
            if (n == "pnputil") return "安装驱动程序";
            if (n == "taskmgr") return "以管理员身份打开任务管理器";
            if (n == "explorer") return "以管理员身份重启资源管理器";
            if (n == "dism" || n == "sfc") return "修复或修改系统映像";
            if (p.StartsWith("c:\\windows\\")) return "修改系统组件或系统设置";
            if (n.Contains("setup") || n.Contains("install") || n.Contains("update") || n.Contains("unins"))
                return "安装或更新程序";
            if (!string.IsNullOrEmpty(cmd)) return "以管理员身份运行指定命令";
            return "以管理员身份运行此程序";
        }
    }

    internal sealed class ElevationMonitor : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly TrustStore _trust;
        internal bool UseModel;
        internal string ModelEndpoint = "http://127.0.0.1:11434";
        internal string ModelName = "qwen3.5:0.8b";
        internal event Action<ElevEvent> Reassessed;
        private readonly HashSet<int> _consentSeen = new HashSet<int>();
        private ElevEvent _pending;
        private DateTime _pendingSince = DateTime.MinValue;
        private DateTime _consentGoneAt = DateTime.MinValue;
        private ElevEvent _denied;
        private DateTime _deniedAt = DateTime.MinValue;
        private HashSet<int> _deniedBaseline;
        private string _fgName;
        private string _fgPath;
        private DateTime _fgAt = DateTime.MinValue;
        private DateTime _lastFgProbe = DateTime.MinValue;

        internal event Action<ElevEvent> Detected;
        internal event Action<ElevEvent> Resolved;

        internal ElevationMonitor()
        {
            _trust = new TrustStore(Path.Combine(Store.Dir, "trust.json"));
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 300;
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                Poll();
            }
            catch { }
        }

        private void Poll()
        {
            DateTime now = DateTime.Now;

            if ((now - _lastFgProbe).TotalMilliseconds > 700)
            {
                _lastFgProbe = now;
                ProbeForeground();
            }

            HashSet<int> consent = new HashSet<int>();
            try
            {
                foreach (Process p in Process.GetProcessesByName("consent"))
                {
                    try { consent.Add(p.Id); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            if (consent.Count > 0 && _pending == null)
            {
                Begin(consent, now);
                _consentSeen.Clear();
                foreach (int id in consent) _consentSeen.Add(id);
                return;
            }

            if (_pending == null)
            {
                CheckLateTarget(now);
                return;
            }

            TrackTarget(consent);
            TrackResolution(consent, now);
        }

        private void ProbeForeground()
        {
            try
            {
                IntPtr h = Native.GetForegroundWindow();
                if (h == IntPtr.Zero) return;
                uint pid;
                Native.GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return;
                if ((int)pid == Process.GetCurrentProcess().Id) return;
                string path = Native.GetProcessPath((int)pid);
                if (string.IsNullOrEmpty(path)) return;
                string name = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name)) return;
                _fgName = name;
                _fgPath = path;
                _fgAt = DateTime.Now;
            }
            catch { }
        }

        private void Begin(HashSet<int> consent, DateTime now)
        {
            ElevEvent ev = new ElevEvent();
            ev.Id = Guid.NewGuid().ToString("N");
            ev.Time = now;
            ev.Status = "pending";
            ev.ConsentPid = consent.First();
            if (_fgAt != DateTime.MinValue && (now - _fgAt).TotalSeconds < 180)
            {
                ev.SourceName = _fgName;
                ev.SourcePath = _fgPath;
            }
            else
            {
                ev.SourceName = "未知来源";
            }

            if (!string.IsNullOrEmpty(ev.SourcePath))
            {
                string sp;
                bool ss;
                bool sv;
                SignatureInfo.Inspect(ev.SourcePath, out sp, out ss, out sv);
                ev.SourcePublisher = sp;
            }
            ev.Tier = RiskEngine.TierMedium;
            ev.Silent = _trust != null && _trust.PredictSilent(ev.SourcePath);

            _pending = ev;
            _pendingSince = now;
            _consentGoneAt = DateTime.MinValue;
            _denied = null;
            _deniedBaseline = null;

            Action<ElevEvent> handler = Detected;
            if (handler != null) handler(ev);
        }

        private void TrackTarget(HashSet<int> consent)
        {
            if (_pending.TargetPath != null) return;

            int self = Process.GetCurrentProcess().Id;
            foreach (int id in SnapshotIds())
            {
                if (id == self || consent.Contains(id)) continue;
                if (Native.IsElevated(id) != true) continue;
                if (TryFill(_pending, id))
                {
                    RiskEngine.Assess(_pending);
                    return;
                }
            }
        }

        private void CheckLateTarget(DateTime now)
        {
            if (_denied == null) return;
            if ((now - _deniedAt).TotalSeconds > 6)
            {
                _denied = null;
                _deniedBaseline = null;
                return;
            }

            int self = Process.GetCurrentProcess().Id;
            foreach (int id in SnapshotIds())
            {
                if (id == self) continue;
                if (_deniedBaseline != null && _deniedBaseline.Contains(id)) continue;
                if (Native.IsElevated(id) != true) continue;
                if (!TryFill(_denied, id)) continue;
                RiskEngine.Assess(_denied);

                _denied.Status = "allowed";
                _denied.Note = "UAC 弹窗中已允许（稍后确认）";
                ElevEvent ev = _denied;
                _denied = null;
                _deniedBaseline = null;
                Action<ElevEvent> handler = Resolved;
                if (handler != null) handler(ev);
                return;
            }
        }

        private bool TryFill(ElevEvent ev, int id)
        {
            string path = Native.GetProcessPath(id);
            string name = string.IsNullOrEmpty(path) ? null : Path.GetFileNameWithoutExtension(path);
            if (name == null)
            {
                try { name = Process.GetProcessById(id).ProcessName; }
                catch { name = null; }
            }
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            if (lower == "conhost" || lower == "werfault" || lower == "dllhost" ||
                lower == "runtimebroker" || lower == "smartscreen" || lower == "consent") return false;

            ev.TargetPid = id;
            ev.TargetName = name;
            ev.TargetPath = path;
            ev.TargetDesc = Describe(path);
            ev.CommandLine = Wmi.GetCommandLine(id);
            string publisher;
            bool signed;
            bool verified;
            SignatureInfo.Inspect(path, out publisher, out signed, out verified);
            ev.Publisher = publisher;
            ev.Signed = signed;
            ev.Verified = verified;
            ev.Intent = Intent.Infer(path, name, ev.CommandLine);
            return true;
        }

        private static HashSet<int> SnapshotIds()
        {
            HashSet<int> ids = new HashSet<int>();
            try
            {
                foreach (Process p in Process.GetProcesses())
                {
                    try { ids.Add(p.Id); }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }
            return ids;
        }

        private static string Describe(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrEmpty(vi.FileDescription)) return vi.FileDescription.Trim();
                }
            }
            catch { }
            return null;
        }

        private void TrackResolution(HashSet<int> consent, DateTime now)
        {
            bool up = consent.Count > 0;
            if (up)
            {
                _consentGoneAt = DateTime.MinValue;
                return;
            }
            if (_consentGoneAt == DateTime.MinValue) _consentGoneAt = now;
            double gone = (now - _consentGoneAt).TotalSeconds;

            if (_pending.Status == "pending")
            {
                if (_pending.TargetPath != null && gone >= 0.2)
                {
                    Resolve("allowed", "UAC 弹窗中已允许");
                }
                else if (gone >= 1.8)
                {
                    Resolve("denied", "弹窗被取消或拒绝");
                }
            }

            if ((now - _pendingSince).TotalSeconds > 240)
            {
                Resolve("unknown", "超时未识别");
            }
        }

        private static int Rank(string tier)
        {
            if (tier == RiskEngine.TierHigh) return 2;
            if (tier == RiskEngine.TierMedium) return 1;
            return 0;
        }

        private void Resolve(string status, string note)
        {
            if (_pending == null) return;
            _pending.Status = status;
            _pending.Note = note;

            if (!string.IsNullOrEmpty(_pending.TargetPath))
            {
                RiskEngine.Assess(_pending);
            }

            if (status == "allowed" && !string.IsNullOrEmpty(_pending.FingerprintKey))
            {
                _trust.RecordAllowed(_pending.FingerprintKey, _pending.OpKind, _pending.TargetPath, _pending.SourcePath);
                if (_trust.IsTrusted(_pending.FingerprintKey, _pending.OpKind))
                {
                    _pending.Silent = true;
                    _pending.Tier = RiskEngine.TierLow;
                }
            }
            else if (status == "denied" && !string.IsNullOrEmpty(_pending.FingerprintKey))
            {
                _trust.RecordDenied(_pending.FingerprintKey);
            }

            ElevEvent ev = _pending;
            _pending = null;

            if (UseModel && !string.IsNullOrEmpty(ev.TargetPath))
            {
                ElevEvent snapshot = ev;
                System.Threading.Tasks.Task.Run(delegate
                {
                    ModelVerdict v = LocalModel.Classify(snapshot, ModelEndpoint, ModelName, 2600);
                    if (v == null) return;
                    snapshot.ModelReason = v.Reason;
                    if (v.Tier == RiskEngine.TierHigh && Rank(snapshot.Tier) < 2)
                    {
                        snapshot.Tier = v.Tier;
                        snapshot.Silent = false;
                    }
                    Action<ElevEvent> h = Reassessed;
                    if (h != null) h(snapshot);
                });
            }

            if (status == "denied")
            {
                _denied = ev;
                _deniedAt = DateTime.Now;
                _deniedBaseline = SnapshotIds();
            }

            Action<ElevEvent> handler = Resolved;
            if (handler != null) handler(ev);
        }

        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }

    internal static class Wmi
    {
        internal static string GetCommandLine(int pid)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + pid))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        object v = obj["CommandLine"];
                        if (v != null) return v.ToString();
                    }
                }
            }
            catch { }
            return null;
        }
    }

    internal static class Glyphs
    {
        internal static Icon Shield(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                float w = size;
                float h = size;
                PointF[] pts = new PointF[]
                {
                    new PointF(0.50f * w, 0.05f * h),
                    new PointF(0.92f * w, 0.22f * h),
                    new PointF(0.88f * w, 0.63f * h),
                    new PointF(0.50f * w, 0.95f * h),
                    new PointF(0.12f * w, 0.63f * h),
                    new PointF(0.08f * w, 0.22f * h)
                };
                using (SolidBrush b = new SolidBrush(Theme.Green)) g.FillPolygon(b, pts);
                using (SolidBrush b2 = new SolidBrush(Theme.Bg))
                {
                    g.FillEllipse(b2, 0.43f * w, 0.36f * h, 0.14f * w, 0.14f * h);
                }
            }
            IntPtr handle = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(handle))
                {
                    return (Icon)tmp.Clone();
                }
            }
            finally
            {
                Native.DestroyIcon(handle);
                bmp.Dispose();
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Config _cfg;
        private readonly List<ElevEvent> _events;
        private readonly ElevationMonitor _monitor;
        private readonly System.Windows.Forms.Timer _flash;
        private readonly System.Windows.Forms.Timer _hideAlert;
        private readonly System.Windows.Forms.Timer _agentTimer;
        private bool _agentsRunning;
        private DateTime _agentsGoneAt = DateTime.MinValue;
        private bool _manualHide;
        private readonly Dictionary<string, Image> _icons = new Dictionary<string, Image>();
        private NotifyIcon _tray;
        private float _s = 1f;
        private int _rowsTop;
        private int _scroll;
        private int _hoverRow = -1;
        private bool _hoverClose;
        private bool _hoverMin;
        private bool _hoverHistory;
        private bool _hoverAuto;
        private int _flashPhase;
        private ElevEvent _alert;
        private DateTime _alertExpire = DateTime.MinValue;
        private readonly List<Rectangle> _rowRects = new List<Rectangle>();
        private Rectangle _closeRect;
        private Rectangle _minRect;
        private Rectangle _histRect;
        private Rectangle _autoRect;
        private Rectangle _bannerRect;
        private Rectangle _ackRect;
        private Rectangle _listRect;
        private Rectangle _projRect;
        private int _runningProjects;
        private int _projTick;

        internal MainForm()
        {
            Store.Init();
            _cfg = Config.Load(Store.ConfigFile);
            _events = Store.LoadEvents();
            _cfg.Save(Store.ConfigFile);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            _flash = new System.Windows.Forms.Timer();
            _flash.Interval = 420;
            _flash.Tick += new EventHandler(FlashTick);

            _hideAlert = new System.Windows.Forms.Timer();
            _hideAlert.Interval = 8200;
            _hideAlert.Tick += new EventHandler(HideAlertTick);

            _agentTimer = new System.Windows.Forms.Timer();
            _agentTimer.Interval = 3000;
            _agentTimer.Tick += new EventHandler(AgentTick);
            _agentTimer.Start();

            _monitor = new ElevationMonitor();
            _monitor.UseModel = _cfg.UseModel;
            _monitor.ModelEndpoint = _cfg.ModelEndpoint;
            _monitor.ModelName = _cfg.ModelName;
            _monitor.Detected += OnDetected;
            _monitor.Resolved += OnResolved;
            _monitor.Reassessed += OnReassessed;

            BuildTray();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (Graphics g = CreateGraphics())
            {
                _s = g.DpiX / 96f;
            }
            ApplyLayout();
            Screen screen = Screen.FromPoint(Cursor.Position);
            Rectangle wa = screen.WorkingArea;
            Location = new Point(wa.Right - Width - Sc(18), wa.Bottom - Height - Sc(18));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_monitor != null) _monitor.Dispose();
            base.OnFormClosing(e);
        }

        private int Sc(int v)
        {
            return (int)Math.Round(v * _s);
        }

        private bool ShowBanner
        {
            get { return _alert != null; }
        }

        private void ApplyLayout()
        {
            int width = Sc(364);
            int headerH = Sc(56);
            int bannerH = ShowBanner ? Sc(72) : Sc(6);
            int visible = Math.Min(Math.Max(_events.Count, 0), 4);
            int listH;
            if (_events.Count == 0)
            {
                listH = Sc(104);
            }
            else
            {
                listH = visible * Sc(58) + (visible - 1) * Sc(6);
            }
            int footerH = Sc(44);
            int height = headerH + bannerH + Sc(6) + listH + footerH;

            _rowsTop = headerH + bannerH + Sc(6);
            Size = new Size(width, height);

            using (GraphicsPath p = Draw.Round(new Rectangle(0, 0, Width, Height), Sc(14)))
            {
                Region = new Region(p);
            }

            _closeRect = new Rectangle(Width - Sc(36), Sc(10), Sc(24), Sc(24));
            _minRect = new Rectangle(Width - Sc(64), Sc(10), Sc(24), Sc(24));
            _histRect = new Rectangle(Sc(12), Height - Sc(38), Sc(120), Sc(26));
            _projRect = new Rectangle(Sc(136), Height - Sc(38), Sc(58), Sc(26));
            _autoRect = new Rectangle(Width - Sc(100), Height - Sc(36), Sc(88), Sc(22));
            _bannerRect = ShowBanner ? new Rectangle(Sc(12), Sc(56), Width - Sc(24), Sc(66)) : Rectangle.Empty;
            _ackRect = ShowBanner ? new Rectangle(Width - Sc(88), Sc(64), Sc(64), Sc(24)) : Rectangle.Empty;
            _listRect = new Rectangle(Sc(12), _rowsTop, Width - Sc(24), listH);

            _rowRects.Clear();
            int y = _rowsTop;
            for (int i = 0; i < visible; i++)
            {
                _rowRects.Add(new Rectangle(Sc(12), y, Width - Sc(24), Sc(58)));
                y += Sc(58) + Sc(6);
            }

            Invalidate();
        }

        internal void LoadDemoData()
        {
            _events.Clear();

            ElevEvent pending = new ElevEvent();
            pending.Id = "demo-pending";
            pending.Time = DateTime.Now.AddSeconds(-6);
            pending.Status = "pending";
            pending.SourceName = "Doubao";
            pending.SourcePath = @"C:\Windows\System32\notepad.exe";
            pending.Intent = "以管理员身份执行命令";
            pending.Tier = "high";
            pending.RiskScore = 78;

            ElevEvent allowed = Models.DemoEvent();
            allowed.Time = DateTime.Now.AddMinutes(-4);
            allowed.Tier = "low";
            allowed.Silent = true;
            allowed.RiskScore = 12;

            ElevEvent denied = Models.DemoList()[1];
            denied.Time = DateTime.Now.AddMinutes(-26);
            denied.Tier = "medium";
            denied.RiskScore = 52;

            _events.Add(pending);
            _events.Add(allowed);
            _events.Add(denied);
            _alert = pending;
            _alertExpire = DateTime.MaxValue;
            ApplyLayout();
        }

        private void FlashTick(object sender, EventArgs e)
        {
            _flashPhase = _flashPhase == 0 ? 1 : 0;
            Invalidate();
        }

        private void HideAlertTick(object sender, EventArgs e)
        {
            _hideAlert.Stop();
            if (_alert != null && _alert.Status != "pending")
            {
                _alert = null;
                ApplyLayout();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_cfg.StartInTray && !AgentWatch.AnyRunning())
            {
                Hide();
                try
                {
                    _tray.ShowBalloonTip(2500, "提权哨兵已在托盘运行",
                        "双击绿盾图标打开面板；检测到提权时会自动提醒", ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void AgentTick(object sender, EventArgs e)
        {
            _projTick++;
            if (_projTick % 4 == 1)
            {
                int n = 0;
                foreach (ProjectItem it in ProjectScanner.Scan())
                {
                    if (it.Status == "running") n++;
                }
                if (n != _runningProjects)
                {
                    _runningProjects = n;
                    Invalidate();
                }
            }

            if (!_cfg.FollowAgents) return;
            bool running = AgentWatch.AnyRunning();
            if (running)
            {
                if (!_agentsRunning)
                {
                    _agentsRunning = true;
                    _manualHide = false;
                    _agentsGoneAt = DateTime.MinValue;
                    if (!Visible) ShowPanel();
                }
                return;
            }

            if (_agentsRunning)
            {
                _agentsRunning = false;
                _agentsGoneAt = DateTime.Now;
            }
            if (Visible && !_manualHide && _agentsGoneAt != DateTime.MinValue &&
                (DateTime.Now - _agentsGoneAt).TotalSeconds > 180)
            {
                Hide();
            }
        }

        private void OnDetected(ElevEvent ev)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<ElevEvent>(OnDetected), ev);
                return;
            }
            _events.Insert(0, ev);
            Trim();
            _scroll = 0;
            Store.SaveEvents(_events);

            bool quiet = ev.Tier == RiskEngine.TierLow && ev.Silent;
            if (!quiet)
            {
                _alert = ev;
                _alertExpire = DateTime.MaxValue;
                _hideAlert.Stop();
                if (ev.Tier == RiskEngine.TierHigh) PlayAlert();
                else PlaySoftAlert();
                _flash.Start();
                if (!Visible) Show();
            }
            ApplyLayout();
        }

        private void OnResolved(ElevEvent ev)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<ElevEvent>(OnResolved), ev);
                return;
            }
            Store.SaveEvents(_events);

            bool quiet = ev.Tier == RiskEngine.TierLow && ev.Silent;
            if (quiet)
            {
                if (_alert != null && _alert.Id == ev.Id) _alert = null;
                _flash.Stop();
                ApplyLayout();
                return;
            }

            // 之前在检测阶段没提醒（或预判静默失败），这里补一次
            if (_alert == null || _alert.Id != ev.Id)
            {
                _alert = ev;
                if (ev.Tier == RiskEngine.TierHigh) PlayAlert();
                else PlaySoftAlert();
                if (!Visible) Show();
            }
            _alertExpire = DateTime.Now.AddSeconds(ev.Tier == RiskEngine.TierHigh ? 30 : 8);
            _hideAlert.Stop();
            _hideAlert.Start();
            _flash.Stop();
            ApplyLayout();
        }

        private void OnReassessed(ElevEvent ev)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<ElevEvent>(OnReassessed), ev);
                return;
            }
            Store.SaveEvents(_events);
            if (ev.Tier == RiskEngine.TierHigh && (_alert == null || _alert.Id != ev.Id))
            {
                _alert = ev;
                PlayAlert();
                if (!Visible) ShowPanel();
                _alertExpire = DateTime.Now.AddSeconds(30);
                _hideAlert.Stop();
                _hideAlert.Start();
            }
            Invalidate();
        }

        private void Trim()
        {
            int max = Math.Max(20, _cfg.MaxEvents);
            while (_events.Count > max)
            {
                _events.RemoveAt(_events.Count - 1);
            }
        }

        private void PlayAlert()
        {
            if (!_cfg.Sound) return;
            try
            {
                string wav = Path.Combine(Store.Dir, "alert.wav");
                if (!File.Exists(wav)) WriteAlertWav(wav);
                SoundPlayer player = new SoundPlayer(wav);
                player.Play();
            }
            catch { }
        }

        private void PlaySoftAlert()
        {
            if (!_cfg.Sound) return;
            try
            {
                string wav = Path.Combine(Store.Dir, "soft.wav");
                if (!File.Exists(wav)) WriteSoftWav(wav);
                SoundPlayer player = new SoundPlayer(wav);
                player.Play();
            }
            catch { }
        }

        private static void WriteSoftWav(string path)
        {
            int rate = 44100;
            List<short> samples = new List<short>();
            AppendTone(samples, rate, 660, 0.09, 0.16);
            WriteWave(path, rate, samples);
        }

        private static void WriteAlertWav(string path)
        {
            int rate = 44100;
            List<short> samples = new List<short>();
            AppendTone(samples, rate, 880, 0.13, 0.35);
            AppendSilence(samples, rate, 0.06);
            AppendTone(samples, rate, 1174, 0.13, 0.35);
            AppendSilence(samples, rate, 0.05);
            AppendTone(samples, rate, 880, 0.16, 0.30);
            WriteWave(path, rate, samples);
        }

        private static void WriteWave(string path, int rate, List<short> samples)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                int dataLen = samples.Count * 2;
                w.Write(new char[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + dataLen);
                w.Write(new char[] { 'W', 'A', 'V', 'E' });
                w.Write(new char[] { 'f', 'm', 't', ' ' });
                w.Write(16);
                w.Write((short)1);
                w.Write((short)1);
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(new char[] { 'd', 'a', 't', 'a' });
                w.Write(dataLen);
                foreach (short s in samples) w.Write(s);
            }
        }

        private static void AppendTone(List<short> samples, int rate, double freq, double seconds, double amp)
        {
            int n = (int)(rate * seconds);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / rate;
                double env = Math.Min(1.0, Math.Min(i / (rate * 0.012), (n - i) / (rate * 0.03)));
                double v = Math.Sin(2 * Math.PI * freq * t) * amp * env;
                samples.Add((short)(v * 32767));
            }
        }

        private static void AppendSilence(List<short> samples, int rate, double seconds)
        {
            int n = (int)(rate * seconds);
            for (int i = 0; i < n; i++) samples.Add(0);
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = Glyphs.Shield(32);
            _tray.Text = "提权哨兵";
            _tray.Visible = true;
            _tray.DoubleClick += delegate { ToggleVisible(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.BackColor = Theme.Card;
            menu.ForeColor = Theme.Text;
            menu.ShowImageMargin = false;

            ToolStripMenuItem show = new ToolStripMenuItem("显示面板");
            show.Click += delegate { ShowPanel(); };
            menu.Items.Add(show);

            ToolStripMenuItem preview = new ToolStripMenuItem("预览提醒效果");
            preview.Click += delegate { PreviewAlert(); };
            menu.Items.Add(preview);

            ToolStripMenuItem projects = new ToolStripMenuItem("项目状态");
            projects.Click += delegate { ShowProjects(); };
            menu.Items.Add(projects);

            ToolStripMenuItem sound = new ToolStripMenuItem("提醒声音");
            sound.CheckOnClick = true;
            sound.Checked = _cfg.Sound;
            sound.CheckedChanged += delegate
            {
                _cfg.Sound = sound.Checked;
                _cfg.Save(Store.ConfigFile);
            };
            menu.Items.Add(sound);

            ToolStripMenuItem auto = new ToolStripMenuItem("开机自启");
            auto.CheckOnClick = true;
            auto.Checked = Autostart.IsEnabled();
            auto.CheckedChanged += delegate
            {
                Autostart.Set(auto.Checked);
                Invalidate();
            };
            menu.Items.Add(auto);

            ToolStripMenuItem folder = new ToolStripMenuItem("打开数据文件夹");
            folder.Click += delegate
            {
                try { Process.Start("explorer.exe", "\"" + Store.Dir + "\""); }
                catch { }
            };
            menu.Items.Add(folder);

            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate
            {
                _tray.Visible = false;
                Application.Exit();
            };
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
        }

        private void ToggleVisible()
        {
            if (Visible && Bounds.Contains(Cursor.Position))
            {
                Hide();
            }
            else
            {
                ShowPanel();
            }
        }

        private void ShowPanel()
        {
            Show();
            BringToFront();
            Activate();
        }

        private ProjectsForm _projectsForm;

        private void ShowProjects()
        {
            if (_projectsForm != null && !_projectsForm.IsDisposed)
            {
                _projectsForm.Activate();
                return;
            }
            _projectsForm = new ProjectsForm();
            _projectsForm.Show();
        }

        private void PreviewAlert()
        {
            ElevEvent ev = new ElevEvent();
            ev.Id = "preview";
            ev.Time = DateTime.Now;
            ev.Status = "pending";
            ev.SourceName = "豆包";
            ev.SourcePath = "C:\\Users\\Public\\Doubao.exe";
            ev.Intent = "以管理员身份执行命令";
            _alert = ev;
            _alertExpire = DateTime.Now.AddSeconds(6);
            _hideAlert.Stop();
            _hideAlert.Start();
            ShowPanel();
            PlayAlert();
            ApplyLayout();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Draw.Round(body, Sc(14)))
            using (SolidBrush b = new SolidBrush(Theme.Bg))
            using (Pen p = new Pen(Theme.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }

            PaintHeader(g);
            if (ShowBanner) PaintBanner(g);
            PaintRows(g);
            PaintFooter(g);
        }

        private void PaintHeader(Graphics g)
        {
            bool pending = _alert != null && _alert.Status == "pending";
            Color dot = pending ? Theme.Red : Theme.Green;
            int cx = Sc(21);
            int cy = Sc(24);
            int r = Sc(4);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(pending ? 60 : 40, dot)))
            {
                g.FillEllipse(glow, cx - r * 2, cy - r * 2, r * 4, r * 4);
            }
            using (SolidBrush b = new SolidBrush(dot))
            {
                g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            }

            Gfx.Text(g, "提权哨兵", Theme.Title, Theme.Text, Sc(34), Sc(12));

            int today = 0;
            int silentToday = 0;
            foreach (ElevEvent ev in _events)
            {
                if (ev.Time.Date == DateTime.Now.Date)
                {
                    today++;
                    if (ev.Silent) silentToday++;
                }
            }
            string sub = pending
                ? "有提权请求正在等待处理"
                : "监控中 \u00B7 今日 " + today + " 次" + (silentToday > 0 ? "（静默 " + silentToday + "）" : "")
                  + (_runningProjects > 0 ? " \u00B7 " + _runningProjects + " 个项目在跑" : "");
            Gfx.Text(g, sub, Theme.Small, pending ? Theme.Red : Theme.Sub, Sc(34), Sc(34));

            PaintGlyphButton(g, _minRect, false, _hoverMin);
            PaintGlyphButton(g, _closeRect, true, _hoverClose);
        }

        private void PaintGlyphButton(Graphics g, Rectangle rect, bool close, bool hover)
        {
            if (hover)
            {
                Draw.Fill(g, rect, Sc(7), Theme.CardHover);
            }
            Color c = hover ? Theme.Text : Theme.Sub;
            using (Pen pen = new Pen(c, Math.Max(1.2f, _s * 1.2f)))
            {
                int cx = rect.X + rect.Width / 2;
                int cy = rect.Y + rect.Height / 2;
                int d = Sc(5);
                if (close)
                {
                    g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d);
                    g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d);
                }
                else
                {
                    g.DrawLine(pen, cx - d, cy, cx + d, cy);
                }
            }
        }

        private void PaintBanner(Graphics g)
        {
            bool pending = _alert.Status == "pending";
            bool allowed = _alert.Status == "allowed";
            bool high = _alert.Tier == RiskEngine.TierHigh;
            bool low = _alert.Tier == RiskEngine.TierLow;

            Color accent = high ? Theme.Red : (low ? Theme.Green : Theme.Amber);
            Color bg = high ? Theme.DangerBg : (low ? Color.FromArgb(24, 40, 32) : Color.FromArgb(46, 38, 22));
            Color border = high ? Theme.DangerBorder : Theme.Border;

            if (high && _flashPhase == 1)
            {
                bg = Color.FromArgb(58, 32, 37);
            }

            Draw.Fill(g, _bannerRect, Sc(10), bg);
            Draw.Stroke(g, _bannerRect, Sc(10), border);
            using (SolidBrush b = new SolidBrush(accent))
            {
                g.FillRectangle(b, _bannerRect.X + Sc(1), _bannerRect.Y + Sc(10), Sc(3), _bannerRect.Height - Sc(20));
            }

            int x = _bannerRect.X + Sc(16);
            int y = _bannerRect.Y + Sc(10);
            int textW = _bannerRect.Width - Sc(104);
            string title;
            string line2;
            string line3;
            if (pending)
            {
                title = high ? "高风险提权请求" : "检测到提权请求";
                line2 = _cfg.Friendly(_alert.SourceName) + " 正在请求管理员权限";
                line3 = "请看屏幕中央的 Windows 弹窗，点\u201C是\u201D前先确认程序名";
            }
            else if (allowed)
            {
                title = "提权已允许";
                line2 = _alert.DisplayTarget() + " 已获得管理员权限";
                line3 = RiskEngine.TierText(_alert.Tier) + " \u00B7 " + (_alert.Intent ?? "未识别");
            }
            else
            {
                title = "提权已取消";
                line2 = _cfg.Friendly(_alert.SourceName) + " 的这次提权没有通过";
                line3 = string.IsNullOrEmpty(_alert.Note) ? "弹窗被取消或拒绝" : _alert.Note;
            }

            Gfx.Text(g, title, Theme.BodyBold, accent, x, y);
            Gfx.Text(g, Gfx.Fit(g, line2, Theme.Small, textW), Theme.Small, Theme.Text, x, y + Sc(20));
            Gfx.Text(g, Gfx.Fit(g, line3, Theme.Tiny, textW), Theme.Tiny, Theme.Dim, x, y + Sc(38));

            if (_ackRect.Width > 0)
            {
                Draw.Fill(g, _ackRect, Sc(8), Color.FromArgb(40, accent));
                int tw = Gfx.Width(g, "知道了", Theme.Tiny);
                Gfx.Text(g, "知道了", Theme.Tiny, accent, _ackRect.X + (_ackRect.Width - tw) / 2, _ackRect.Y + Sc(4));
            }
        }

        private void PaintRows(Graphics g)
        {
            if (_events.Count == 0)
            {
                string l1 = "还没有检测到提权请求";
                string l2 = "任何程序弹出 UAC 提权窗口时，这里会亮起提醒";
                int y = _listRect.Y + _listRect.Height / 2 - Sc(18);
                int w1 = Gfx.Width(g, l1, Theme.Body);
                Gfx.Text(g, l1, Theme.Body, Theme.Sub, _listRect.X + (_listRect.Width - w1) / 2, y);
                int w2 = Gfx.Width(g, l2, Theme.Tiny);
                Gfx.Text(g, l2, Theme.Tiny, Theme.Dim, _listRect.X + (_listRect.Width - w2) / 2, y + Sc(24));
                return;
            }

            for (int i = 0; i < _rowRects.Count; i++)
            {
                int index = _scroll + i;
                if (index >= _events.Count) break;
                ElevEvent ev = _events[index];
                Rectangle r = _rowRects[i];
                bool hover = i == _hoverRow;
                Draw.Fill(g, r, Sc(10), hover ? Theme.CardHover : Theme.Card);

                Color tierColor = ev.Tier == RiskEngine.TierHigh ? Theme.Red
                    : (ev.Tier == RiskEngine.TierLow ? Theme.Green : Theme.Amber);
                using (SolidBrush tb = new SolidBrush(tierColor))
                {
                    g.FillEllipse(tb, r.X + Sc(4), r.Y + Sc(25), Sc(5), Sc(9));
                }

                Rectangle iconRect = new Rectangle(r.X + Sc(12), r.Y + Sc(13), Sc(32), Sc(32));
                PaintAppIcon(g, iconRect, ev.SourcePath);

                int chipW;
                string status = ev.ResultText();
                Color statusColor = StatusColor(ev.Status);
                chipW = Gfx.Width(g, status, Theme.Tiny) + Sc(14);
                Rectangle chip = new Rectangle(r.Right - Sc(12) - chipW, r.Y + Sc(12), chipW, Sc(18));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(36, statusColor)))
                {
                    using (GraphicsPath p = Draw.Round(chip, Sc(9)))
                    {
                        g.FillPath(b, p);
                    }
                }
                int tw = Gfx.Width(g, status, Theme.Tiny);
                Gfx.Text(g, status, Theme.Tiny, statusColor, chip.X + (chip.Width - tw) / 2, chip.Y + Sc(2));

                int textX = r.X + Sc(52);
                int textW = chip.X - textX - Sc(8);
                Gfx.Text(g, Gfx.Fit(g, _cfg.Friendly(ev.SourceName), Theme.Body, textW), Theme.Body, Theme.Text, textX, r.Y + Sc(11));

                string line2 = ev.DisplayTarget();
                if (ev.Silent) line2 += "  \u00B7 已静默";
                line2 += "  \u00B7 " + ev.Time.ToString("HH:mm:ss");
                Gfx.Text(g, Gfx.Fit(g, line2, Theme.Tiny, textW), Theme.Tiny,
                    ev.TargetName == null ? Theme.Dim : Theme.Sub, textX, r.Y + Sc(31));
            }
        }

        private static Color StatusColor(string status)
        {
            if (status == "pending") return Theme.Amber;
            if (status == "allowed") return Theme.Green;
            return Theme.Sub;
        }

        private void PaintAppIcon(Graphics g, Rectangle rect, string path)
        {
            Image img = GetIcon(path);
            if (img != null)
            {
                g.DrawImage(img, rect);
                return;
            }
            Color c = Color.FromArgb(70, 80, 96);
            Draw.Fill(g, rect, Sc(8), c);
            string letter = "?";
            if (!string.IsNullOrEmpty(path))
            {
                string n = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrEmpty(n)) letter = n.Substring(0, 1).ToUpperInvariant();
            }
            int w = Gfx.Width(g, letter, Theme.BodyBold);
            Gfx.Text(g, letter, Theme.BodyBold, Theme.Text, rect.X + (rect.Width - w) / 2, rect.Y + Sc(7));
        }

        private Image GetIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_icons.ContainsKey(path)) return _icons[path];
            Image img = null;
            try
            {
                if (File.Exists(path))
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(path))
                    {
                        if (ico != null) img = ico.ToBitmap();
                    }
                }
            }
            catch { img = null; }
            _icons[path] = img;
            return img;
        }

        private void PaintFooter(Graphics g)
        {
            using (Pen pen = new Pen(Color.FromArgb(34, 40, 50)))
            {
                g.DrawLine(pen, Sc(14), Height - Sc(42), Width - Sc(14), Height - Sc(42));
            }

            string more = _events.Count > 4 ? "查看全部 " + _events.Count + " 条记录" : "查看全部记录";
            Color moreColor = _hoverHistory ? Theme.Text : Theme.Blue;
            Gfx.Text(g, more, Theme.Small, moreColor, _histRect.X, _histRect.Y + Sc(4));

            string proj = _runningProjects > 0 ? "项目 " + _runningProjects : "项目";
            Gfx.Text(g, proj, Theme.Small, _runningProjects > 0 ? Theme.Green : Theme.Blue, _projRect.X, _projRect.Y + Sc(4));

            int box = Sc(13);
            Rectangle boxRect = new Rectangle(_autoRect.X, _autoRect.Y + Sc(4), box, box);
            bool on = Autostart.IsEnabled();
            Draw.Fill(g, boxRect, Sc(4), on ? Theme.Green : Color.FromArgb(40, 47, 58));
            Draw.Stroke(g, boxRect, Sc(4), on ? Theme.Green : Theme.Border);
            if (on)
            {
                using (Pen pen = new Pen(Theme.Bg, Math.Max(1.6f, _s * 1.6f)))
                {
                    g.DrawLine(pen, boxRect.X + Sc(3), boxRect.Y + Sc(7), boxRect.X + Sc(5), boxRect.Y + Sc(9));
                    g.DrawLine(pen, boxRect.X + Sc(5), boxRect.Y + Sc(9), boxRect.X + Sc(9), boxRect.Y + Sc(4));
                }
            }
            Gfx.Text(g, "开机自启", Theme.Tiny, _hoverAuto ? Theme.Text : Theme.Sub,
                boxRect.Right + Sc(6), _autoRect.Y + Sc(3));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = -1;
            for (int i = 0; i < _rowRects.Count; i++)
            {
                if (_rowRects[i].Contains(e.Location)) { row = i; break; }
            }
            bool hc = _closeRect.Contains(e.Location);
            bool hm = _minRect.Contains(e.Location);
            bool hh = _histRect.Contains(e.Location);
            bool ha = _autoRect.Contains(e.Location);
            if (row != _hoverRow || hc != _hoverClose || hm != _hoverMin || hh != _hoverHistory || ha != _hoverAuto)
            {
                _hoverRow = row;
                _hoverClose = hc;
                _hoverMin = hm;
                _hoverHistory = hh;
                _hoverAuto = ha;
                Cursor = (row >= 0 || hc || hm || hh || ha) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverRow = -1;
            _hoverClose = false;
            _hoverMin = false;
            _hoverHistory = false;
            _hoverAuto = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (_closeRect.Contains(e.Location) || _minRect.Contains(e.Location))
            {
                _manualHide = true;
                Hide();
                return;
            }
            if (ShowBanner && _ackRect.Contains(e.Location))
            {
                _alert = null;
                _hideAlert.Stop();
                ApplyLayout();
                return;
            }
            if (_histRect.Contains(e.Location))
            {
                using (HistoryForm form = new HistoryForm(_events))
                {
                    form.ShowDialog(this);
                }
                return;
            }
            if (_projRect.Contains(e.Location))
            {
                ShowProjects();
                return;
            }
            if (_autoRect.Contains(e.Location))
            {
                Autostart.Set(!Autostart.IsEnabled());
                Invalidate();
                return;
            }
            if (ShowBanner && _bannerRect.Contains(e.Location) && _alert != null && _alert.Status != "preview")
            {
                using (DetailForm form = new DetailForm(_alert, _cfg))
                {
                    form.ShowDialog(this);
                }
                return;
            }
            for (int i = 0; i < _rowRects.Count; i++)
            {
                if (_rowRects[i].Contains(e.Location))
                {
                    int index = _scroll + i;
                    if (index < _events.Count)
                    {
                        using (DetailForm form = new DetailForm(_events[index], _cfg))
                        {
                            form.ShowDialog(this);
                        }
                    }
                    return;
                }
            }
            if (e.Y < Sc(56))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int maxScroll = Math.Max(0, _events.Count - 4);
            if (maxScroll <= 0) return;
            int next = _scroll - Math.Sign(e.Delta);
            if (next < 0) next = 0;
            if (next > maxScroll) next = maxScroll;
            if (next != _scroll)
            {
                _scroll = next;
                ApplyLayout();
            }
        }
    }

    internal sealed class DetailForm : Form
    {
        private readonly ElevEvent _ev;
        private readonly Config _cfg;
        private float _s = 1f;

        internal DetailForm(ElevEvent ev, Config cfg)
        {
            _ev = ev;
            _cfg = cfg;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            using (Graphics g = CreateGraphics())
            {
                _s = g.DpiX / 96f;
            }
            BuildUi();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;
                return cp;
            }
        }

        private int Sc(int v)
        {
            return (int)Math.Round(v * _s);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (GraphicsPath path = Draw.Round(new Rectangle(0, 0, Width, Height), Sc(12)))
            {
                Region = new Region(path);
            }
        }

        private void BuildUi()
        {
            int width = Sc(452);

            List<string[]> fields = new List<string[]>();
            fields.Add(new string[] { "时间", _ev.Time.ToString("yyyy-MM-dd HH:mm:ss"), "0" });
            fields.Add(new string[] { "来源程序", _cfg.Friendly(_ev.SourceName), "0" });
            if (!string.IsNullOrEmpty(_ev.SourcePath)) fields.Add(new string[] { "来源路径", _ev.SourcePath, "0" });
            string targetLabel = _ev.TargetName == null ? "未识别（弹窗被取消时无法获取）" : _ev.TargetName;
            if (_ev.TargetName != null && !string.IsNullOrEmpty(_ev.TargetDesc)) targetLabel += "（" + _ev.TargetDesc + "）";
            fields.Add(new string[] { "目标程序", targetLabel, "0" });
            if (!string.IsNullOrEmpty(_ev.TargetPath)) fields.Add(new string[] { "目标路径", _ev.TargetPath, "0" });

            string signer;
            if (!_ev.Signed) signer = "未签名";
            else if (_ev.Verified) signer = (_ev.Publisher ?? "未知") + "  \u00B7  签名已验证";
            else signer = (_ev.Publisher ?? "未知") + "  \u00B7  签名未通过验证";
            fields.Add(new string[] { "发布者", signer, "0" });

            fields.Add(new string[] { "可能意图", string.IsNullOrEmpty(_ev.Intent) ? "未识别" : _ev.Intent, "0" });
            fields.Add(new string[] { "风险研判", RiskEngine.TierText(_ev.Tier) + "（" + _ev.RiskScore + " 分）" + (_ev.Silent ? "  \u00B7  已静默" : ""), "0" });
            if (!string.IsNullOrEmpty(_ev.ModelReason))
            {
                fields.Add(new string[] { "模型研判", _ev.ModelReason, "0" });
            }

            if (!string.IsNullOrEmpty(_ev.CommandLine))
            {
                fields.Add(new string[] { "命令行", _ev.CommandLine, "1" });
            }

            string result = _ev.ResultText();
            if (!string.IsNullOrEmpty(_ev.Note)) result += "  \u00B7  " + _ev.Note;
            fields.Add(new string[] { "结果", result, "0" });

            int bodyH = 0;
            foreach (string[] f in fields) bodyH += f[2] == "1" ? Sc(78) : Sc(40);
            int height = Sc(50) + bodyH + Sc(14) + Sc(38);
            Size = new Size(width, height);

            Label title = new Label();
            title.Text = "提权详情";
            title.Font = Theme.Title;
            title.ForeColor = Theme.Text;
            title.BackColor = Color.Transparent;
            title.SetBounds(Sc(18), Sc(12), Sc(200), Sc(24));
            Controls.Add(title);

            Button close = MakeButton("关闭", Width - Sc(78), Sc(10), Sc(60), Sc(26), false);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            int y = Sc(50);
            foreach (string[] f in fields)
            {
                y = AddField(f[0], f[1], y, f[2] == "1");
            }

            Button copy = MakeButton("复制详情", Sc(18), Height - Sc(42), Sc(92), Sc(30), false);
            copy.Click += delegate { CopyDetails(); };
            Controls.Add(copy);

            if (!string.IsNullOrEmpty(_ev.TargetPath))
            {
                Button open = MakeButton("打开文件位置", Sc(118), Height - Sc(42), Sc(106), Sc(30), false);
                open.Click += delegate
                {
                    try { Process.Start("explorer.exe", "/select,\"" + _ev.TargetPath + "\""); }
                    catch { }
                };
                Controls.Add(open);
            }
        }

        private int AddField(string label, string value, int y, bool mono)
        {
            Label l = new Label();
            l.Text = label;
            l.Font = Theme.Tiny;
            l.ForeColor = Theme.Dim;
            l.BackColor = Color.Transparent;
            l.SetBounds(Sc(18), y, Sc(90), Sc(16));
            Controls.Add(l);

            if (mono)
            {
                TextBox box = new TextBox();
                box.Text = value;
                box.Font = Theme.Mono;
                box.Multiline = true;
                box.ReadOnly = true;
                box.BorderStyle = BorderStyle.None;
                box.BackColor = Theme.Card;
                box.ForeColor = Theme.Text;
                box.ScrollBars = ScrollBars.Vertical;
                box.SetBounds(Sc(16), y + Sc(16), Width - Sc(32), Sc(56));
                Controls.Add(box);
                return y + Sc(78);
            }

            Label v = new Label();
            v.Text = value;
            v.Font = Theme.Body;
            v.ForeColor = Theme.Text;
            v.BackColor = Color.Transparent;
            v.AutoEllipsis = true;
            v.SetBounds(Sc(18), y + Sc(15), Width - Sc(36), Sc(20));
            ToolTip tip = new ToolTip();
            tip.SetToolTip(v, value);
            Controls.Add(v);
            return y + Sc(40);
        }

        private Button MakeButton(string text, int x, int y, int w, int h, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = primary ? Theme.Green : Theme.Border;
            b.BackColor = primary ? Theme.Green : Theme.Card;
            b.ForeColor = primary ? Theme.Bg : Theme.Text;
            b.Font = Theme.Small;
            b.SetBounds(x, y, w, h);
            b.Cursor = Cursors.Hand;
            return b;
        }

        private void CopyDetails()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("时间: " + _ev.Time.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("来源: " + _cfg.Friendly(_ev.SourceName) + " (" + _ev.SourcePath + ")");
            sb.AppendLine("目标: " + (_ev.TargetName ?? "未识别") + " (" + (_ev.TargetPath ?? "-") + ")");
            sb.AppendLine("发布者: " + (_ev.Publisher ?? "未签名") + (_ev.Verified ? " [已验证]" : ""));
            sb.AppendLine("意图: " + (_ev.Intent ?? "-"));
            if (!string.IsNullOrEmpty(_ev.CommandLine)) sb.AppendLine("命令行: " + _ev.CommandLine);
            sb.AppendLine("结果: " + _ev.ResultText() + " " + (_ev.Note ?? ""));
            try { Clipboard.SetText(sb.ToString()); }
            catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Draw.Round(body, Sc(12)))
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawPath(p, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < Sc(44))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly List<ElevEvent> _events;
        private readonly Config _cfg;
        private float _s = 1f;

        internal HistoryForm(List<ElevEvent> events)
        {
            _events = events;
            _cfg = Config.Load(Store.ConfigFile);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            using (Graphics g = CreateGraphics())
            {
                _s = g.DpiX / 96f;
            }
            BuildUi();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80;
                return cp;
            }
        }

        private int Sc(int v)
        {
            return (int)Math.Round(v * _s);
        }

        private void BuildUi()
        {
            Size = new Size(Sc(600), Sc(430));

            Label title = new Label();
            title.Text = "提权记录  ·  共 " + _events.Count + " 条";
            title.Font = Theme.Title;
            title.ForeColor = Theme.Text;
            title.BackColor = Color.Transparent;
            title.SetBounds(Sc(18), Sc(12), Sc(320), Sc(24));
            Controls.Add(title);

            Button close = new Button();
            close.Text = "关闭";
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 1;
            close.FlatAppearance.BorderColor = Theme.Border;
            close.BackColor = Theme.Card;
            close.ForeColor = Theme.Text;
            close.Font = Theme.Small;
            close.SetBounds(Width - Sc(78), Sc(10), Sc(60), Sc(26));
            close.Click += delegate { Close(); };
            Controls.Add(close);

            ListView list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.OwnerDraw = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.BackColor = Theme.Bg;
            list.ForeColor = Theme.Text;
            list.BorderStyle = BorderStyle.None;
            list.Font = Theme.Small;
            list.SetBounds(Sc(14), Sc(48), Width - Sc(28), Height - Sc(64));
            list.Columns.Add("时间", Sc(116));
            list.Columns.Add("来源", Sc(84));
            list.Columns.Add("目标", Sc(130));
            list.Columns.Add("结果", Sc(58));
            list.Columns.Add("意图", Sc(150));

            foreach (ElevEvent ev in _events)
            {
                ListViewItem item = new ListViewItem(ev.Time.ToString("MM-dd HH:mm:ss"));
                item.SubItems.Add(_cfg.Friendly(ev.SourceName));
                item.SubItems.Add(ev.DisplayTarget());
                item.SubItems.Add(ev.ResultText());
                item.SubItems.Add(ev.Intent ?? "-");
                item.Tag = ev;
                list.Items.Add(item);
            }

            list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Tiny, new Rectangle(e.Bounds.X + Sc(8), e.Bounds.Y, e.Bounds.Width, e.Bounds.Height), Theme.Sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            };
            list.DrawItem += delegate(object s, DrawListViewItemEventArgs e)
            {
                Color bg = e.Item.Selected ? Theme.CardHover : Theme.Bg;
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            };
            list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                Color color = Theme.Text;
                if (e.ColumnIndex == 3)
                {
                    ElevEvent ev = e.Item.Tag as ElevEvent;
                    color = ev == null ? Theme.Sub : StatusColor(ev.Status);
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Small,
                    new Rectangle(e.Bounds.X + Sc(8), e.Bounds.Y, e.Bounds.Width - Sc(10), e.Bounds.Height), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            list.MouseDoubleClick += delegate
            {
                if (list.SelectedItems.Count == 0) return;
                ElevEvent ev = list.SelectedItems[0].Tag as ElevEvent;
                if (ev == null) return;
                using (DetailForm form = new DetailForm(ev, Config.Load(Store.ConfigFile)))
                {
                    form.ShowDialog(this);
                }
            };

            Controls.Add(list);
        }

        private static Color StatusColor(string status)
        {
            if (status == "pending") return Theme.Amber;
            if (status == "allowed") return Theme.Green;
            return Theme.Sub;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle body = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Draw.Round(body, Sc(12)))
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawPath(p, path);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < Sc(44))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
        }
    }

    internal static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "UacSentinel";

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    return k.GetValue(Name) != null;
                }
            }
            catch { return false; }
        }

        internal static void Set(bool on)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
