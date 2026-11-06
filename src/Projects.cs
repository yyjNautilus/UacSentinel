using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace UacSentinel
{
    internal sealed class ProjectItem
    {
        internal string Title;
        internal string Cwd;
        internal string Status;
        internal int Turns;
        internal DateTime LastActivity;
        internal long OpenTurn;
    }

    /// <summary>读取 DSH 的会话缓存，判断每个会话是“在跑 / 已完成 / 中断”。</summary>
    internal static class ProjectScanner
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        internal static string CacheDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".dsh", "storages", "session_projcache", "sessions");
            }
        }

        internal static List<ProjectItem> Scan()
        {
            List<ProjectItem> list = new List<ProjectItem>();
            try
            {
                if (Directory.Exists(CacheDir))
                {
                    foreach (string file in Directory.GetFiles(CacheDir, "*.json"))
                    {
                        try
                        {
                            ProjectItem item = Parse(file);
                            if (item != null) list.Add(item);
                        }
                        catch { }
                    }
                }
            }
            catch { }

            list.AddRange(ScanExternal());

            return list
                .OrderByDescending(x => x.Status == "running")
                .ThenByDescending(x => x.LastActivity)
                .ToList();
        }

        internal static string AgentStatusDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentStatus");
            }
        }

        /// <summary>读取第三方 AI 助手的状态：%LOCALAPPDATA%\AgentStatus\*.json，或 *.port 指向的本地 HTTP 接口。</summary>
        private static List<ProjectItem> ScanExternal()
        {
            List<ProjectItem> list = new List<ProjectItem>();
            try
            {
                if (!Directory.Exists(AgentStatusDir)) return list;

                foreach (string file in Directory.GetFiles(AgentStatusDir, "*.json"))
                {
                    try
                    {
                        string body = File.ReadAllText(file, Encoding.UTF8);
                        list.AddRange(ParseExternal(body, Path.GetFileNameWithoutExtension(file), File.GetLastWriteTime(file)));
                    }
                    catch { }
                }

                foreach (string portFile in Directory.GetFiles(AgentStatusDir, "*.port"))
                {
                    try
                    {
                        string portText = File.ReadAllText(portFile, Encoding.UTF8).Trim();
                        int port;
                        if (!int.TryParse(portText, out port)) continue;
                        string body = HttpGet("http://127.0.0.1:" + port + "/agent-status");
                        if (string.IsNullOrEmpty(body)) continue;
                        list.AddRange(ParseExternal(body, Path.GetFileNameWithoutExtension(portFile), File.GetLastWriteTime(portFile)));
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        private static List<ProjectItem> ParseExternal(string json, string fallbackAgent, DateTime fallbackTime)
        {
            List<ProjectItem> list = new List<ProjectItem>();
            Dictionary<string, object> root = null;
            try { root = Json.Deserialize<Dictionary<string, object>>(json); }
            catch { return list; }
            if (root == null) return list;

            string agent = root.ContainsKey("agent") ? Convert.ToString(root["agent"]) : fallbackAgent;
            if (string.IsNullOrEmpty(agent)) agent = "外部助手";

            System.Collections.IEnumerable seq = null;
            if (root.ContainsKey("projects")) seq = root["projects"] as System.Collections.IEnumerable;
            if (seq == null) return list;

            foreach (object o in seq)
            {
                Dictionary<string, object> p = AsDict(o);
                if (p == null) continue;
                string title = p.ContainsKey("title") ? Convert.ToString(p["title"]) : "未命名";
                string status = p.ContainsKey("status") ? Normalize(Convert.ToString(p["status"])) : "idle";
                string cwd = p.ContainsKey("cwd") ? Convert.ToString(p["cwd"]) : null;

                ProjectItem item = new ProjectItem();
                item.Title = agent + " \u00B7 " + title;
                item.Cwd = cwd;
                item.Status = status;
                item.Turns = p.ContainsKey("turns") ? SafeInt(p["turns"]) : 0;
                item.LastActivity = p.ContainsKey("updatedAt") ? SafeDate(p["updatedAt"]) : fallbackTime;
                list.Add(item);
            }
            return list;
        }

        private static string HttpGet(string url)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 1500;
                req.ReadWriteTimeout = 1500;
                req.Proxy = null;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (Stream s = resp.GetResponseStream())
                using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
            catch { return null; }
        }

        private static string Normalize(string status)
        {
            string s = (status ?? string.Empty).Trim().ToLowerInvariant();
            if (s == "running" || s == "working" || s == "busy") return "running";
            if (s == "done" || s == "completed" || s == "success" || s == "finished") return "done";
            if (s == "error" || s == "failed" || s == "aborted") return "interrupted";
            return "idle";
        }

        private static int SafeInt(object o)
        {
            try { return Convert.ToInt32(o); }
            catch { return 0; }
        }

        private static DateTime SafeDate(object o)
        {
            try
            {
                DateTime dt;
                if (DateTime.TryParse(Convert.ToString(o), out dt)) return dt;
            }
            catch { }
            return DateTime.Now;
        }

        private static ProjectItem Parse(string file)
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
            Dictionary<string, object> root = Json.Deserialize<Dictionary<string, object>>(text);
            if (root == null) return null;

            Dictionary<string, object> record = GetDict(root, "record");
            if (record == null) return null;
            Dictionary<string, object> rows = GetDict(record, "rows");
            if (rows == null) return null;

            ProjectItem item = new ProjectItem();
            item.Title = GetString(rows, "title", "val");
            if (string.IsNullOrEmpty(item.Title)) item.Title = "(未命名会话)";

            Dictionary<string, object> identity = GetDict(record, "identity");
            item.Cwd = identity != null ? GetString(identity, "cwd") : null;

            object[] turns = GetArray(rows, "turnOutline", "val", "turns");
            string lastResponse = null;
            if (turns != null && turns.Length > 0)
            {
                item.Turns = turns.Length;
                Dictionary<string, object> last = AsDict(turns[turns.Length - 1]);
                if (last != null) lastResponse = GetString(last, "response");
            }

            object openObj = GetDeep(rows, "turnBoundary", "val", "openTurnStartSeq");
            item.OpenTurn = openObj == null ? 0 : Convert.ToInt64(openObj);

            item.LastActivity = File.GetLastWriteTime(file);
            DateTime now = DateTime.Now;
            bool empty = string.IsNullOrWhiteSpace(lastResponse);

            if (empty && (now - item.LastActivity).TotalMinutes < 10) item.Status = "running";
            else if (empty) item.Status = "interrupted";
            else item.Status = "done";
            return item;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return AsDict(d[key]);
        }

        private static Dictionary<string, object> AsDict(object o)
        {
            Dictionary<string, object> dict = o as Dictionary<string, object>;
            if (dict != null) return dict;
            System.Collections.IDictionary id = o as System.Collections.IDictionary;
            if (id == null) return null;
            Dictionary<string, object> result = new Dictionary<string, object>();
            foreach (System.Collections.DictionaryEntry e in id)
            {
                result[Convert.ToString(e.Key)] = e.Value;
            }
            return result;
        }

        private static object GetDeep(Dictionary<string, object> d, params string[] path)
        {
            object cur = d;
            foreach (string p in path)
            {
                Dictionary<string, object> dd = AsDict(cur);
                if (dd == null || !dd.ContainsKey(p)) return null;
                cur = dd[p];
            }
            return cur;
        }

        private static object[] GetArray(Dictionary<string, object> d, params string[] path)
        {
            object o = GetDeep(d, path);
            if (o == null) return null;
            object[] arr = o as object[];
            if (arr != null) return arr;
            System.Collections.IEnumerable seq = o as System.Collections.IEnumerable;
            if (seq == null) return null;
            List<object> list = new List<object>();
            foreach (object x in seq) list.Add(x);
            return list.ToArray();
        }

        private static string GetString(Dictionary<string, object> d, params string[] path)
        {
            object o = GetDeep(d, path);
            return o == null ? null : Convert.ToString(o);
        }

        internal static string StatusText(string status)
        {
            if (status == "running") return "运行中";
            if (status == "done") return "已完成";
            if (status == "interrupted") return "已中断";
            return "未知";
        }

        internal static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return "刚刚";
            if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " 分钟前";
            if (d.TotalHours < 24) return (int)d.TotalHours + " 小时前";
            if (d.TotalDays < 2) return "昨天";
            return (int)d.TotalDays + " 天前";
        }
    }

    internal sealed class ProjectsForm : Form
    {
        private readonly List<ProjectItem> _items = new List<ProjectItem>();
        private readonly System.Windows.Forms.Timer _timer;
        private readonly Label _header;
        private readonly ListView _list;
        private readonly Button _close;
        private float _s = 1f;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        internal ProjectsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.None;
            using (Graphics g = CreateGraphics())
            {
                _s = g.DpiX / 96f;
            }
            Rectangle wa = Screen.PrimaryScreen != null
                ? Screen.PrimaryScreen.WorkingArea
                : new Rectangle(0, 0, Sc(1280), Sc(800));
            Size = new Size(Math.Max(Sc(640), Math.Min(Sc(980), wa.Width - Sc(48))),
                            Math.Max(Sc(400), Math.Min(Sc(700), wa.Height - Sc(48))));
            MinimumSize = new Size(Sc(620), Sc(400));

            _header = new Label();
            _header.Font = Theme.Title;
            _header.ForeColor = Theme.Text;
            _header.BackColor = Color.Transparent;
            _header.SetBounds(Sc(18), Sc(12), Sc(480), Sc(24));
            Controls.Add(_header);

            Button close = new Button();
            close.Text = "关闭";
            close.FlatStyle = FlatStyle.Flat;
            close.FlatAppearance.BorderSize = 1;
            close.FlatAppearance.BorderColor = Theme.Border;
            close.BackColor = Theme.Card;
            close.ForeColor = Theme.Text;
            close.Font = Theme.Small;
            close.SetBounds(Width - Sc(80), Sc(10), Sc(62), Sc(26));
            close.Click += delegate { Close(); };
            _close = close;
            Controls.Add(close);

            _list = new ListView();
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.OwnerDraw = true;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.BackColor = Theme.Bg;
            _list.ForeColor = Theme.Text;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Theme.Small;
            _list.SetBounds(Sc(14), Sc(48), Width - Sc(28), Height - Sc(64));
            _list.Columns.Add("状态", Sc(62));
            _list.Columns.Add("项目 / 会话", Sc(206));
            _list.Columns.Add("轮次", Sc(52));
            _list.Columns.Add("最后活动", Sc(80));
            _list.Columns.Add("工作目录", Sc(208));
            _list.DrawColumnHeader += delegate(object s, DrawListViewColumnHeaderEventArgs e)
            {
                using (SolidBrush b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (Pen p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Theme.Tiny,
                    new Rectangle(e.Bounds.X + Sc(8), e.Bounds.Y, e.Bounds.Width, e.Bounds.Height), Theme.Sub,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            };
            // 背景与所有列文字集中在 DrawItem 一次画完：悬停/局部重绘时只要重绘该行就一定连文字一起画，
            // 不会再出现“只刷背景把字盖掉”的情况（DrawSubItem 置空）。
            _list.DrawItem += delegate(object s, DrawListViewItemEventArgs e)
            {
                Color bg = e.Item.Selected ? Theme.CardHover : Theme.Bg;
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

                ProjectItem it = e.Item.Tag as ProjectItem;
                for (int i = 0; i < e.Item.SubItems.Count; i++)
                {
                    ListViewItem.ListViewSubItem sub = e.Item.SubItems[i];
                    Rectangle cell = sub.Bounds;
                    if (cell.Width <= 0) continue;
                    Color color = Theme.Text;
                    if (i == 0 && it != null)
                    {
                        if (it.Status == "running") color = Theme.Green;
                        else if (it.Status == "interrupted") color = Theme.Red;
                        else color = Theme.Sub;
                    }
                    TextRenderer.DrawText(e.Graphics, sub.Text, Theme.Small,
                        new Rectangle(cell.X + Sc(8), cell.Y, Math.Max(0, cell.Width - Sc(10)), cell.Height), color,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                }
            };
            _list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                e.DrawDefault = false;
            };
            Controls.Add(_list);

            LayoutChildren();
            LayoutColumns();

            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 5000;
            _timer.Tick += delegate { Reload(); };
            _timer.Start();
            Reload();
        }

        private int Sc(int v)
        {
            return (int)Math.Round(v * _s);
        }

        private void ApplyRoundRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Draw.Round(new Rectangle(0, 0, Width, Height), Sc(12)))
            {
                Region = new Region(path);
            }
        }

        /// <summary>按当前窗口尺寸重新摆放标题、关闭按钮和列表。</summary>
        private void LayoutChildren()
        {
            if (_header == null || _list == null || _close == null) return;
            int cw = ClientSize.Width > 0 ? ClientSize.Width : Width;
            int ch = ClientSize.Height > 0 ? ClientSize.Height : Height;
            _header.SetBounds(Sc(18), Sc(12), Math.Max(Sc(120), cw - Sc(112)), Sc(24));
            _close.SetBounds(cw - Sc(80), Sc(10), Sc(62), Sc(26));
            _list.SetBounds(Sc(14), Sc(48), Math.Max(Sc(120), cw - Sc(28)), Math.Max(Sc(80), ch - Sc(64)));
        }

        /// <summary>按列表真实客户区宽度重分列宽，保证 5 列都在窗口内，项目/会话与工作目录自适应。</summary>
        private void LayoutColumns()
        {
            if (_list == null || _list.Columns.Count < 5) return;
            int avail = _list.ClientSize.Width;
            if (avail <= 0) avail = Math.Max(Sc(320), (ClientSize.Width > 0 ? ClientSize.Width : Width) - Sc(30));
            avail -= Sc(18); // 预留滚动条，避免最后一列被挡
            int fixedW = Sc(66) + Sc(56) + Sc(86);
            int rest = avail - fixedW;
            if (rest < Sc(240)) rest = Math.Max(Sc(200), avail - Sc(140));
            int projW = (int)Math.Round(rest * 0.52);
            int cwdW = rest - projW;
            _list.Columns[0].Width = Sc(66);
            _list.Columns[1].Width = projW;
            _list.Columns[2].Width = Sc(56);
            _list.Columns[3].Width = Sc(86);
            _list.Columns[4].Width = cwdW;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundRegion();
            LayoutChildren();
            LayoutColumns();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    int lp = m.LParam.ToInt32();
                    Point sp = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                    Point cp = PointToClient(sp);
                    int grip = Sc(7);
                    bool left = cp.X <= grip;
                    bool right = cp.X >= ClientSize.Width - grip;
                    bool top = cp.Y <= grip;
                    bool bottom = cp.Y >= ClientSize.Height - grip;
                    if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                    else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                    else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                    else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundRegion();
            LayoutChildren();
            LayoutColumns();
        }

        private void Reload()
        {
            _items.Clear();
            _items.AddRange(ProjectScanner.Scan());

            int running = _items.Count(x => x.Status == "running");
            _header.Text = "项目状态  ·  " + running + " 个在跑 / 共 " + _items.Count + " 个";

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (ProjectItem it in _items)
            {
                ListViewItem row = new ListViewItem(ProjectScanner.StatusText(it.Status));
                row.SubItems.Add(it.Title);
                row.SubItems.Add(it.Turns > 0 ? ("第 " + it.Turns + " 轮") : "-");
                row.SubItems.Add(ProjectScanner.Ago(it.LastActivity));
                row.SubItems.Add(it.Cwd ?? "-");
                row.Tag = it;
                _list.Items.Add(row);
            }
            _list.EndUpdate();
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

            // 右下角缩放提示（可拖此处缩放窗口）
            using (Pen gp = new Pen(Theme.Border))
            {
                int bx = Width - Sc(6);
                int by = Height - Sc(6);
                for (int i = 1; i <= 3; i++)
                {
                    int off = i * Sc(5);
                    g.DrawLine(gp, bx - off, by, bx, by - off);
                }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
