using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RemoteBridge
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayContext());
        }
    }

    /// <summary>托盘常驻 + 状态网站 + 网页遥控</summary>
    internal class TrayContext : ApplicationContext
    {
        private NotifyIcon _tray;
        private HttpServer _server;
        private UiMainForm _form;
        private string _pin;

        public TrayContext()
        {
            _pin = LoadOrCreatePin();
            _server = new HttpServer(8899, _pin);
            _server.Start();

            _tray = new NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "远程桥 · 网页遥控已开启";
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主窗口", null, (s, e) => ShowInfo());
            menu.Items.Add("打开控制台", null, (s, e) => OpenWeb("remote"));
            menu.Items.Add("打开状态网站", null, (s, e) => OpenWeb(""));
            menu.Items.Add("复制内网地址", null, (s, e) => CopyAddr());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApp());
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowInfo();

            string ip = IpHelper.FirstInternalIp();
            string pinText = string.IsNullOrEmpty(_pin) ? "无密码（同一网络直接进）" : _pin;
            _tray.ShowBalloonTip(6000, "远程桥已启动 · 网页遥控已开",
                "控制台: http://" + ip + ":8899/remote\n访问密码(PIN): " + pinText + "\n手机/天猫精灵浏览器打开就能控制电脑",
                ToolTipIcon.Info);

            _form = new UiMainForm(this);
            _form.Show();   // 启动即显示主窗口（网易UU风格）
            _form.Activate();
        }

        public HttpServer Server { get { return _server; } }
        public string Pin { get { return _pin; } }

        /// <summary>设置访问密码：留空=无密码；立即生效并保存</summary>
        public void SetPin(string pin)
        {
            pin = pin == null ? "" : pin.Trim();
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote_pin.txt");
                File.WriteAllText(f, string.IsNullOrEmpty(pin) ? "0" : pin, Encoding.UTF8);
            }
            catch { }
            _pin = pin;
            _server.SetPin(pin);
            if (_form != null) _form.RefreshData();
        }

        /// <summary>是否已开启开机自启动（计划任务 RemoteBridge）</summary>
        public bool IsAutoStart()
        {
            try
            {
                using (var p = new Process())
                {
                    p.StartInfo.FileName = "schtasks";
                    p.StartInfo.Arguments = "/query /tn RemoteBridge";
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.Start();
                    p.WaitForExit(3000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>切换开机自启动：开启=登录自动以最高权限运行；关闭=取消</summary>
        public void ToggleAutoStart()
        {
            try
            {
                string exe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteBridge.exe");
                if (IsAutoStart())
                {
                    Process.Start(new ProcessStartInfo("schtasks", "/delete /tn RemoteBridge /f")
                    { UseShellExecute = false, CreateNoWindow = true });
                }
                else
                {
                    // 单层引号：schtasks /tr "C:\...\RemoteBridge.exe"（与命令行直接创建一致）
                    Process.Start(new ProcessStartInfo("schtasks",
                        "/create /tn RemoteBridge /tr \"" + exe + "\" /sc onlogon /rl highest /f")
                    { UseShellExecute = false, CreateNoWindow = true });
                }
            }
            catch { }
            if (_form != null) _form.RefreshData();
        }

        private string LoadOrCreatePin()
        {
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remote_pin.txt");
                if (File.Exists(f))
                {
                    string s = File.ReadAllText(f).Trim();
                    if (s == "0" || s.Length == 0) return "";   // 无密码模式
                    if (s.Length >= 4) return s;
                }
                var rnd = new Random();
                string pin = rnd.Next(100000, 999999).ToString();
                File.WriteAllText(f, pin, Encoding.UTF8);
                return pin;
            }
            catch { return "123456"; }
        }

        public void ShowInfo()
        {
            if (_form == null) _form = new UiMainForm(this);
            _form.RefreshData();
            if (!_form.Visible) _form.Show();
            _form.Activate();
        }

        public void OpenWeb(string page)
        {
            string ip = IpHelper.FirstInternalIp();
            if (string.IsNullOrEmpty(ip)) return;
            try
            {
                string url = "http://" + ip + ":8899/";
                if (!string.IsNullOrEmpty(page)) url = "http://" + ip + ":8899/" + page;
                System.Diagnostics.Process.Start(url);
            }
            catch { }
        }

        public void CopyAddr()
        {
            string ip = IpHelper.FirstInternalIp();
            if (string.IsNullOrEmpty(ip)) return;
            try
            {
                Clipboard.SetText("http://" + ip + ":8899/remote");
                _tray.ShowBalloonTip(1500, "已复制", "控制台地址已复制到剪贴板", ToolTipIcon.Info);
            }
            catch { }
        }

        public void CopyText(string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                Clipboard.SetText(text);
                _tray.ShowBalloonTip(1500, "已复制", "内容已复制到剪贴板", ToolTipIcon.Info);
            }
            catch { }
        }

        public void ExitApp()
        {
            if (_form != null) _form.AllowClose();
            _server.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            Application.Exit();
        }

        internal static Icon MakeIcon()
        {
            try
            {
                Icon ex = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (ex != null) return ex;
            }
            catch { }
            IntPtr h = IntPtr.Zero;
            using (var bmp = new Bitmap(16, 16))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    using (var br = new SolidBrush(Color.FromArgb(0, 120, 212)))
                        g.FillEllipse(br, 1, 1, 14, 14);
                    using (var br2 = new SolidBrush(Color.White))
                        g.FillEllipse(br2, 5, 5, 6, 6);
                }
                h = bmp.GetHicon();
            }
            // FromHandle 不拥有句柄：Clone 后必须 DestroyIcon 释放原句柄，否则每次启动泄漏一个 HICON
            try
            {
                using (var ic = Icon.FromHandle(h))
                    return (Icon)ic.Clone();
            }
            finally
            {
                if (h != IntPtr.Zero) Native.DestroyIcon(h);
            }
        }
    }

    /// <summary>主窗口（网易UU电脑版风格：浅色极简）</summary>
    internal class UiMainForm : Form
    {
        private readonly TrayContext _ctx;
        private string _ip = "", _ex = "", _cur = "0", _today = "0", _total = "0", _pinText = "", _autoText = "", _recent = "";
        private bool _allowClose;
        private readonly Rectangle _btnMain = new Rectangle(40, 470, 560, 54);
        private readonly Rectangle _btnSite = new Rectangle(40, 544, 272, 44);
        private readonly Rectangle _btnCopy = new Rectangle(328, 544, 272, 44);
        private readonly Rectangle _btnIpCopy = new Rectangle(548, 140, 56, 30);
        private readonly Rectangle _btnPin = new Rectangle(548, 236, 56, 30);
        private readonly Rectangle _btnAuto = new Rectangle(548, 424, 56, 30);

        // ===== 缓存 GDI 对象（原每帧 new Font/SolidBrush/StringFormat 不释放，长期运行会 GDI 句柄耗尽） =====
        private readonly Font _fTitle = new Font("Microsoft YaHei UI", 22, FontStyle.Bold);
        private readonly Font _fIp = new Font("Microsoft YaHei UI", 28, FontStyle.Bold);
        private readonly Font _fNum = new Font("Microsoft YaHei UI", 24, FontStyle.Bold);
        private readonly Font _fMid = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        private readonly Font _fBtn16 = new Font("Microsoft YaHei UI", 16, FontStyle.Bold);
        private readonly Font _fBtn12 = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
        private readonly Font _fBtn12r = new Font("Microsoft YaHei UI", 12);
        private readonly Font _fBtn11 = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
        private readonly Font _fBtn10 = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
        private readonly Font _fLabel11 = new Font("Microsoft YaHei UI", 11);
        private readonly Font _fLabel10 = new Font("Microsoft YaHei UI", 10);
        private readonly SolidBrush _bOrange = new SolidBrush(Color.FromArgb(242, 80, 34));
        private readonly SolidBrush _bGreenL = new SolidBrush(Color.FromArgb(127, 186, 0));
        private readonly SolidBrush _bBlue = new SolidBrush(Color.FromArgb(0, 164, 239));
        private readonly SolidBrush _bYellow = new SolidBrush(Color.FromArgb(255, 185, 0));
        private readonly SolidBrush _bDark = new SolidBrush(Color.FromArgb(32, 33, 36));
        private readonly SolidBrush _bGray = new SolidBrush(Color.FromArgb(95, 99, 104));
        private readonly SolidBrush _bLabel = new SolidBrush(Color.FromArgb(128, 134, 139));
        private readonly SolidBrush _bLighter = new SolidBrush(Color.FromArgb(154, 160, 166));
        private readonly SolidBrush _bGreen = new SolidBrush(Color.FromArgb(24, 128, 56));
        private readonly SolidBrush _bBlueBg = new SolidBrush(Color.FromArgb(232, 240, 254));
        private readonly SolidBrush _bBtnBg = new SolidBrush(Color.FromArgb(241, 243, 244));
        private readonly SolidBrush _bBtnBlue = new SolidBrush(Color.FromArgb(26, 115, 232));
        private readonly StringFormat _sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly System.Windows.Forms.Timer _timer;

        public UiMainForm(TrayContext ctx)
        {
            _ctx = ctx;
            Text = "远程桥 · 我的电脑";
            Icon = TrayContext.MakeIcon();   // 任务栏图标 = 四色窗（与 exe 一致）
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 660);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);
            DoubleBuffered = true;

            Shown += (s, e) => RefreshData();
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 3000;
            _timer.Tick += (s, e) => RefreshData();
            _timer.Start();

            FormClosing += (s, e) =>
            {
                if (!_allowClose)
                {
                    e.Cancel = true;
                    Hide();   // 关窗口=最小化到托盘，服务不中断
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _timer.Dispose(); } catch { }
                var fonts = new[] { _fTitle, _fIp, _fNum, _fMid, _fBtn16, _fBtn12, _fBtn12r, _fBtn11, _fBtn10, _fLabel11, _fLabel10 };
                foreach (var f in fonts) try { f.Dispose(); } catch { }
                var brushes = new[] { _bOrange, _bGreenL, _bBlue, _bYellow, _bDark, _bGray, _bLabel, _bLighter, _bGreen, _bBlueBg, _bBtnBg, _bBtnBlue };
                foreach (var b in brushes) try { b.Dispose(); } catch { }
                try { _sfCenter.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }

        public void AllowClose() { _allowClose = true; Close(); }

        public void RefreshData()
        {
            _ip = IpHelper.FirstInternalIp();
            string ex = IpHelper.ExternalIpCached;
            _ex = string.IsNullOrEmpty(ex) ? "" : ex;
            StatsData st = _ctx.Server.Stats;
            _cur = st.Current.ToString();
            _today = st.Today.ToString();
            _total = st.Total.ToString();
            _pinText = string.IsNullOrEmpty(_ctx.Pin) ? "无密码" : _ctx.Pin;
            _autoText = _ctx.IsAutoStart() ? "已开启" : "未开启";
            _recent = st.RecentText;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            Point p = e.Location;
            if (_btnMain.Contains(p)) _ctx.OpenWeb("remote");
            else if (_btnSite.Contains(p)) _ctx.OpenWeb("");
            else if (_btnCopy.Contains(p)) _ctx.CopyAddr();
            else if (_btnIpCopy.Contains(p)) _ctx.CopyText("http://" + _ip + ":8899/remote");
            else if (_btnPin.Contains(p)) ShowPinDialog();
            else if (_btnAuto.Contains(p)) _ctx.ToggleAutoStart();
        }

        /// <summary>设置访问密码弹窗：留空 = 无密码</summary>
        private void ShowPinDialog()
        {
            try
            {
                var f = new Form();
                f.Text = "设置访问密码";
                f.StartPosition = FormStartPosition.CenterScreen;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false;
                f.MinimizeBox = false;
                f.ClientSize = new Size(340, 200);
                f.BackColor = Color.White;
                f.Font = new Font(Font.FontFamily, 12);
                f.ShowInTaskbar = false;
                f.TopMost = true;

                var tip = new Label();
                tip.Text = "输入 4~8 位数字密码\n留空 = 无密码（同一 WiFi 直接进）";
                tip.Location = new Point(22, 16);
                tip.AutoSize = true;
                tip.ForeColor = Color.FromArgb(128, 134, 139);
                tip.Font = new Font(Font.FontFamily, 10);
                f.Controls.Add(tip);

                var tb = new TextBox();
                tb.Location = new Point(22, 78);
                tb.Size = new Size(296, 30);
                tb.Font = new Font(Font.FontFamily, 14);
                tb.TextAlign = HorizontalAlignment.Center;
                tb.Text = _ctx.Pin;
                tb.MaxLength = 8;
                tb.KeyPress += delegate(object s, KeyPressEventArgs k)
                {
                    if (!char.IsDigit(k.KeyChar) && k.KeyChar != (char)8) k.Handled = true;
                };
                f.Controls.Add(tb);

                var ok = new Button();
                ok.Text = "保存";
                ok.Location = new Point(130, 130);
                ok.Size = new Size(96, 40);
                ok.FlatStyle = FlatStyle.Flat;
                ok.FlatAppearance.BorderColor = Color.FromArgb(26, 115, 232);
                ok.BackColor = Color.FromArgb(232, 240, 254);
                ok.ForeColor = Color.FromArgb(26, 115, 232);
                ok.Font = new Font(Font.FontFamily, 12, FontStyle.Bold);
                f.Controls.Add(ok);

                var cancel = new Button();
                cancel.Text = "取消";
                cancel.Location = new Point(232, 130);
                cancel.Size = new Size(86, 40);
                cancel.FlatStyle = FlatStyle.Flat;
                cancel.FlatAppearance.BorderColor = Color.FromArgb(241, 243, 244);
                cancel.BackColor = Color.FromArgb(241, 243, 244);
                cancel.ForeColor = Color.FromArgb(95, 99, 104);
                cancel.Font = new Font(Font.FontFamily, 12);
                f.Controls.Add(cancel);

                ok.Click += delegate(object s, EventArgs ev)
                {
                    string v = tb.Text.Trim();
                    if (v.Length > 0 && (v.Length < 4 || v.Length > 8))
                    {
                        MessageBox.Show("密码需为 4~8 位数字", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    _ctx.SetPin(v);
                    f.Close();
                };
                cancel.Click += delegate(object s, EventArgs ev) { f.Close(); };
                f.Shown += delegate(object s, EventArgs ev) { tb.Focus(); };
                f.FormClosed += delegate(object s, FormClosedEventArgs ev) { f.Dispose(); };

                f.ShowDialog(this);
            }
            catch { }
        }

        private static GraphicsPath RoundedRect(Rectangle r, int rad)
        {
            var path = new GraphicsPath();
            int d = rad * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width;

            // ============ 顶部（无渐变：Logo + 标题 + 状态点） ============
            int lx = 40, ly = 34, sz = 18, gap = 3;
            g.FillRectangle(_bOrange, lx, ly, sz, sz);
            g.FillRectangle(_bGreenL, lx + sz + gap, ly, sz, sz);
            g.FillRectangle(_bBlue, lx, ly + sz + gap, sz, sz);
            g.FillRectangle(_bYellow, lx + sz + gap, ly + sz + gap, sz, sz);
            g.DrawString("远程桥", _fTitle, _bDark, 92, 38);
            // 状态点
            g.FillEllipse(_bGreen, W - 116, 46, 8, 8);
            g.DrawString("运行中", _fLabel11, _bGray, W - 102, 44);

            // 分隔线
            using (var pen = new Pen(Color.FromArgb(238, 238, 238)))
                g.DrawLine(pen, 40, 96, W - 40, 96);

            // ============ 内网地址（唯一焦点，大字号） ============
            g.DrawString("内网地址 · 手机连同一个 WiFi 后填这个", _fLabel11, _bLabel, 40, 116);
            g.DrawString(string.IsNullOrEmpty(_ip) ? "获取中…" : _ip, _fIp, _bBtnBlue, 40, 138);
            // 复制胶囊
            var pill = _btnIpCopy;
            using (var path = RoundedRect(pill, 15))
                g.FillPath(_bBlueBg, path);
            g.DrawString("复制", _fBtn11, _bBtnBlue, pill, _sfCenter);
            g.DrawString("端口 8899 · 无需外网", _fLabel11, _bLighter, 40, 190);

            // ============ 访问密码 ============
            g.DrawString("访问密码", _fLabel11, _bLabel, 40, 242);
            bool noPin = string.IsNullOrEmpty(_ctx.Pin);
            g.DrawString(_pinText, _fMid, noPin ? _bGreen : _bDark, 96, 242);
            // 设置密码胶囊
            var pinPill = _btnPin;
            using (var path = RoundedRect(pinPill, 15))
                g.FillPath(_bBtnBg, path);
            g.DrawString("设置", _fBtn10, _bGray, pinPill, _sfCenter);

            // ============ 外网 IP ============
            g.DrawString("外网 IP", _fLabel11, _bLabel, 40, 280);
            bool hasEx = !string.IsNullOrEmpty(_ex);
            g.DrawString(hasEx ? _ex : "获取中…", _fMid, hasEx ? _bDark : _bLighter, 96, 280);

            // ============ 开机自启动 ============
            g.DrawString("开机自启动", _fLabel11, _bLabel, 40, 430);
            bool onAuto = _autoText == "已开启";
            g.DrawString(_autoText, _fMid, onAuto ? _bGreen : _bGray, 96, 430);
            var autoPill = _btnAuto;
            using (var path = RoundedRect(autoPill, 15))
                g.FillPath(_bBtnBg, path);
            g.DrawString("切换", _fBtn10, _bGray, autoPill, _sfCenter);

            // ============ 连接统计（一行三组，留白） ============
            g.DrawString(_cur, _fNum, _bBtnBlue, 40, 352);
            g.DrawString("当前连接", _fLabel11, _bLabel, 40, 392);
            g.DrawString(_today, _fNum, _bBtnBlue, 216, 352);
            g.DrawString("今日连接", _fLabel11, _bLabel, 216, 392);
            g.DrawString(_total, _fNum, _bBtnBlue, 392, 352);
            g.DrawString("累计连接", _fLabel11, _bLabel, 392, 392);

            // ============ 按钮（轻柔） ============
            DrawBtn(g, _btnMain, "进入远程控制", _bBlueBg, _bBtnBlue, _fBtn16);
            DrawBtn(g, _btnSite, "打开状态网站", _bBtnBg, _bGray, _fBtn12r);
            DrawBtn(g, _btnCopy, "复制控制台地址", _bBtnBg, _bGray, _fBtn12r);

            // ============ 底部小提示 ============
            g.DrawString("关闭窗口将最小化到托盘，遥控服务不中断", _fLabel10, _bLighter, 40, 612);
        }

        private void DrawBtn(Graphics g, Rectangle r, string text, SolidBrush bg, SolidBrush fg, Font f)
        {
            using (var path = RoundedRect(r, 14))
                g.FillPath(bg, path);
            g.DrawString(text, f, fg, r, _sfCenter);
        }
    }

    /// <summary>IP 工具</summary>
    internal static class IpHelper
    {
        private static string _external;
        private static DateTime _externalTime;

        public static string ExternalIpCached
        {
            get
            {
                if (!string.IsNullOrEmpty(_external) && (DateTime.Now - _externalTime).TotalMinutes < 10)
                    return _external;
                return null;
            }
        }

        public static string FirstInternalIp()
        {
            try
            {
                string fallback = null;
                foreach (NetworkInterface n in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus != OperationalStatus.Up) continue;
                    if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string desc = (n.Description ?? "") + (n.Name ?? "");
                    // 跳过 VMware/VirtualBox/虚拟网卡（手机连不到）
                    if (desc.IndexOf("VMware", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("VirtualBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("Hyper-V", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        desc.IndexOf("TAP-", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    foreach (UnicastIPAddressInformation u in n.GetIPProperties().UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(u.Address)) continue;
                        string s = u.Address.ToString();
                        if (s.StartsWith("169.254.")) continue;
                        // 有默认网关的物理网卡 = 真实上网网卡，优先返回
                        if (n.GetIPProperties().GatewayAddresses.Count > 0) return s;
                        if (fallback == null) fallback = s;
                    }
                }
                if (fallback != null) return fallback;
            }
            catch { }
            return "127.0.0.1";
        }

        public static void FetchExternalIpAsync()
        {
            Task.Run(() =>
            {
                // 多源轮流试：国外的被墙就换国内直连源；全部失败 30 秒后重试（while 常驻，避免递归开新 Task）
                string[] urls = new string[] {
                    "https://api.ipify.org",
                    "http://ip.42.pl/raw",
                    "https://myip.ipip.net",
                    "https://ddns.oray.com/checkip",
                    "https://4.ipw.cn",
                    "https://ifconfig.me/ip"
                };
                while (true)
                {
                    bool ok = false;
                    foreach (string u in urls)
                    {
                        try
                        {
                            var req = (HttpWebRequest)WebRequest.Create(u);
                            req.Method = "GET";
                            req.Timeout = 8000;
                            req.ReadWriteTimeout = 8000;
                            req.UserAgent = "RemoteBridge/2.0";
                            using (var resp = (HttpWebResponse)req.GetResponse())
                            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                            {
                                string s = sr.ReadToEnd();
                                Match m = Regex.Match(s ?? "", @"\b\d{1,3}(\.\d{1,3}){3}\b");
                                if (m.Success)
                                {
                                    _external = m.Value;
                                    _externalTime = DateTime.Now;
                                    ok = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    if (ok) return;
                    Thread.Sleep(30000);
                }
            });
        }
    }

    internal class StatsData
    {
        public int Current;
        public int Today;
        public long Total;
        public string RecentText;
    }

    /// <summary>内置 HTTP 服务：状态网站 + 遥控 API + 网页遥控</summary>
    internal class HttpServer
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly int _port;
        private string _pin;
        private CancellationTokenSource _cts;
        private int _active;
        private long _total;
        private readonly ConcurrentDictionary<string, DateTime> _today = new ConcurrentDictionary<string, DateTime>();
        private readonly ConcurrentDictionary<string, byte> _sessions = new ConcurrentDictionary<string, byte>();   // 已通过 PIN 校验的会话 token
        private readonly ConcurrentQueue<string> _recent = new ConcurrentQueue<string>();
        private DateTime _todayDate = DateTime.Today;
        private static readonly Random Rnd = new Random();

        public HttpServer(int port, string pin)
        {
            _port = port;
            _pin = pin;
            _listener.Prefixes.Add("http://+:" + _port + "/");
        }

        public void SetPin(string pin) { _pin = pin ?? ""; }

        public void Start()
        {
            _listener.Start();
            IpHelper.FetchExternalIpAsync();
            StartScreenCache();   // 后台持续截屏缓存最新帧
            _cts = new CancellationTokenSource();
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public void Stop()
        {
            try { if (_cts != null) _cts.Cancel(); } catch { }
            try { _listener.Stop(); } catch { }
        }

        private async Task AcceptLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }
                Handle(ctx);
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref _active);
            RollDay();
            string ip = "?";
            try { ip = ctx.Request.RemoteEndPoint != null ? (ctx.Request.RemoteEndPoint.Address != null ? ctx.Request.RemoteEndPoint.Address.ToString() : "?") : "?"; } catch { }
            _today.TryAdd(ip, DateTime.Now);
            _recent.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + ip);
            while (_recent.Count > 20) { string drop; _recent.TryDequeue(out drop); }
            Interlocked.Increment(ref _total);

            try
            {
                string path = ctx.Request.Url.AbsolutePath.ToLowerInvariant();
                ctx.Response.AddHeader("Access-Control-Allow-Origin", "*");
                ctx.Response.AddHeader("Cache-Control", "no-store");
                if (path.EndsWith("api/conn"))
                {
                    WriteJson(ctx, "{\"internalIp\":\"" + IpHelper.FirstInternalIp() +
                        "\",\"externalIp\":\"" + (IpHelper.ExternalIpCached ?? "") +
                        "\",\"current\":" + _active +
                        ",\"today\":" + _today.Count +
                        ",\"total\":" + _total + "}");
                }
                else if (path.EndsWith("api/pin"))
                {
                    // 校验 PIN 并下发会话 cookie（无密码模式直接放行）
                    string pin = ctx.Request.QueryString["pin"];
                    if (string.IsNullOrEmpty(_pin) || (!string.IsNullOrEmpty(pin) && pin == _pin))
                    {
                        string token = Md5(_pin + DateTime.Now.Ticks.ToString() + Rnd.Next(99999));
                        if (_sessions.Count > 200) _sessions.Clear();   // 防呆：token 过多时清空，用户重连即可
                        _sessions.TryAdd(token, 0);
                        ctx.Response.SetCookie(new Cookie("rb_sess", token) { HttpOnly = true });
                        WriteJson(ctx, "{\"ok\":true}");
                    }
                    else WriteJson(ctx, "{\"ok\":false}");
                }
                else if (path.EndsWith("api/check"))
                {
                    WriteJson(ctx, "{\"ok\":" + (IsAuthed(ctx) ? "true" : "false") + "}");
                }
                else if (path.EndsWith("api/screen"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else WriteScreen(ctx);
                }
                else if (path.EndsWith("api/mouse"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else { HandleMouse(ctx); WriteJson(ctx, "{\"ok\":true}"); }
                }
                else if (path.EndsWith("api/key"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else { HandleKey(ctx); WriteJson(ctx, "{\"ok\":true}"); }
                }
                else if (path.EndsWith("api/launch"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else { HandleLaunch(ctx); WriteJson(ctx, "{\"ok\":true}"); }
                }
                else if (path.EndsWith("api/files"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else HandleFiles(ctx);
                }
                else if (path.EndsWith("api/download"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else HandleDownload(ctx);
                }
                else if (path.EndsWith("api/upload"))
                {
                    if (!IsAuthed(ctx)) { ctx.Response.StatusCode = 401; WriteJson(ctx, "{\"ok\":false,\"msg\":\"auth\"}"); }
                    else { HandleUpload(ctx); WriteJson(ctx, "{\"ok\":true}"); }
                }
                else if (path.EndsWith("remote"))
                {
                    WriteHtml(ctx, BuildRemotePage());
                }
                else
                {
                    WriteHtml(ctx, BuildHomePage());
                }
                ctx.Response.Close();
            }
            catch { try { ctx.Response.Close(); } catch { } }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private bool IsAuthed(HttpListenerContext ctx)
        {
            try
            {
                if (string.IsNullOrEmpty(_pin)) return true;   // 无密码模式：直接放行
                var c = ctx.Request.Cookies["rb_sess"];
                if (c == null || string.IsNullOrEmpty(c.Value)) return false;
                return _sessions.ContainsKey(c.Value);   // 必须是校验时下发的真实会话，防止伪造 cookie 绕过密码
            }
            catch { return false; }
        }

        private static string Md5(string s)
        {
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private void WriteJson(HttpListenerContext ctx, string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            NoCache(ctx);
            ctx.Response.OutputStream.Write(b, 0, b.Length);
        }

        private static void NoCache(HttpListenerContext ctx)
        {
            try
            {
                ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                ctx.Response.Headers["Pragma"] = "no-cache";
                ctx.Response.Headers["Expires"] = "0";
            }
            catch { }
        }

        private void WriteHtml(HttpListenerContext ctx, string html)
        {
            byte[] b = Encoding.UTF8.GetBytes(html);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            NoCache(ctx);
            ctx.Response.OutputStream.Write(b, 0, b.Length);
        }

        /// <summary>后台截屏缓存：线程持续生成最新帧，/api/screen 秒回，操作后立即补帧</summary>
        private volatile byte[] _scrCache;
        private volatile int _scrW, _scrH, _scrOX, _scrOY, _scrIW, _scrIH;
        private Thread _scrThread;

        private void StartScreenCache()
        {
            if (_scrThread != null && _scrThread.IsAlive) return;
            _scrThread = new Thread(() =>
            {
                while (true)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try { _scrCache = CaptureFrame(); }
                    catch { }
                    int el = (int)sw.ElapsedMilliseconds;
                    int slp = 80 - el;   // 目标每 ~80ms 出一帧（上限受截屏耗时限制）
                    if (slp > 0) { try { Thread.Sleep(slp); } catch { } }
                }
            }) { IsBackground = true };
            _scrThread.Start();
        }

        /// <summary>截屏并压缩 JPEG（宽1080）；CopyFromScreen 不捕获光标，无需移动真实鼠标</summary>
        private byte[] CaptureFrame()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            int vw = vs.Width, vh = vs.Height;
            int iw = 1080;
            int ih = (int)Math.Round((double)vh * iw / vw);
            byte[] ret;
            using (var bmp = new Bitmap(vw, vh))
            {
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(vs.X, vs.Y, 0, 0, bmp.Size);
                using (var thumb = new Bitmap(bmp, iw, ih))
                {
                    var enc = GetJpegEncoder();
                    using (var ms = new MemoryStream())
                    {
                        using (var ep = new EncoderParameters(1))
                        {
                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
                            thumb.Save(ms, enc, ep);
                        }
                        ret = ms.ToArray();
                    }
                }
            }
            _scrW = vw; _scrH = vh; _scrOX = vs.X; _scrOY = vs.Y; _scrIW = iw; _scrIH = ih;
            return ret;
        }

        /// <summary>秒回最新缓存帧（无缓存时即时生成一帧）</summary>
        private void WriteScreen(HttpListenerContext ctx)
        {
            byte[] jpg = _scrCache;
            if (jpg == null) { jpg = CaptureFrame(); }
            ctx.Response.ContentType = "image/jpeg";
            NoCache(ctx);
            ctx.Response.AddHeader("X-SW", _scrW.ToString());
            ctx.Response.AddHeader("X-SH", _scrH.ToString());
            ctx.Response.AddHeader("X-OX", _scrOX.ToString());
            ctx.Response.AddHeader("X-OY", _scrOY.ToString());
            ctx.Response.AddHeader("X-IW", _scrIW.ToString());
            ctx.Response.AddHeader("X-IH", _scrIH.ToString());
            ctx.Response.OutputStream.Write(jpg, 0, jpg.Length);
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            return null;
        }

        private void HandleMouse(HttpListenerContext ctx)
        {
            var p = ReadJson(ctx);
            try
            {
                int x = GetInt(p, "x", 0);
                int y = GetInt(p, "y", 0);
                string action = GetStr(p, "action", "click");
                int scroll = GetInt(p, "scroll", 0);
                Rectangle vs = SystemInformation.VirtualScreen;
                int sx = vs.X + x, sy = vs.Y + y;
                Native.SetCursorPos(sx, sy);
                // 注意：mouse_event 的 dx/dy 是“相对偏移”，已用 SetCursorPos 定位，这里必须传 0,0 原地触发
                switch (action)
                {
                    case "click":
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;
                    case "dbl":
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;
                    case "right":
                        Native.MouseEvent(Native.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                        Native.MouseEvent(Native.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                        break;
                    case "down":
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        break;
                    case "up":
                        Native.MouseEvent(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                        break;
                }
                if (scroll != 0)
                    Native.MouseEvent(Native.MOUSEEVENTF_WHEEL, 0, 0, scroll, 0);
            }
            catch { }
        }

        private void HandleKey(HttpListenerContext ctx)
        {
            var p = ReadJson(ctx);
            try
            {
                string text = GetStr(p, "text", "");
                string key = GetStr(p, "key", "");
                string combo = GetStr(p, "combo", "");
                string ch = GetStr(p, "ch", "");
                int ctrl = GetInt(p, "ctrl", 0);
                int alt = GetInt(p, "alt", 0);
                if (!string.IsNullOrEmpty(text))
                {
                    // 剪贴板 + Ctrl+V，支持中文任意文本
                    Clipboard.SetText(text);
                    Native.KeybdEvent(0x11, 0, 0, 0);          // Ctrl down
                    Native.KeybdEvent(0x56, 0, 0, 0);          // V down
                    Native.KeybdEvent(0x56, 0, 2, 0);          // V up
                    Native.KeybdEvent(0x11, 0, 2, 0);          // Ctrl up
                }
                else if (!string.IsNullOrEmpty(ch))
                {
                    // 完整键盘：逐字符键入（大写/符号自动带 Shift，支持 Ctrl/Alt 粘滞组合）
                    char c = ch[0];
                    byte vk; bool shift;
                    if (MapChar(c, out vk, out shift) && vk != 0)
                    {
                        if (ctrl != 0) Native.KeybdEvent(0x11, 0, 0, 0);
                        if (alt != 0) Native.KeybdEvent(0x12, 0, 0, 0);
                        if (shift) Native.KeybdEvent(0x10, 0, 0, 0);
                        Native.KeybdEvent(vk, 0, 0, 0);
                        Native.KeybdEvent(vk, 0, 2, 0);
                        if (shift) Native.KeybdEvent(0x10, 0, 2, 0);
                        if (alt != 0) Native.KeybdEvent(0x12, 0, 2, 0);
                        if (ctrl != 0) Native.KeybdEvent(0x11, 0, 2, 0);
                    }
                }
                else if (!string.IsNullOrEmpty(combo))
                {
                    string[] parts = combo.Split('+');
                    byte[] mods = new byte[3];
                    int mi = 0;
                    byte main = 0;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        string s = parts[i].Trim().ToLower();
                        if (s == "ctrl") mods[mi++] = 0x11;
                        else if (s == "alt") mods[mi++] = 0x12;
                        else if (s == "win") mods[mi++] = 0x5B;
                        else if (s == "shift") mods[mi++] = 0x10;
                        else main = MapKey(s);
                    }
                    for (int i = 0; i < mi; i++) Native.KeybdEvent(mods[i], 0, 0, 0);
                    if (main != 0)
                    {
                        Native.KeybdEvent(main, 0, 0, 0);
                        Native.KeybdEvent(main, 0, 2, 0);
                    }
                    for (int i = mi - 1; i >= 0; i--) Native.KeybdEvent(mods[i], 0, 2, 0);
                }
                else if (!string.IsNullOrEmpty(key))
                {
                    byte vk = MapKey(key);
                    if (vk != 0)
                    {
                        Native.KeybdEvent(vk, 0, 0, 0);
                        Native.KeybdEvent(vk, 0, 2, 0);
                    }
                }
            }
            catch { }
        }

        private static byte MapKey(string key)
        {
            switch (key)
            {
                case "enter": return 0x0D;
                case "back": return 0x08;
                case "tab": return 0x09;
                case "esc": return 0x1B;
                case "space": return 0x20;
                case "del": return 0x2E;
                case "caps": return 0x14;
                case "ins": return 0x2D;
                case "up": return 0x26;
                case "down": return 0x28;
                case "left": return 0x25;
                case "right": return 0x27;
                case "win": return 0x5B;
                case "f1": return 0x70;
                case "f2": return 0x71;
                case "f3": return 0x72;
                case "f4": return 0x73;
                case "f5": return 0x74;
                case "f6": return 0x75;
                case "f7": return 0x76;
                case "f8": return 0x77;
                case "f9": return 0x78;
                case "f10": return 0x79;
                case "f11": return 0x7A;
                case "f12": return 0x7B;
                case "home": return 0x24;
                case "end": return 0x23;
                case "pgup": return 0x21;
                case "pgdn": return 0x22;
            }
            if (key.Length == 1 && key[0] >= 'a' && key[0] <= 'z') return (byte)(0x41 + (key[0] - 'a'));
            if (key.Length == 1 && key[0] >= '0' && key[0] <= '9') return (byte)(0x30 + (key[0] - '0'));
            return 0;
        }

        /// <summary>字符 → 虚拟键码 + 是否需要 Shift（完整键盘逐键输入用）</summary>
        private static bool MapChar(char c, out byte vk, out bool shift)
        {
            shift = false;
            if (c >= 'a' && c <= 'z') { vk = (byte)(0x41 + (c - 'a')); return true; }
            if (c >= 'A' && c <= 'Z') { vk = (byte)(0x41 + (c - 'A')); shift = true; return true; }
            if (c >= '0' && c <= '9') { vk = (byte)(0x30 + (c - '0')); return true; }
            switch (c)
            {
                case ' ': vk = 0x20; return true;
                case '\t': vk = 0x09; return true;
                case '\r': case '\n': vk = 0x0D; return true;
                case '`': vk = 0xC0; return true;
                case '~': vk = 0xC0; shift = true; return true;
                case '-': vk = 0xBD; return true;
                case '_': vk = 0xBD; shift = true; return true;
                case '=': vk = 0xBB; return true;
                case '+': vk = 0xBB; shift = true; return true;
                case '[': vk = 0xDB; return true;
                case '{': vk = 0xDB; shift = true; return true;
                case ']': vk = 0xDD; return true;
                case '}': vk = 0xDD; shift = true; return true;
                case '\\': vk = 0xDC; return true;
                case '|': vk = 0xDC; shift = true; return true;
                case ';': vk = 0xBA; return true;
                case ':': vk = 0xBA; shift = true; return true;
                case '\'': vk = 0xDE; return true;
                case '"': vk = 0xDE; shift = true; return true;
                case ',': vk = 0xBC; return true;
                case '<': vk = 0xBC; shift = true; return true;
                case '.': vk = 0xBE; return true;
                case '>': vk = 0xBE; shift = true; return true;
                case '/': vk = 0xBF; return true;
                case '?': vk = 0xBF; shift = true; return true;
                case '!': vk = 0x31; shift = true; return true;
                case '@': vk = 0x32; shift = true; return true;
                case '#': vk = 0x33; shift = true; return true;
                case '$': vk = 0x34; shift = true; return true;
                case '%': vk = 0x35; shift = true; return true;
                case '^': vk = 0x36; shift = true; return true;
                case '&': vk = 0x37; shift = true; return true;
                case '*': vk = 0x38; shift = true; return true;
                case '(': vk = 0x39; shift = true; return true;
                case ')': vk = 0x30; shift = true; return true;
            }
            vk = 0;
            return false;
        }

        private void HandleLaunch(HttpListenerContext ctx)
        {
            var p = ReadJson(ctx);
            string path = GetStr(p, "path", "");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                System.Diagnostics.Process.Start(path);
            }
            catch { }
        }

        private static Dictionary<string, object> ReadJson(HttpListenerContext ctx)
        {
            var d = new Dictionary<string, object>();
            try
            {
                using (var r = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                {
                    string body = r.ReadToEnd();
                    if (string.IsNullOrEmpty(body)) return d;
                    // 简易 JSON 解析：{"k":"v","k2":123}
                    int i = 0;
                    while (i < body.Length)
                    {
                        int ks = body.IndexOf('"', i);
                        if (ks < 0) break;
                        int ke = body.IndexOf('"', ks + 1);
                        if (ke < 0) break;
                        string k = body.Substring(ks + 1, ke - ks - 1);
                        int vs = body.IndexOf(':', ke);
                        if (vs < 0) break;
                        vs++;
                        while (vs < body.Length && (body[vs] == ' ' || body[vs] == '\t' || body[vs] == '\r' || body[vs] == '\n')) vs++;
                        if (vs >= body.Length) break;
                        string v;
                        if (body[vs] == '"')
                        {
                            int ve = body.IndexOf('"', vs + 1);
                            if (ve < 0) break;
                            v = body.Substring(vs + 1, ve - vs - 1);
                            i = ve + 1;
                        }
                        else
                        {
                            int ve = body.IndexOfAny(new char[] { ',', '}' }, vs);
                            if (ve < 0) ve = body.Length;
                            v = body.Substring(vs, ve - vs).Trim();
                            i = ve;
                        }
                        d[k] = v;
                    }
                }
            }
            catch { }
            return d;
        }

        private static string GetStr(Dictionary<string, object> d, string k, string def)
        {
            object o;
            if (d.TryGetValue(k, out o) && o != null) return o.ToString();
            return def;
        }

        private static int GetInt(Dictionary<string, object> d, string k, int def)
        {
            int v;
            if (int.TryParse(GetStr(d, k, ""), out v)) return v;
            return def;
        }

        /// <summary>JSON 字符串转义</summary>
        private static string EscJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        /// <summary>远程文件：目录列表</summary>
        private void HandleFiles(HttpListenerContext ctx)
        {
            try
            {
                string p = ctx.Request.QueryString["path"];
                if (string.IsNullOrEmpty(p)) p = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string dir;
                try { dir = Path.GetFullPath(p); }
                catch { WriteJson(ctx, "{\"ok\":false,\"msg\":\"bad path\"}"); return; }
                if (!Directory.Exists(dir)) { WriteJson(ctx, "{\"ok\":false,\"msg\":\"no such dir\"}"); return; }

                var sb = new StringBuilder();
                sb.Append("{\"ok\":true,\"path\":\"").Append(EscJson(dir)).Append("\",\"dirs\":[");
                bool first = true;
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"n\":\"").Append(EscJson(Path.GetFileName(d))).Append("\"}");
                }
                sb.Append("],\"files\":[");
                first = true;
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var fi = new FileInfo(f);
                    sb.Append("{\"n\":\"").Append(EscJson(fi.Name)).Append("\",\"s\":").Append(fi.Length)
                      .Append(",\"t\":\"").Append(fi.LastWriteTime.ToString("MM-dd HH:mm")).Append("\"}");
                }
                sb.Append("]}");
                WriteJson(ctx, sb.ToString());
            }
            catch (Exception ex)
            {
                WriteJson(ctx, "{\"ok\":false,\"msg\":\"" + EscJson(ex.Message) + "\"}");
            }
        }

        /// <summary>远程文件：下载（禁止目录，避免读大目录卡死）</summary>
        private void HandleDownload(HttpListenerContext ctx)
        {
            try
            {
                string p = ctx.Request.QueryString["path"];
                if (string.IsNullOrEmpty(p) || !File.Exists(p))
                {
                    ctx.Response.StatusCode = 404;
                    WriteJson(ctx, "{\"ok\":false,\"msg\":\"no file\"}");
                    return;
                }
                string name = Path.GetFileName(p);
                ctx.Response.ContentType = "application/octet-stream";
                ctx.Response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''" + Uri.EscapeDataString(name));
                using (FileStream fs = File.OpenRead(p))
                {
                    fs.CopyTo(ctx.Response.OutputStream);
                }
                // 响应统一由外层 Handle 关闭，这里不重复 Close
            }
            catch { try { ctx.Response.Close(); } catch { } }
        }

        /// <summary>远程文件：上传（raw body 写入，文件名由参数指定）</summary>
        private void HandleUpload(HttpListenerContext ctx)
        {
            try
            {
                string dir = ctx.Request.QueryString["path"];
                string name = ctx.Request.QueryString["name"];
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
                {
                    ctx.Response.StatusCode = 400;
                    WriteJson(ctx, "{\"ok\":false,\"msg\":\"params\"}");
                    return;
                }
                string full = Path.Combine(Path.GetFullPath(dir), Path.GetFileName(name));
                using (FileStream fs = File.Create(full))
                {
                    var buf = new byte[65536];
                    var st = ctx.Request.InputStream;
                    int n;
                    while ((n = st.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
            catch { }
        }

        private void RollDay()
        {
            if (DateTime.Today != _todayDate)
            {
                _todayDate = DateTime.Today;
                _today.Clear();
            }
        }

        public StatsData Stats
        {
            get
            {
                var sb = new StringBuilder();
                string[] arr = _recent.ToArray();
                for (int i = arr.Length - 1; i >= 0 && i >= arr.Length - 8; i--)
                    sb.AppendLine(arr[i]);
                var d = new StatsData();
                d.Current = _active;
                d.Today = _today.Count;
                d.Total = _total;
                d.RecentText = sb.ToString();
                return d;
            }
        }

        private string BuildHomePage()
        {
            return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>远程桥 · 连接状态</title>
<style>
*{margin:0;padding:0;box-sizing:border-box}
body{font-family:'Segoe UI','PingFang SC','Microsoft YaHei',sans-serif;background:linear-gradient(160deg,#0B0F14,#101B2E);color:#E5E7EB;display:flex;min-height:100vh;align-items:center;justify-content:center;padding:16px}
.card{background:rgba(15,23,42,.9);border:1px solid rgba(148,163,184,.16);border-radius:20px;box-shadow:0 10px 34px rgba(0,0,0,.5);padding:30px 32px;width:100%;max-width:440px;backdrop-filter:blur(14px)}
h1{font-size:20px;color:#93C5FD;margin-bottom:18px}
.row{display:flex;justify-content:space-between;padding:11px 0;border-bottom:1px solid rgba(148,163,184,.12);font-size:14px}
.row span{color:#64748B}
.row b{color:#F1F5F9}
.big{font-size:28px;font-weight:700;color:#60A5FA}
.note{margin-top:16px;font-size:12px;color:#64748B;line-height:1.6}
.ctrl{margin-top:22px;display:block;text-align:center;background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;text-decoration:none;font-size:15px;font-weight:700;padding:14px 0;border-radius:14px;box-shadow:0 6px 20px rgba(59,130,246,.4)}
</style>
</head>
<body>
<div class=""card"">
<h1>远程桥 · 连接状态</h1>
<div class=""row""><span>电脑内网IP</span><b id=""ip"">-</b></div>
<div class=""row""><span>外网IP</span><b id=""ex"">-</b></div>
<div class=""row""><span>当前连接数</span><b class=""big"" id=""cur"">0</b></div>
<div class=""row""><span>今日连接次数</span><b id=""today"">0</b></div>
<div class=""row""><span>累计连接次数</span><b id=""total"">0</b></div>
<a class=""ctrl"" href=""/remote"">进入控制台 · 控制电脑</a>
<p class=""note"">此页面由电脑上的「远程桥」程序提供。数据每 3 秒自动刷新。控制台需要访问密码（电脑托盘气泡/窗口里显示）。</p>
</div>
<script>
function load(){
  fetch('/api/conn').then(function(r){return r.json()}).then(function(d){
    document.getElementById('ip').textContent=d.internalIp||'-';
    document.getElementById('ex').textContent=d.externalIp||'获取失败（需联网）';
    document.getElementById('cur').textContent=d.current;
    document.getElementById('today').textContent=d.today;
    document.getElementById('total').textContent=d.total;
  }).catch(function(){});
}
load();setInterval(load,3000);
</script>
</body>
</html>";
        }

        private string BuildRemotePage()
        {
            return @"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no"">
<title>远程桥 · 控制台</title>
<style>
*{margin:0;padding:0;box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{font-family:'Segoe UI','PingFang SC','Microsoft YaHei',sans-serif;background:#0B0F14;color:#E5E7EB;height:100vh;display:flex;flex-direction:column;overflow:hidden}
.vw{flex:1;position:relative;background:#000;overflow:hidden;touch-action:none;cursor:none}
#view{position:absolute;left:0;top:0;width:100%;height:100%;transform-origin:0 0;will-change:transform;cursor:none}
#view img{width:100%;height:100%;object-fit:contain;display:block;user-select:none;-webkit-user-select:none;cursor:none}
.vcursor{position:absolute;z-index:15;width:28px;height:28px;pointer-events:none;transform:translate(-14px,-14px);filter:drop-shadow(0 2px 6px rgba(0,0,0,.55))}
.vcursor svg{width:100%;height:100%;display:block}
/* 顶部状态胶囊（已按用户要求隐藏，不再显示） */
.topbar{display:none}
.topbar .dot{width:7px;height:7px;border-radius:50%;background:#34D399;box-shadow:0 0 6px #34D399}
.topbar b{color:#0078D4;font-size:13px}
.topbar .fs{margin-left:6px;padding:2px 10px;border-radius:999px;background:#0078D4;color:#fff;font-size:12px;cursor:pointer}
/* 右侧竖排工具栏（默认隐藏，点右边缘“≡”展开） */
.side{position:absolute;right:8px;top:50%;transform:translateY(-50%) translateX(130%);z-index:25;background:rgba(15,23,42,.82);backdrop-filter:blur(16px);-webkit-backdrop-filter:blur(16px);border:1px solid rgba(148,163,184,.16);border-radius:20px;padding:8px 6px;box-shadow:0 10px 36px rgba(0,0,0,.5);display:flex;flex-direction:column;gap:4px;transition:transform .18s ease}
.side.open{transform:translateY(-50%) translateX(0)}
/* 右边缘展开按钮（渐变胶囊） */
.sidebtn{position:absolute;right:0;top:50%;transform:translateY(-50%);z-index:26;width:28px;height:62px;border-radius:14px 0 0 14px;background:rgba(59,130,246,.55);color:#fff;font-size:14px;font-weight:700;display:flex;align-items:center;justify-content:center;cursor:pointer;box-shadow:0 2px 10px rgba(0,0,0,.3);backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px)}
.sidebtn:active{background:rgba(59,130,246,.85)}
.side .grp{display:flex;flex-direction:column;gap:4px;padding:5px 0;border-bottom:1px solid rgba(148,163,184,.13)}
.side .grp:last-child{border-bottom:none}
.side button{background:transparent;border:none;border-radius:12px;padding:8px 4px;font-size:10px;color:#94A3B8;display:flex;flex-direction:column;align-items:center;gap:3px;min-width:54px;transition:background .12s,color .12s}
.side button .ic{font-size:19px;line-height:1}
.side button.on{background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;box-shadow:0 4px 14px rgba(59,130,246,.4)}
.side button:active{background:rgba(59,130,246,.28);color:#fff}
/* 键盘面板（底部弹出，深色毛玻璃） */
.kbd{display:none;position:absolute;left:10px;right:74px;bottom:12px;z-index:30;background:rgba(15,23,42,.88);backdrop-filter:blur(18px);-webkit-backdrop-filter:blur(18px);border:1px solid rgba(148,163,184,.16);border-radius:20px;padding:14px;box-shadow:0 12px 40px rgba(0,0,0,.55);gap:8px}
.kbd.show{display:block}
.ptop{display:flex;align-items:center;justify-content:space-between;margin-bottom:10px}
.ptop b{font-size:14px;color:#F1F5F9}
.ptop span{background:rgba(255,255,255,.08);border:1px solid rgba(148,163,184,.22);border-radius:9px;padding:7px 15px;font-size:12px;color:#93C5FD;cursor:pointer;font-weight:600;transition:background .12s}
.ptop span:active{background:rgba(59,130,246,.32)}
.kbd .row{display:flex;gap:6px;margin-bottom:10px}
.kbd input{flex:1;min-width:0;padding:11px;border-radius:11px;border:1px solid rgba(148,163,184,.24);font-size:15px;background:rgba(255,255,255,.07);color:#F1F5F9;outline:none}
.kbd input::placeholder{color:#64748B}
.kbd .go{background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;border:none;border-radius:11px;padding:0 22px;font-size:14px;font-weight:700;box-shadow:0 3px 12px rgba(59,130,246,.35)}
.kbd .keys{display:flex;gap:6px;flex-wrap:wrap}
.kbd .keys button{background:rgba(255,255,255,.08);color:#E2E8F0;border:1px solid rgba(148,163,184,.18);border-radius:10px;padding:9px 10px;font-size:12px;flex:1;min-width:52px;transition:background .12s}
.kbd .keys button:active{background:rgba(59,130,246,.38);color:#fff}
/* 完整键盘（仿系统屏幕键盘，深色） */
.kbd .fullkb{margin-top:10px;display:flex;flex-direction:column;gap:5px}
.kbd .kbrow{display:flex;gap:4px}
.kbd .kbk{flex:1;min-width:0;background:rgba(255,255,255,.08);color:#E2E8F0;border:1px solid rgba(148,163,184,.16);border-radius:8px;padding:9px 0;font-size:12px;line-height:1;overflow:hidden;transition:background .1s}
.kbd .kbk:active{background:rgba(59,130,246,.42);color:#fff}
.kbd .kbk.mod{background:rgba(59,130,246,.16);color:#93C5FD;border-color:rgba(59,130,246,.32)}
.kbd .kbk.mod.on{background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;border-color:transparent;box-shadow:0 2px 10px rgba(59,130,246,.45)}
.kbd .kbk.space{flex:5}
/* 程序面板（深色） */
.launch{display:none;position:absolute;left:10px;right:74px;bottom:12px;background:rgba(15,23,42,.88);border:1px solid rgba(148,163,184,.16);border-radius:20px;padding:14px;z-index:30;box-shadow:0 12px 40px rgba(0,0,0,.55);gap:8px;flex-wrap:wrap;backdrop-filter:blur(18px);-webkit-backdrop-filter:blur(18px)}
.launch.show{display:flex}
.launch button{background:rgba(255,255,255,.08);color:#E2E8F0;border:1px solid rgba(148,163,184,.18);border-radius:11px;padding:12px 14px;font-size:13px;flex:1;min-width:86px;transition:background .12s}
.launch button:active{background:rgba(59,130,246,.38);color:#fff}
.launch input{width:100%;padding:11px;border-radius:11px;border:1px solid rgba(148,163,184,.24);font-size:14px;background:rgba(255,255,255,.07);color:#F1F5F9;margin-top:9px;outline:none}
.launch input::placeholder{color:#64748B}
/* 文件面板（深色） */
.files{display:none;position:absolute;left:10px;right:74px;bottom:12px;background:rgba(15,23,42,.9);border:1px solid rgba(148,163,184,.16);border-radius:20px;padding:14px;z-index:30;box-shadow:0 12px 40px rgba(0,0,0,.55);flex-direction:column;gap:8px;max-height:64%;overflow:hidden;backdrop-filter:blur(18px);-webkit-backdrop-filter:blur(18px)}
.files.show{display:flex}
.files .frow{display:flex;gap:6px;align-items:center}
.files .frow input[type=text]{flex:1;min-width:0;padding:10px;border-radius:10px;border:1px solid rgba(148,163,184,.24);font-size:13px;background:rgba(255,255,255,.07);color:#F1F5F9;outline:none}
.files .fbtn{background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;border:none;border-radius:10px;padding:0 17px;font-size:13px;font-weight:700;cursor:pointer;box-shadow:0 3px 12px rgba(59,130,246,.35)}
.files .fbtn2{background:rgba(255,255,255,.08);color:#CBD5E1;border:1px solid rgba(148,163,184,.2);border-radius:10px;padding:0 14px;font-size:12px;cursor:pointer}
.files .flist{overflow-y:auto;border:1px solid rgba(148,163,184,.16);border-radius:11px;background:rgba(255,255,255,.04);flex:1;min-height:150px}
.files .it{display:flex;align-items:center;gap:8px;padding:9px 11px;border-bottom:1px solid rgba(148,163,184,.1);font-size:13px;color:#E2E8F0;cursor:pointer}
.files .it:last-child{border-bottom:none}
.files .it:active{background:rgba(59,130,246,.2)}
.files .it .ic{font-size:16px;flex:none}
.files .it .nm{flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.files .it .sz{color:#64748B;font-size:11px;flex:none}
.files .empty{text-align:center;color:#64748B;font-size:13px;padding:26px 0}
/* 横屏（全屏）时：工具栏改底部横排，同样默认隐藏 */
@media (orientation:landscape){
  .side{right:auto;bottom:8px;left:50%;top:auto;transform:translate(-50%,170%);flex-direction:row;padding:7px 9px;border-radius:16px}
  .side.open{transform:translate(-50%,0)}
  .side .grp{flex-direction:row;padding:0 6px;border-bottom:none;border-right:1px solid rgba(148,163,184,.14)}
  .side .grp:last-child{border-right:none}
  .side button{min-width:44px;padding:5px 2px}
  .kbd,.launch,.files{right:12px}
  .sidebtn{top:auto;bottom:8px;right:50%;transform:translateX(50%);width:60px;height:28px;border-radius:14px 14px 0 0}
}
/* toast / 诊断 / PIN（深色风格） */
.toast{position:absolute;top:52px;left:50%;transform:translateX(-50%);background:rgba(30,41,59,.92);border:1px solid rgba(148,163,184,.2);color:#F1F5F9;padding:9px 18px;border-radius:999px;font-size:13px;z-index:40;display:none;white-space:nowrap;box-shadow:0 6px 22px rgba(0,0,0,.4);backdrop-filter:blur(10px)}
.diag{display:none;position:absolute;top:10px;left:50%;transform:translateX(-50%);background:rgba(30,41,59,.9);border:1px solid rgba(251,191,36,.35);color:#FBBF24;font-size:11px;padding:7px 13px;border-radius:10px;z-index:45;max-width:92%;word-break:break-all;box-shadow:0 2px 10px rgba(0,0,0,.3)}
/* PIN 遮罩 */
.mask{position:absolute;inset:0;background:rgba(2,6,23,.86);display:flex;align-items:center;justify-content:center;flex-direction:column;gap:16px;z-index:50;backdrop-filter:blur(6px)}
.mask input{width:70%;max-width:280px;padding:15px;border-radius:14px;border:none;font-size:22px;text-align:center;letter-spacing:8px;background:rgba(255,255,255,.08);color:#F1F5F9;outline:none;border:1px solid rgba(148,163,184,.25)}
.mask input::placeholder{color:#475569;letter-spacing:1px}
.mask .btn{background:linear-gradient(135deg,#3B82F6,#6366F1);color:#fff;border:none;padding:14px 46px;border-radius:14px;font-size:17px;font-weight:700;box-shadow:0 6px 22px rgba(59,130,246,.45)}
.mask p{font-size:13px;color:#9FB2C3}
</style>
</head>
<body>
<div class=""vw"" id=""scr"">
  <div id=""view"">
    <img id=""screen"" alt=""screen"">
  </div>
  <div class=""vcursor"" id=""vcursor""><svg viewBox=""0 0 24 24"" fill=""none""><circle cx=""12"" cy=""12"" r=""9"" fill=""rgba(59,130,246,.28)"" stroke=""#3B82F6"" stroke-width=""2.5""/><circle cx=""12"" cy=""12"" r=""2.4"" fill=""#3B82F6""/></svg></div>
  <div class=""sidebtn"" id=""bSide"">≡</div>
  <div class=""side"" id=""side"">
    <div class=""grp"">
      <button id=""bPointer"" class=""on""><span class=""ic"">🖱</span>指针</button>
      <button id=""bTouch""><span class=""ic"">👆</span>触控</button>
    </div>
    <div class=""grp"">
      <button id=""bKbd""><span class=""ic"">⌨</span>键盘</button>
      <button id=""bRightM""><span class=""ic"">☰</span>右键模式</button>
      <button id=""bLaunch""><span class=""ic"">📂</span>程序</button>
      <button id=""bFiles""><span class=""ic"">🗂</span>文件</button>
    </div>
    <div class=""grp"">
      <button id=""bFit""><span class=""ic"">⤢</span>复位</button>
      <button id=""bSens""><span class=""ic"">⚡</span>速度2x</button>
      <button id=""bDiag""><span class=""ic"">🔍</span>诊断</button>
    </div>
    <div class=""grp"">
      <button id=""bHome""><span class=""ic"">←</span>返回</button>
    </div>
  </div>
    <div class=""kbd"" id=""kbd"">
    <div class=""ptop""><b>⌨ 键盘</b><span id=""bCloseKbd"">← 返回</span></div>
    <div class=""row""><input type=""text"" id=""ktext"" placeholder=""输入文字后点发送（支持中文）""><div class=""go"" id=""bSend"">发送</div></div>
    <div class=""keys"">
      <button data-h=""ctrl+c"">Ctrl+C</button><button data-h=""ctrl+v"">Ctrl+V</button><button data-h=""ctrl+x"">Ctrl+X</button><button data-h=""ctrl+z"">Ctrl+Z</button>
      <button data-h=""ctrl+a"">Ctrl+A</button><button data-h=""alt+tab"">Alt+Tab</button><button data-h=""win+d"">桌面</button><button data-h=""win+e"">文件</button>
      <button data-h=""win+r"">运行</button><button data-k=""f5"">F5</button>
    </div>
    <div class=""fullkb"" id=""fullkb""></div>
  </div>
  <div class=""launch"" id=""launch"">
    <div class=""ptop""><b>📂 程序</b><span id=""bCloseLaunch"">← 返回</span></div>
    <button data-l=""notepad.exe"">记事本</button>
    <button data-l=""calc.exe"">计算器</button>
    <button data-l=""explorer.exe"">此电脑</button>
    <button data-l=""https://www.bing.com"">浏览器</button>
    <input type=""text"" id=""lpath"" placeholder=""输入程序或文件路径，如 D:\a.exe"">
    <button id=""bLaunchGo"" style=""width:100%"">打开</button>
  </div>
  <div class=""files"" id=""files"">
    <div class=""ptop""><b>🗂 文件</b><span id=""bCloseFiles"">← 返回</span></div>
    <div class=""frow"">
      <input type=""text"" id=""fpath"" value="""">
      <button class=""fbtn2"" id=""bFup"">↑上级</button>
      <button class=""fbtn"" id=""bFgo"">打开</button>
      <button class=""fbtn"" id=""bFupl"">上传</button>
      <input type=""file"" id=""fupload"" style=""display:none"" multiple>
    </div>
    <div class=""flist"" id=""flist""></div>
  </div>
  <div class=""toast"" id=""toast""></div>
  <div class=""diag"" id=""diag""></div>
  <div class=""mask"" id=""mask"">
    <p>请输入电脑上的访问密码（PIN）</p>
    <input type=""password"" id=""pin"" inputmode=""numeric"" maxlength=""6"">
    <div class=""btn"" id=""btnPin"">进入控制台</div>
  </div>
</div>
<script>
var authed=false, sw=1920, sh=1080, ox=0, oy=0;
var isTouch = ('ontouchstart' in window) || (navigator.maxTouchPoints > 0 && window.matchMedia('(pointer:coarse)').matches);
var mode=isTouch?'touch':'pointer', sens=2, diag=false, firstSync=true;
var rightTap=false;
var curX=960, curY=540;          // 虚拟鼠标（电脑坐标）
var dispW=0, dispH=0, offX=0, offY=0;
var viewScale=1, viewX=0, viewY=0;   // 画面缩放/平移
var el=function(id){return document.getElementById(id)};

function toast(t){var x=el('toast');x.textContent=t;x.style.display='block';setTimeout(function(){x.style.display='none'},1400);}
function diagMsg(s){el('diag').style.display='block';el('diag').textContent=s;}

function applyView(){
  el('view').style.transform='translate('+viewX+'px,'+viewY+'px) scale('+viewScale+')';
}
function calcDisp(){
  var img=el('screen');
  var r=img.getBoundingClientRect();
  var scale=Math.min(r.width/sw, r.height/sh);
  dispW=sw*scale; dispH=sh*scale;
  offX=(r.width-dispW)/2; offY=(r.height-dispH)/2;
  return r;
}
function showCursor(){
  var r=calcDisp();
  var dx=(curX-ox)/sw*dispW+offX;
  var dy=(curY-oy)/sh*dispH+offY;
  var c=el('vcursor');
  c.style.left=(dx/r.width*100)+'%';
  c.style.top=(dy/r.height*100)+'%';
}
function sendMouse(x,y,action,scroll){
  fetch('/api/mouse',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({x:x,y:y,action:action,scroll:scroll||0})}).then(function(){poke();}).catch(function(){});
}
function sendCombo(c){
  fetch('/api/key',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({combo:c})}).then(function(){poke();}).catch(function(){});
}
var lastMoveT=0;
function throttledMove(){
  var now=Date.now();
  if(now-lastMoveT<30)return;
  lastMoveT=now;
  sendMouse(curX-ox, curY-oy, 'move', 0);
}
// 绝对换算（getBoundingClientRect 已含缩放平移，自动正确）
function posAt(cx,cy){
  var img=el('screen');
  var r=img.getBoundingClientRect();
  var scale=Math.min(r.width/sw, r.height/sh);
  var dw=sw*scale, dh=sh*scale;
  var oxx=(r.width-dw)/2, oyy=(r.height-dh)/2;
  var px=(cx-r.left-oxx)/dw, py=(cy-r.top-oyy)/dh;
  if(px<0||px>1||py<0||py>1)return null;
  return {x:Math.round(px*sw)+ox, y:Math.round(py*sh)+oy};
}

// ---- 手势：单指（移动光标/拖拽/轻点），双指（缩放/滚动）----
var pressed=false, isDrag=false, moved=0, sx=0, sy=0, downT=0;
var longT=null, longFired=false;
var lastTapT=0, lastTapX=0, lastTapY=0;
var pinch=null;

function down1(x,y){
  if(pressed)return;
  pressed=true; isDrag=false; moved=0; sx=x; sy=y; downT=Date.now(); longFired=false;
  longT=setTimeout(function(){ longT=null; if(pressed && moved<14) longFired=true; },480);
}
function panView(dx,dy){
  var vw=el('scr').clientWidth, vh=el('scr').clientHeight;
  var mx=vw*(viewScale-1)/2+vw*0.08, my=vh*(viewScale-1)/2+vh*0.08;
  viewX=Math.max(-mx,Math.min(0,viewX+dx));
  viewY=Math.max(-my,Math.min(0,viewY+dy));
  applyView();
}
function move1(x,y){
  if(!pressed)return;
  var dx=x-sx, dy=y-sy;
  sx=x; sy=y;
  moved+=Math.abs(dx)+Math.abs(dy);
  // 放大状态下：单指拖动=平移画面
  if(viewScale>1.02){
    if(moved>6)panView(dx,dy);
    return;
  }
  if(moved>10 && !isDrag){
    if(longT){clearTimeout(longT);longT=null;}
    longFired=false;
    isDrag=true;
    sendMouse(curX-ox, curY-oy, 'down', 0);
    if(diag)diagMsg('拖拽开始 @('+Math.round(curX)+','+Math.round(curY)+')');
  }
  curX=Math.max(ox,Math.min(ox+sw,curX+dx*sens));
  curY=Math.max(oy,Math.min(oy+sh,curY+dy*sens));
  if(mode==='pointer')showCursor();
  throttledMove();
  if(diag)diagMsg('移动'+Math.round(dx*sens)+','+Math.round(dy*sens)+' 光标('+Math.round(curX)+','+Math.round(curY)+')');
}
function up1(x,y){
  if(!pressed)return;
  if(longT){clearTimeout(longT);longT=null;}
  if(longFired && moved<14){
    // 长按右键：触控模式=手指按的位置，指针模式=光标位置
    if(mode==='touch'){
      var pp=posAt(x,y);
      if(pp){sendMouse(pp.x,pp.y,'right',0);if(diag)diagMsg('长按右键 @('+pp.x+','+pp.y+')');}
    } else {
      sendMouse(curX-ox, curY-oy, 'right', 0);
      if(diag)diagMsg('长按右键 @('+Math.round(curX)+','+Math.round(curY)+')');
    }
  } else if(isDrag){
    sendMouse(curX-ox, curY-oy, 'up', 0);
    if(diag)diagMsg('拖拽结束 @('+Math.round(curX)+','+Math.round(curY)+')');
  } else if(moved<10){
    var now=Date.now();
    if(mode==='pointer'){
      if(now-lastTapT<350 && Math.abs(curX-lastTapX)<50 && Math.abs(curY-lastTapY)<50){
        lastTapT=0; sendMouse(curX-ox, curY-oy, 'dbl', 0);
        if(diag)diagMsg('双击 @('+Math.round(curX)+','+Math.round(curY)+')');
      } else {
        lastTapT=now; lastTapX=curX; lastTapY=curY;
        sendMouse(curX-ox, curY-oy, rightTap?'right':'click', 0);
        if(diag)diagMsg((rightTap?'右键模式-单击':'单击')+' @('+Math.round(curX)+','+Math.round(curY)+')');
      }
    } else {
      var p=posAt(x,y);
      if(p){
        // 触控模式：轻点一次=单击（不做双击检测，避免连点被误判成双击）
        sendMouse(p.x, p.y, rightTap?'right':'click', 0);
        if(diag)diagMsg((rightTap?'右键模式-单击':'单击')+' @('+p.x+','+p.y+')');
      }
    }
  }
  pressed=false; isDrag=false; longFired=false; moved=0;
}

var sc=el('scr');
sc.addEventListener('touchstart',function(e){
  if(!authed)return;
  if(e.touches.length===1){ down1(e.touches[0].clientX, e.touches[0].clientY); }
  else if(e.touches.length===2){
    if(pressed){ if(longT){clearTimeout(longT);longT=null;} if(isDrag)sendMouse(curX-ox,curY-oy,'up',0); pressed=false; isDrag=false; }
    var a=e.touches[0], b=e.touches[1];
    var d=Math.hypot(a.clientX-b.clientX, a.clientY-b.clientY);
    pinch={dist:d, scale:viewScale, midX:(a.clientX+b.clientX)/2, midY:(a.clientY+b.clientY)/2, viewX:viewX, viewY:viewY, mode:'none'};
  }
},{passive:true});
sc.addEventListener('touchmove',function(e){
  if(!authed)return;
  if(e.touches.length===2 && pinch){
    e.preventDefault();
    var a=e.touches[0], b=e.touches[1];
    var d=Math.hypot(a.clientX-b.clientX, a.clientY-b.clientY);
    var midX=(a.clientX+b.clientX)/2, midY=(a.clientY+b.clientY)/2;
    if(pinch.mode==='none'){
      if(Math.abs(d-pinch.dist)>18) pinch.mode='zoom';
      else if(Math.abs(midY-pinch.midY)>18) pinch.mode='scroll';
    }
    if(pinch.mode==='zoom'){
      var ns=Math.max(1, Math.min(4, pinch.scale*d/pinch.dist));
      if(Math.abs(ns-viewScale)>0.01){
        viewScale=ns;
        viewX=pinch.viewX+(midX-pinch.midX);
        viewY=pinch.viewY+(midY-pinch.midY);
        var vw=el('scr').clientWidth, vh=el('scr').clientHeight;
        var mx=vw*(viewScale-1)/2+vw*0.08, my=vh*(viewScale-1)/2+vh*0.08;
        viewX=Math.max(-mx,Math.min(0,viewX));
        viewY=Math.max(-my,Math.min(0,viewY));
        applyView();
        if(mode==='pointer')showCursor();
      }
    } else if(pinch.mode==='scroll'){
      var dy=midY-pinch.midY;
      if(Math.abs(dy)>25){ sendMouse(0,0,'move',dy>0?-120:120); pinch.midY=midY; }
    }
    return;
  }
  if(e.touches.length!==1 || !pressed)return;
  e.preventDefault();
  move1(e.touches[0].clientX, e.touches[0].clientY);
},{passive:false});
sc.addEventListener('touchend',function(e){
  if(!authed)return;
  if(pinch){
    pinch=null;
    if(pressed){pressed=false; isDrag=false;}
    return;
  }
  if(e.changedTouches.length===1 && e.touches.length===0 && pressed){
    up1(e.changedTouches[0].clientX, e.changedTouches[0].clientY);
  } else {
    if(pressed){ if(longT){clearTimeout(longT);longT=null;} if(isDrag)sendMouse(curX-ox,curY-oy,'up',0); pressed=false; isDrag=false; }
  }
},{passive:true});

// 电脑浏览器/平板：鼠标事件同逻辑
sc.addEventListener('mousedown',function(e){ if(!authed)return; down1(e.clientX,e.clientY); });
window.addEventListener('mousemove',function(e){ if(!authed)return; if(pressed)move1(e.clientX,e.clientY); });
window.addEventListener('mouseup',function(e){ if(!authed)return; if(pressed)up1(e.clientX,e.clientY); });

// ---- 侧边工具栏 ----
function setMode(m){
  mode=m;
  el('bPointer').classList.toggle('on',m==='pointer');
  el('bTouch').classList.toggle('on',m==='touch');
  if(m==='pointer'){ el('vcursor').style.display='block'; showCursor(); }
  else { el('vcursor').style.display='none'; }
  toast(m==='pointer'?'指针模式（触控板）':'触控模式（指哪点哪）');
}
el('bPointer').onclick=function(){setMode('pointer');};
el('bTouch').onclick=function(){setMode('touch');};
setMode(mode);
el('bSide').onclick=function(){el('side').classList.toggle('open');};
el('bRightM').onclick=function(){rightTap=!rightTap;this.classList.toggle('on',rightTap);toast(rightTap?'右键模式开：轻点=右键':'右键模式关：轻点=左键');};
el('bFit').onclick=function(){viewScale=1;viewX=0;viewY=0;applyView();showCursor();toast('画面已复位');};
el('bSens').onclick=function(){sens=sens===1?2:(sens===2?3:1);el('bSens').textContent='速度'+sens+'x';toast('鼠标速度：'+sens+'x');};
el('bDiag').onclick=function(){diag=!diag;this.classList.toggle('on',diag);el('diag').style.display=diag?'block':'none';toast(diag?'诊断已开':'诊断已关');};
el('bKbd').onclick=function(){el('launch').classList.remove('show');el('kbd').classList.toggle('show');};
el('bCloseKbd').onclick=function(){el('kbd').classList.remove('show');};
el('bCloseLaunch').onclick=function(){el('launch').classList.remove('show');};
el('bCloseFiles').onclick=function(){el('files').classList.remove('show');};
el('bSend').onclick=function(){var t=el('ktext').value;if(!t)return;sendKey(t);};
el('ktext').addEventListener('keydown',function(e){if(e.key==='Enter'){el('bSend').click();}});
document.querySelectorAll('.kbd .keys button').forEach(function(b){
  b.onclick=function(){
    var h=b.getAttribute('data-h');
    if(h){sendCombo(h);return;}
    sendKey('',b.getAttribute('data-k'));
  };
});

// ===== 完整键盘（仿系统屏幕键盘：字母/数字/符号/功能键，Shift/Caps/Ctrl/Alt 粘滞） =====
var Q=String.fromCharCode(39), DQ=String.fromCharCode(34);   // 单引号与双引号字符常量（避免转义问题）
var kbDef = [
  ['esc','`','1','2','3','4','5','6','7','8','9','0','-','=','back'],
  ['tab','q','w','e','r','t','y','u','i','o','p','[',']','\\','del'],
  ['caps','a','s','d','f','g','h','j','k','l',';',Q,'enter'],
  ['shift','z','x','c','v','b','n','m',',','.','/','shift'],
  ['ctrl','win','alt','space','alt','ctrl','left','up','down','right']
];
var kbLabel = {esc:'Esc',tab:'Tab',back:'⌫',del:'Del',caps:'Caps',enter:'⏎',shift:'⇧',ctrl:'Ctrl',alt:'Alt',win:'⊞',space:'空格',left:'←',up:'↑',down:'↓',right:'→'};
var shiftOn=false, capsOn=false, ctrlOn=false, altOn=false;
function shiftUp(c){
  var m={'`':'~','1':'!','2':'@','3':'#','4':'$','5':'%','6':'^','7':'&','8':'*','9':'(','0':')','-':'_','=':'+','[':'{',']':'}','\\':'|',';':':',[Q]:DQ,',':'<','.':'>','/':'?'};
  return m[c]||c.toUpperCase();
}
function tapKey(ch,ctrl,alt){
  fetch('/api/key',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({ch:ch,ctrl:ctrl?1:0,alt:alt?1:0})}).catch(function(){});
}
function kbClick(k){
  if(k==='shift'){ shiftOn=!shiftOn; paintKb(); return; }
  if(k==='caps'){ capsOn=!capsOn; sendKey('','caps'); paintKb(); return; }
  if(k==='ctrl'){ ctrlOn=!ctrlOn; paintKb(); return; }
  if(k==='alt'){ altOn=!altOn; paintKb(); return; }
  if(['esc','tab','back','del','enter','win','left','up','down','right'].indexOf(k)>=0){
    sendKey('',k);
  } else {
    var c=k;
    if(shiftOn) c=shiftUp(c);
    else if(capsOn && /^[a-z]$/.test(c)) c=c.toUpperCase();
    tapKey(c,ctrlOn,altOn);
  }
  if(shiftOn){ shiftOn=false; paintKb(); }
  if(ctrlOn){ ctrlOn=false; paintKb(); }
  if(altOn){ altOn=false; paintKb(); }
}
function buildKb(){
  var kb=el('fullkb'), h='';
  kbDef.forEach(function(row){
    h+='<div class=""kbrow"">';
    row.forEach(function(k){
      var cls='kbk'+(('shift caps ctrl alt'.split(' ').indexOf(k)>=0)?' mod':'')+(k==='space'?' space':'');
      h+='<button class=""'+cls+'"" data-k=""'+k+'"">'+(kbLabel[k]||k)+'</button>';
    });
    h+='</div>';
  });
  kb.innerHTML=h;
  kb.querySelectorAll('.kbk').forEach(function(b){
    b.onclick=function(){kbClick(b.getAttribute('data-k'));};
  });
}
function paintKb(){
  var kb=el('fullkb');
  kb.querySelectorAll('.kbk').forEach(function(b){
    var k=b.getAttribute('data-k');
    var on=(k==='shift'&&shiftOn)||(k==='caps'&&capsOn)||(k==='ctrl'&&ctrlOn)||(k==='alt'&&altOn);
    b.classList.toggle('on',on);
  });
}
buildKb();
function sendKey(text,key){
  fetch('/api/key',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({text:text,key:key||''})}).catch(function(){});
  if(text)el('ktext').value='';
}
el('bLaunch').onclick=function(){el('kbd').classList.remove('show');el('launch').classList.toggle('show');};
document.querySelectorAll('.launch button[data-l]').forEach(function(b){
  b.onclick=function(){launchApp(b.getAttribute('data-l'));el('launch').classList.remove('show');};
});
el('bLaunchGo').onclick=function(){var t=el('lpath').value;if(!t)return;launchApp(t);el('launch').classList.remove('show');};
function launchApp(path){
  fetch('/api/launch',{method:'POST',headers:{'Content-Type':'application/json'},
    body:JSON.stringify({path:path})}).catch(function(){});
}

// ---- 文件传输（UU远程式） ----
var curFileDir='';
el('bFiles').onclick=function(){
  el('kbd').classList.remove('show');el('launch').classList.remove('show');
  var show=el('files').classList.toggle('show');
  if(show){ if(!curFileDir){curFileDir='/';} loadFiles(curFileDir); }
};
el('bFup').onclick=function(){
  if(curFileDir==='/'||curFileDir==='')return;
  var p=curFileDir.replace(/\/+$/,'');
  var i=p.lastIndexOf('/');
  curFileDir = i>0 ? p.substring(0,i) : '/';
  el('fpath').value=curFileDir;
  loadFiles(curFileDir);
};
el('bFgo').onclick=function(){
  var p=el('fpath').value.trim();
  if(p){curFileDir=p.replace(/\\/g,'/');loadFiles(curFileDir);}
};
el('fpath').addEventListener('keydown',function(e){if(e.key==='Enter'){el('bFgo').click();}});
el('bFupl').onclick=function(){el('fupload').click();};
el('fupload').addEventListener('change',function(){
  var f=this.files;
  for(var i=0;i<f.length;i++){ uploadFile(f[i]); }
  this.value='';
});
function loadFiles(path){
  el('flist').innerHTML='<div class=""empty"">加载中…</div>';
  fetch('/api/files?path='+encodeURIComponent(path)).then(function(r){return r.json()}).then(function(d){
    if(!d.ok){el('flist').innerHTML='<div class=""empty"">无法打开该目录</div>';return;}
    el('fpath').value=d.path;
    curFileDir=d.path;
    var h='';
    d.dirs.forEach(function(x){ h+='<div class=""it"" data-d=""'+x.n+'""><span class=""ic"">📁</span><span class=""nm"">'+x.n+'</span><span class=""sz"">目录</span></div>'; });
    d.files.forEach(function(x){
      var sz=x.s>1048576 ? (x.s/1048576).toFixed(1)+' MB' : (x.s>1024 ? Math.round(x.s/1024)+' KB' : x.s+' B');
      h+='<div class=""it"" data-f=""'+x.n+'""><span class=""ic"">📄</span><span class=""nm"">'+x.n+'</span><span class=""sz"">'+x.t+' '+sz+'</span></div>';
    });
    if(!h)h='<div class=""empty"">空目录</div>';
    el('flist').innerHTML=h;
    el('flist').querySelectorAll('.it[data-d]').forEach(function(it){
      it.onclick=function(){
        var p=curFileDir.replace(/\/+$/,'')+'/'+it.getAttribute('data-d');
        curFileDir=p; el('fpath').value=p; loadFiles(p);
      };
    });
    el('flist').querySelectorAll('.it[data-f]').forEach(function(it){
      it.onclick=function(){
        var p=curFileDir.replace(/\/+$/,'')+'/'+it.getAttribute('data-f');
        downloadFile(p);
      };
    });
  }).catch(function(){el('flist').innerHTML='<div class=""empty"">加载失败</div>';});
}
function downloadFile(path){
  fetch('/api/download?path='+encodeURIComponent(path)).then(function(r){
    if(!r.ok)return;
    return r.blob();
  }).then(function(bl){
    if(!bl)return;
    var a=document.createElement('a');
    a.href=URL.createObjectURL(bl);
    var nm=decodeURIComponent(path.split('/').pop());
    a.download=nm;
    a.click();
    toast('已开始下载');
  }).catch(function(){toast('下载失败');});
}
function uploadFile(file){
  fetch('/api/upload?path='+encodeURIComponent(curFileDir)+'&name='+encodeURIComponent(file.name),{
    method:'POST', body:file
  }).then(function(r){return r.json()}).then(function(d){
    if(d.ok){toast('已上传 '+file.name);loadFiles(curFileDir);}
    else{toast('上传失败');}
  }).catch(function(){toast('上传失败');});
}
el('bHome').onclick=function(){location.href='/';};

// ---- 登录 & 启动 ----
function check(){
  fetch('/api/check').then(function(r){return r.json()}).then(function(d){
    if(d.ok){authed=true;el('mask').style.display='none';start();}
  }).catch(function(){});
}
el('btnPin').onclick=function(){
  var p=el('pin').value;
  fetch('/api/pin?pin='+encodeURIComponent(p)).then(function(r){return r.json()}).then(function(d){
    if(d.ok){authed=true;el('mask').style.display='none';toast('已进入控制台');start();}
    else{toast('密码不对');}
  });
};
function start(){
  loadScreen();
  setInterval(loadScreen,100);   // 100ms 一帧（后台缓存秒回），操作后立即补帧
}
var scrBusy=false, lastPoke=0;
function poke(){   // 操作后立即补一帧（防连发：最短120ms一次）
  var now=Date.now();
  if(now-lastPoke<120)return;
  lastPoke=now;
  loadScreen();
}
function loadScreen(){
  if(scrBusy)return;   // 上一帧没回来就先跳过，不排队堆积
  scrBusy=true;
  var t=new Date().getTime();
  fetch('/api/screen?_='+t).then(function(r){
    var swn=parseInt(r.headers.get('X-SW')||'0');
    if(swn>0){
      sw=swn; sh=parseInt(r.headers.get('X-SH')||'1080');
      ox=parseInt(r.headers.get('X-OX')||'0'); oy=parseInt(r.headers.get('X-OY')||'0');
    }
    return r.blob();
  }).then(function(b){
    scrBusy=false;
    if(!b)return;
    var img=el('screen');
    if(img._ob)URL.revokeObjectURL(img._ob);
    img._ob=URL.createObjectURL(b);
    img.src=img._ob;
    if(firstSync){
      firstSync=false;
      curX=ox+sw/2; curY=oy+sh/2;
      sendMouse(curX-ox, curY-oy, 'move', 0);
    }
    showCursor();
  }).catch(function(){scrBusy=false;});
}
check();
</script>
</body>
</html>";
        }
    }

    /// <summary>Win32 调用</summary>
    internal static class Native
    {
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        public static void MouseEvent(uint flags, int x, int y, int data, int extra)
        {
            mouse_event(flags, x, y, (uint)data, UIntPtr.Zero);
        }

        public static void KeybdEvent(byte vk, byte scan, uint flags, int extra)
        {
            keybd_event(vk, scan, flags, UIntPtr.Zero);
        }

    }
}
