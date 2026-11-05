using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
                if (!Directory.Exists(CacheDir)) return list;
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
            catch { }

            return list
                .OrderByDescending(x => x.Status == "running")
                .ThenByDescending(x => x.LastActivity)
                .ToList();
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
        private float _s = 1f;

        internal ProjectsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
            using (Graphics g = CreateGraphics())
            {
                _s = g.DpiX / 96f;
            }
            Size = new Size(Sc(640), Sc(440));

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
            _list.DrawItem += delegate(object s, DrawListViewItemEventArgs e)
            {
                Color bg = e.Item.Selected ? Theme.CardHover : Theme.Bg;
                using (SolidBrush b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            };
            _list.DrawSubItem += delegate(object s, DrawListViewSubItemEventArgs e)
            {
                Color color = Theme.Text;
                if (e.ColumnIndex == 0)
                {
                    ProjectItem it = e.Item.Tag as ProjectItem;
                    if (it != null)
                    {
                        if (it.Status == "running") color = Theme.Green;
                        else if (it.Status == "interrupted") color = Theme.Red;
                        else color = Theme.Sub;
                    }
                }
                TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Theme.Small,
                    new Rectangle(e.Bounds.X + Sc(8), e.Bounds.Y, e.Bounds.Width - Sc(10), e.Bounds.Height), color,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            Controls.Add(_list);

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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            using (GraphicsPath path = Draw.Round(new Rectangle(0, 0, Width, Height), Sc(12)))
            {
                Region = new Region(path);
            }
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
