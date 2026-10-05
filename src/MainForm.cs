using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace MonitorPower
{
    enum Tone { Neutral, On, Off, Busy, Pending, Warn, Fail }

    // Laid out like RemoteAccessHub: header (title, status pills, settings, more), the monitor card where RemoteAccessHub has its stepper,
    // the banner that narrates what happens, and the action row. The info and log panels open below, the way RemoteAccessHub's log does.
    public sealed class MainForm : Form
    {
        public static readonly int ShowMessage = NativeMethods.RegisterWindowMessage("MonitorPower.ShowWindow");
        public const int MainWidth = 880, MinWidth = 820, PanelHeight = 230;
        const int PowerOnCheckMs = 300, PickerWidth = 340;

        readonly Panel topArea = new Panel(), header = new Panel(), actions = new Panel(), panelArea = new Panel(), panelHead = new Panel();
        readonly Label title = new Label { Text = "Monitor Power", AutoSize = true };
        readonly StatusPill sessionPill = new StatusPill(), powerPill = new StatusPill();
        readonly FlatButton settingsButton = new FlatButton { IconOnly = true, Glyph = Theme.Glyph.Settings, Variant = ButtonVariant.Ghost, Text = "설정", AccessibleName = "설정" };
        readonly FlatButton moreButton = new FlatButton { IconOnly = true, Glyph = Theme.Glyph.More, Variant = ButtonVariant.Ghost, Text = "더 보기", AccessibleName = "더 보기" };
        readonly CardPanel card = new CardPanel(), panelCard = new CardPanel();
        readonly StateBadge badge = new StateBadge();
        readonly Label state = new Label { Text = "상태 확인 전", AutoEllipsis = true }, stateDetail = new Label { AutoEllipsis = true };
        readonly Label pickerCaption = new Label { Text = "제어할 모니터", AutoSize = true }, panelTitle = new Label { AutoSize = true };
        readonly ProgressLine progress = new ProgressLine { Visible = false };
        readonly ThemedComboBox monitors = new ThemedComboBox { DropDownHeight = 200, AccessibleName = "제어할 모니터" };
        readonly FlatButton refresh = new FlatButton { IconOnly = true, Glyph = Theme.Glyph.Refresh, Text = "목록 새로고침", AccessibleName = "모니터 목록 새로고침" };
        readonly StatusBanner banner = new StatusBanner();
        readonly FlatButton on = new FlatButton { Text = "전원 켜기", Glyph = Theme.Glyph.Power }, off = new FlatButton { Text = "전원 끄기", Glyph = Theme.Glyph.Moon };
        readonly FlatButton query = new FlatButton { Text = "상태 확인", Glyph = Theme.Glyph.Refresh }, autoButton = new FlatButton { Glyph = Theme.Glyph.Sync, SplitWidth = 26 };
        readonly FlatButton cancel = new FlatButton { Text = "취소", Glyph = Theme.Glyph.Cancel, Variant = ButtonVariant.Danger, Visible = false };
        readonly FlatButton infoButton = new FlatButton { Text = "정보", Glyph = Theme.Glyph.Info, Variant = ButtonVariant.Ghost }, logButton = new FlatButton { Text = "기록", Glyph = Theme.Glyph.Log, Variant = ButtonVariant.Ghost };
        readonly FlatButton panelCopy = new FlatButton { Text = "복사", Glyph = Theme.Glyph.Copy, Variant = ButtonVariant.Ghost }, panelClear = new FlatButton { Text = "지우기", Glyph = Theme.Glyph.Delete, Variant = ButtonVariant.Ghost };
        readonly ListView details = new ListView { Dock = DockStyle.Fill, View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, BorderStyle = BorderStyle.None,
            MultiSelect = true, HideSelection = false, ShowItemToolTips = true, OwnerDraw = true, AccessibleName = "모니터 정보" };
        readonly TextBox log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9f), AccessibleName = "활동 기록" };
        readonly ContextMenuStrip moreMenu = new ContextMenuStrip(), autoMenu = new ContextMenuStrip(), trayMenu = new ContextMenuStrip();
        readonly ToolStripMenuItem themeSystem = new ToolStripMenuItem("시스템 설정 따르기"), themeDark = new ToolStripMenuItem("어둡게"), themeLight = new ToolStripMenuItem("밝게"), autoOff = new ToolStripMenuItem("자동 새로고침 끄기");
        readonly ToolStripMenuItem trayOpen = new ToolStripMenuItem("Monitor Power 열기"), trayTarget = new ToolStripMenuItem { Enabled = false }, trayOn = new ToolStripMenuItem("전원 켜기"), trayOff = new ToolStripMenuItem("전원 끄기"),
            trayQuery = new ToolStripMenuItem("상태 확인"), trayAuto = new ToolStripMenuItem { CheckOnClick = true }, trayKeep = new ToolStripMenuItem("닫아도 알림 영역에서 실행") { CheckOnClick = true }, trayExit = new ToolStripMenuItem("종료");
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 200 };
        readonly Timer poller = new Timer();
        readonly MonitorClient client;
        readonly string tool, settingsPath;
        readonly AppSettings settings;
        bool busy, loaded, exiting, trayNoticeShown, panelOpen, remote;
        int lastCode = int.MinValue;
        Tone tone = Tone.Neutral;
        Task polling;
        CancellationTokenSource powerOn;

        MonitorInfo Selected { get { return monitors.SelectedItem as MonitorInfo; } }
        bool CanControl { get { return !busy && Selected != null && !Selected.Remote && !SystemInformation.TerminalServerSession && File.Exists(tool); } }

        public MainForm() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ControlMyMonitor.exe"), AppSettings.DefaultPath) { }
        public MainForm(string toolPath, string settingsFile)
        {
            tool = toolPath; client = new MonitorClient(tool); settingsPath = settingsFile; settings = AppSettings.Load(settingsPath);
            Theme.Apply(Theme.ParseMode(settings.ThemeName));
            BuildUi(); BuildMenus(); HookEvents();
            ApplyTheme(); ApplyPanel(settings.DetailPanel, false); ShowSelected();
        }

        // ================================================================ layout

        void BuildUi()
        {
            Text = "Monitor Power"; Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); Font = Theme.UiFont(9.5f);
            AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.CenterScreen; KeyPreview = true; MaximizeBox = false;

            title.Font = Theme.UiFont(13f, FontStyle.Bold); title.Location = new Point(0, 9);
            settingsButton.Size = moreButton.Size = new Size(38, 36);
            header.Controls.AddRange(new Control[] { title, sessionPill, powerPill, settingsButton, moreButton });
            header.Resize += (s, e) => LayoutHeader();

            state.Font = Theme.UiFont(15f, FontStyle.Bold); stateDetail.Font = Theme.UiFont(9.5f); pickerCaption.Font = Theme.UiFont(9f);
            state.LiveSetting = AutomationLiveSetting.Polite; // announces power-state changes to screen readers
            card.Controls.AddRange(new Control[] { badge, state, stateDetail, progress, pickerCaption, monitors, refresh });
            card.Resize += (s, e) => LayoutCard();

            actions.Controls.AddRange(new Control[] { on, off, query, autoButton, cancel, infoButton, logButton });
            actions.Resize += (s, e) => LayoutActions();

            // Dock=Top stacks in reverse order of adding, so the header goes in last.
            topArea.Dock = header.Dock = card.Dock = banner.Dock = actions.Dock = DockStyle.Top;
            topArea.Padding = new Padding(16, 12, 16, 10);
            header.Height = 44; card.Height = 100; banner.Height = 54; actions.Height = 40;
            foreach (Control c in new[] { actions, Spacer(10), banner, Spacer(10), card, Spacer(8), header }) topArea.Controls.Add(c);
            int height = topArea.Padding.Vertical;
            foreach (Control c in topArea.Controls) height += c.Height;
            topArea.Height = height;

            panelArea.Dock = DockStyle.Top; panelArea.Height = PanelHeight; panelArea.Padding = new Padding(16, 0, 16, 12);
            panelCard.Dock = DockStyle.Fill; panelCard.Padding = new Padding(12, 6, 12, 12);
            panelHead.Dock = DockStyle.Top; panelHead.Height = 40;
            panelTitle.Font = Theme.UiFont(10.5f, FontStyle.Bold); panelTitle.Location = new Point(2, 11);
            panelHead.Controls.AddRange(new Control[] { panelTitle, panelCopy, panelClear });
            panelHead.Resize += (s, e) => LayoutPanelHead();
            details.Columns.Add("항목", 150); details.Columns.Add("값", 600);
            panelCard.Controls.Add(details); panelCard.Controls.Add(log); panelCard.Controls.Add(panelHead);
            panelArea.Controls.Add(panelCard);

            Controls.Add(panelArea); Controls.Add(topArea);
            ClientSize = new Size(MainWidth, topArea.Height);
        }

        static Control Spacer(int height) { return new Panel { Dock = DockStyle.Top, Height = height, Tag = "spacer" }; }

        void LayoutHeader()
        {
            moreButton.Location = new Point(header.Width - moreButton.Width, 4);
            settingsButton.Location = new Point(moreButton.Left - settingsButton.Width - 2, 4);
            powerPill.Location = new Point(settingsButton.Left - 10 - powerPill.Width, 7);
            sessionPill.Location = new Point(powerPill.Left - 8 - sessionPill.Width, 7);
        }

        void LayoutCard()
        {
            const int pad = 16;
            int w = card.ClientSize.Width, h = card.ClientSize.Height, px = w - pad - PickerWidth;
            badge.Location = new Point(pad + 2, (h - badge.Height) / 2);
            pickerCaption.Location = new Point(px, 16);
            refresh.Size = new Size(38, monitors.Height);
            monitors.SetBounds(px, 40, PickerWidth - refresh.Width - 8, monitors.Height);
            refresh.Location = new Point(monitors.Right + 8, 40);
            int tx = badge.Right + 16, tw = Math.Max(80, px - 24 - tx);
            state.SetBounds(tx, 15, tw, 32);
            stateDetail.SetBounds(tx, 50, tw, 20);
            progress.SetBounds(tx, 78, Math.Min(tw, 280), 4);
        }

        void LayoutActions()
        {
            const int gap = 8;
            int h = actions.Height, x = 0;
            foreach (FlatButton b in new[] { on, off, query, autoButton })
            {
                b.SetBounds(x, 0, Math.Max(b == autoButton ? 0 : 118, b.PreferredWidth()), h);
                x += b.Width + gap;
            }
            int rx = actions.Width;
            foreach (FlatButton b in new[] { logButton, infoButton, cancel })
            {
                if (b == cancel && powerOn == null) continue;
                int bw = b.PreferredWidth(); rx -= bw;
                b.SetBounds(rx, 0, bw, h); rx -= gap;
            }
        }

        void LayoutPanelHead()
        {
            int rx = panelHead.Width;
            panelClear.Visible = settings.DetailPanel == AppSettings.PanelLog;
            foreach (FlatButton b in new[] { panelCopy, panelClear })
            {
                if (b == panelClear && settings.DetailPanel != AppSettings.PanelLog) continue;
                int bw = b.PreferredWidth(); rx -= bw;
                b.SetBounds(rx, (panelHead.Height - 32) / 2, bw, 32); rx -= 6;
            }
        }

        int ContentHeight { get { return topArea.Height + (panelOpen ? PanelHeight : 0); } }

        // The window is exactly as tall as its content (RemoteAccessHub does the same); only the width can change.
        void ApplyWindowHeight()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            int frameW = Width - ClientSize.Width, frameH = Height - ClientSize.Height, windowHeight = ContentHeight + frameH;
            SuspendLayout();
            MaximumSize = Size.Empty;
            MinimumSize = new Size(MinWidth + frameW, windowHeight);
            ClientSize = new Size(ClientSize.Width, ContentHeight);
            MaximumSize = new Size(short.MaxValue, windowHeight);
            ResumeLayout(true);
        }

        void ApplyPanel(string panel, bool save)
        {
            if (panel != AppSettings.PanelInfo && panel != AppSettings.PanelLog) panel = AppSettings.PanelNone;
            settings.DetailPanel = panel; panelOpen = panel != AppSettings.PanelNone;
            panelArea.Visible = panelOpen;
            details.Visible = panel == AppSettings.PanelInfo; log.Visible = panel == AppSettings.PanelLog;
            panelTitle.Text = panel == AppSettings.PanelLog ? "활동 기록" : "모니터 정보";
            tips.SetToolTip(panelCopy, panel == AppSettings.PanelLog ? "활동 기록 전체를 복사합니다." : "선택한 항목을, 선택이 없으면 전체를 복사합니다. (Ctrl+C)");
            infoButton.Checked = panel == AppSettings.PanelInfo; logButton.Checked = panel == AppSettings.PanelLog;
            LayoutPanelHead(); ApplyWindowHeight();
            if (save) SaveSettings();
        }

        void TogglePanel(string panel) { ApplyPanel(settings.DetailPanel == panel ? AppSettings.PanelNone : panel, true); }

        // ================================================================ menus and events

        void BuildMenus()
        {
            foreach (ContextMenuStrip menu in new[] { moreMenu, autoMenu, trayMenu }) { menu.Renderer = new ThemedMenuRenderer(); menu.Font = Theme.UiFont(9.5f); }

            var themeMenu = new ToolStripMenuItem("테마");
            themeMenu.DropDown.Renderer = new ThemedMenuRenderer();
            themeMenu.DropDownItems.AddRange(new ToolStripItem[] { themeSystem, themeDark, themeLight });
            themeSystem.Click += (s, e) => SetThemeMode(ThemeMode.System, true);
            themeDark.Click += (s, e) => SetThemeMode(ThemeMode.Dark, true);
            themeLight.Click += (s, e) => SetThemeMode(ThemeMode.Light, true);
            moreMenu.Items.AddRange(new ToolStripItem[]
            {
                MenuItem("모니터 목록 새로고침", "Ctrl+R", () => { if (refresh.Enabled) Run(RefreshMonitors); }),
                MenuItem("모니터 정보", "Ctrl+I", () => TogglePanel(AppSettings.PanelInfo)),
                MenuItem("활동 기록", "Ctrl+L", () => TogglePanel(AppSettings.PanelLog)),
                new ToolStripSeparator(), themeMenu,
                MenuItem("설정...", "Ctrl+,", OpenSettings),
                MenuItem("도움말", "F1", ShowHelp),
                new ToolStripSeparator(),
                MenuItem("종료", "", ExitApplication),
            });
            moreMenu.Opening += (s, e) => { themeSystem.Checked = Theme.Mode == ThemeMode.System; themeDark.Checked = Theme.Mode == ThemeMode.Dark; themeLight.Checked = Theme.Mode == ThemeMode.Light; };

            foreach (int seconds in new[] { 1, 2, 3, 5, 10, 30, 60 })
            {
                int value = seconds;
                var item = new ToolStripMenuItem(Interval(seconds)) { Tag = seconds };
                item.Click += (s, e) => { SetRefreshSeconds(value); SetAutoRefresh(true); };
                autoMenu.Items.Add(item);
            }
            autoOff.Click += (s, e) => SetAutoRefresh(false);
            autoMenu.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), MenuItem("간격 직접 설정...", "", OpenSettings), autoOff });
            autoMenu.Opening += (s, e) =>
            {
                foreach (ToolStripItem item in autoMenu.Items) { var menuItem = item as ToolStripMenuItem; if (menuItem != null && menuItem.Tag is int) menuItem.Checked = settings.AutoRefresh && (int)menuItem.Tag == settings.RefreshSeconds; }
                autoOff.Enabled = settings.AutoRefresh;
            };

            trayOpen.Font = Theme.UiFont(9.5f, FontStyle.Bold);
            trayMenu.Items.AddRange(new ToolStripItem[] { trayOpen, new ToolStripSeparator(), trayTarget, trayOn, trayOff, trayQuery, new ToolStripSeparator(), trayAuto, trayKeep, trayExit });
            trayMenu.Opening += (s, e) => SyncTray();
            tray.Icon = new Icon(Icon, SystemInformation.SmallIconSize); tray.Text = "Monitor Power"; tray.ContextMenuStrip = trayMenu; tray.Visible = true;
        }

        static ToolStripMenuItem MenuItem(string text, string shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
            item.Click += (s, e) => action();
            return item;
        }

        void ShowMenuBelow(ContextMenuStrip menu, Control anchor, bool alignRight)
        {
            menu.Show(anchor, new Point(alignRight ? anchor.Width - menu.PreferredSize.Width : 0, anchor.Height + 2));
        }

        void HookEvents()
        {
            refresh.Click += (s, e) => Run(RefreshMonitors);
            query.Click += (s, e) => Run(ReadState);
            on.Click += (s, e) => Run(() => SetPower(true));
            off.Click += (s, e) => Run(() => SetPower(false));
            cancel.Click += (s, e) => CancelPowerOn();
            autoButton.Click += (s, e) => SetAutoRefresh(!settings.AutoRefresh);
            autoButton.DropDownClick += (s, e) => ShowMenuBelow(autoMenu, autoButton, false);
            infoButton.Click += (s, e) => TogglePanel(AppSettings.PanelInfo);
            logButton.Click += (s, e) => TogglePanel(AppSettings.PanelLog);
            settingsButton.Click += (s, e) => OpenSettings();
            moreButton.Click += (s, e) => ShowMenuBelow(moreMenu, moreButton, true);
            powerPill.Click += (s, e) => { if (query.Enabled) Run(ReadState); };
            panelCopy.Click += (s, e) =>
            {
                if (settings.DetailPanel == AppSettings.PanelLog) CopyText(log.Text, "활동 기록을 클립보드에 복사했습니다.");
                else CopyDetails(details.SelectedItems.Count > 0 ? (IEnumerable)details.SelectedItems : details.Items);
            };
            panelClear.Click += (s, e) => { log.Clear(); Say("활동 기록을 지웠습니다.", BannerKind.Info); };

            monitors.PaintItem += PaintMonitor;
            monitors.SelectedIndexChanged += (s, e) => { ShowSelected(); Remember(); if (settings.AutoRefresh && !busy) PollTick(); };
            details.DrawItem += (s, e) => { };
            details.DrawSubItem += DrawDetail;
            details.SizeChanged += (s, e) => { if (details.Columns.Count == 2) details.Columns[1].Width = Math.Max(200, details.ClientSize.Width - 156); };
            details.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { if (details.SelectedItems.Count > 0) CopyDetails(details.SelectedItems); e.SuppressKeyPress = true; } };

            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.BalloonTipClicked += (s, e) => ShowWindow();
            trayOpen.Click += (s, e) => ShowWindow();
            trayOn.Click += (s, e) => Run(() => SetPower(true));
            trayOff.Click += (s, e) => Run(() => SetPower(false));
            trayQuery.Click += (s, e) => Run(ReadState);
            trayExit.Click += (s, e) => ExitApplication();
            trayAuto.CheckedChanged += (s, e) => SetAutoRefresh(trayAuto.Checked);
            trayKeep.CheckedChanged += (s, e) => SetKeepInTray(trayKeep.Checked);

            poller.Interval = settings.RefreshSeconds * 1000; poller.Tick += (s, e) => PollTick(); if (settings.AutoRefresh) poller.Start();
            Shown += (s, e) => Run(RefreshMonitors);
            HandleCreated += (s, e) => Theme.ApplyTitleBar(this);
            Theme.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            tips.SetToolTip(on, "선택한 모니터에 전원 켜기 신호를 보냅니다.\r\n켜짐이 확인될 때까지 0.3초마다 상태를 확인하며 다시 보냅니다. (최대 횟수는 설정에서)");
            tips.SetToolTip(off, "선택한 모니터에 전원 끄기 신호를 보냅니다.\r\n꺼진 뒤에는 연결이 끊겨 다시 켜기가 실패할 수 있습니다.");
            tips.SetToolTip(query, "모니터가 알려 주는 현재 전원 상태를 확인합니다. (F5)");
            tips.SetToolTip(autoButton, "누르면 상태 자동 새로고침을 켜거나 끕니다. 오른쪽 화살표로 간격을 고릅니다.\r\n버튼을 잠그지 않고 뒤에서 확인하며, 상태가 바뀔 때만 기록합니다.");
            tips.SetToolTip(cancel, "전원 켜기 재시도를 멈춥니다. (Esc)");
            tips.SetToolTip(refresh, "연결된 모니터를 다시 찾습니다. (Ctrl+R)");
            tips.SetToolTip(infoButton, "모니터 정보를 보이거나 숨깁니다. (Ctrl+I)");
            tips.SetToolTip(logButton, "활동 기록을 보이거나 숨깁니다. (Ctrl+L)");
            tips.SetToolTip(settingsButton, "설정 (Ctrl+,)");
            tips.SetToolTip(moreButton, "목록 새로고침 · 테마 · 도움말 · 종료");
            tips.SetToolTip(powerPill, "전원 상태 (눌러서 지금 확인)");

            Disposed += (s, e) =>
            {
                Theme.Changed -= OnThemeChanged; SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                poller.Dispose(); tray.Visible = false; tray.Dispose(); moreMenu.Dispose(); autoMenu.Dispose(); trayMenu.Dispose(); tips.Dispose();
            };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.F5: if (query.Enabled) Run(ReadState); return true;
                case Keys.Control | Keys.R: case Keys.Control | Keys.F5: if (refresh.Enabled) Run(RefreshMonitors); return true;
                case Keys.Control | Keys.I: TogglePanel(AppSettings.PanelInfo); return true;
                case Keys.Control | Keys.L: TogglePanel(AppSettings.PanelLog); return true;
                case Keys.Control | Keys.Oemcomma: OpenSettings(); return true;
                case Keys.F1: ShowHelp(); return true;
                case Keys.Escape: if (powerOn != null) { CancelPowerOn(); return true; } break;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void WndProc(ref Message m) { if (ShowMessage != 0 && m.Msg == ShowMessage) { ShowWindow(); return; } base.WndProc(ref m); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && settings.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide();
                if (!trayNoticeShown) { trayNoticeShown = true; tray.ShowBalloonTip(4000, "Monitor Power", "알림 영역에서 계속 실행 중입니다. 아이콘을 두 번 클릭하면 창이 열립니다.", ToolTipIcon.Info); }
                return;
            }
            base.OnFormClosing(e);
        }

        void ShowWindow() { if (IsDisposed) return; Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); }
        void ExitApplication() { exiting = true; Close(); }
        async void Run(Func<Task> action) { await Execute(action); }

        // ================================================================ theme

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color) return;
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(Theme.RefreshFromSystem));
        }

        void OnThemeChanged() { if (IsDisposed) return; if (InvokeRequired) BeginInvoke(new Action(ApplyTheme)); else ApplyTheme(); }

        void SetThemeMode(ThemeMode mode, bool save)
        {
            settings.ThemeName = Theme.ToSetting(mode);
            Theme.Apply(mode); ApplyTheme(); // apply once even when the palette did not change
            if (save) SaveSettings();
        }

        void ApplyTheme()
        {
            Palette p = Theme.Current;
            BackColor = p.Background; ForeColor = p.Text;
            foreach (Control c in new Control[] { topArea, header, actions, panelArea }) c.BackColor = p.Background;
            card.ApplyTheme(); panelCard.ApplyTheme(); monitors.ApplyTheme();
            title.ForeColor = state.ForeColor = p.Text;
            stateDetail.ForeColor = pickerCaption.ForeColor = p.SubText;
            panelTitle.ForeColor = p.Accent;
            details.BackColor = p.Surface; details.ForeColor = p.Text;
            log.BackColor = p.LogBackground; log.ForeColor = p.LogText;
            foreach (ContextMenuStrip menu in new[] { moreMenu, autoMenu, trayMenu }) menu.BackColor = p.Surface;
            Theme.ApplyTitleBar(this);
            ShowState(state.Text, tone, stateDetail.Text, state.AccessibleName, false);
            UpdateSessionPill();
            Invalidate(true);
        }

        // ================================================================ state

        static Color ToneColor(Tone value)
        {
            Palette p = Theme.Current;
            switch (value)
            {
                case Tone.On: return p.Success;
                case Tone.Off: return p.SubText;
                case Tone.Busy: case Tone.Pending: return p.Info;
                case Tone.Warn: return p.Warning;
                case Tone.Fail: return p.Danger;
                default: return p.Muted;
            }
        }
        static string ToneGlyph(Tone value)
        {
            switch (value)
            {
                case Tone.On: return Theme.Glyph.Power;
                case Tone.Off: return Theme.Glyph.Moon;
                case Tone.Busy: return Theme.Glyph.Sync;
                case Tone.Pending: return Theme.Glyph.Info;
                case Tone.Warn: return Theme.Glyph.Warning;
                case Tone.Fail: return Theme.Glyph.Error;
                default: return Theme.Glyph.Monitor;
            }
        }
        static BannerKind ToneBanner(Tone value)
        {
            switch (value)
            {
                case Tone.On: return BannerKind.Success;
                case Tone.Busy: return BannerKind.Progress;
                case Tone.Warn: return BannerKind.Warning;
                case Tone.Fail: return BannerKind.Error;
                default: return BannerKind.Info;
            }
        }

        string DescribeTarget()
        {
            MonitorInfo m = Selected;
            return m == null ? "선택된 모니터 없음" : m.DisplayName + "  ·  " + m.Location + (m.Available ? "" : "  ·  목록에 없음");
        }

        // Card, header pill and tray tooltip. The live region reads the state label's name; a repeated title is announced again only when asked.
        void ShowState(string title, Tone value, string detail, string spoken, bool announceRepeat)
        {
            if (IsDisposed) return;
            tone = value;
            bool same = state.Text == title;
            state.AccessibleName = spoken ?? title;
            state.Text = title;
            stateDetail.Text = detail ?? DescribeTarget();
            badge.Set(ToneColor(value), ToneGlyph(value), value == Tone.On); badge.AccessibleName = title;
            string pill = !same || powerPill.Text.Length == 0 ? (value == Tone.On || value == Tone.Off ? title + " · " + DateTime.Now.ToString("HH:mm") : title) : powerPill.Text;
            powerPill.Set(pill, ToneColor(value), Theme.Glyph.Power); LayoutHeader();
            tray.Text = Fit("Monitor Power · " + title, 63);
            if (same && announceRepeat) state.AccessibilityObject.RaiseLiveRegionChanged();
        }

        void SetStatus(string title, string message, Tone value, bool announceRepeat = true, string detail = null, BannerKind? kind = null)
        {
            if (IsDisposed) return;
            string text = message.Replace("\r\n", " ");
            ShowState(title, value, detail, title + ". " + text, announceRepeat);
            Say(text, kind ?? ToneBanner(value));
        }

        void Say(string message, BannerKind kind) { if (!IsDisposed) banner.Set(message.Replace("\r\n", " "), kind); }

        void UpdateSessionPill()
        {
            Palette p = Theme.Current;
            sessionPill.Set(remote ? "원격 세션 · 제어 제한" : "물리 세션", remote ? p.Warning : p.Success, remote ? Theme.Glyph.Remote : Theme.Glyph.Monitor);
            tips.SetToolTip(sessionPill, remote ? "Windows 원격 데스크톱에서는 물리 모니터를 제어할 수 없습니다." : "이 PC에서 직접 실행 중이라 모니터를 제어할 수 있습니다.");
            LayoutHeader();
        }

        // Emphasise the action the last known state makes most likely; button positions never change.
        void Suggest(bool turnOff) { on.Primary = !turnOff; off.Primary = turnOff; }

        void UpdateButtons()
        {
            refresh.Enabled = !busy; monitors.Enabled = !busy;
            query.Enabled = on.Enabled = off.Enabled = CanControl;
            cancel.Visible = powerOn != null; cancel.Enabled = powerOn != null && !powerOn.IsCancellationRequested;
            autoButton.Checked = settings.AutoRefresh;
            autoButton.Text = settings.AutoRefresh ? "자동 " + IntervalShort(settings.RefreshSeconds) : "자동 새로고침";
            autoButton.AccessibleName = "상태 자동 새로고침 " + (settings.AutoRefresh ? "켜짐, " + Interval(settings.RefreshSeconds) : "꺼짐");
            powerPill.Cursor = query.Enabled ? Cursors.Hand : Cursors.Default;
            LayoutActions(); SyncTray();
        }

        void Notify(string title, string text, bool warning)
        {
            if (IsDisposed || !tray.Visible || (Visible && WindowState != FormWindowState.Minimized)) return;
            tray.ShowBalloonTip(4000, title, String.IsNullOrWhiteSpace(text) ? title : text.Replace("\r\n", " "), warning ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        void SyncTray()
        {
            MonitorInfo m = Selected; trayTarget.Text = m == null ? "선택된 모니터 없음" : m.DisplayName + "  ·  " + state.Text;
            trayOn.Enabled = trayOff.Enabled = trayQuery.Enabled = CanControl; trayKeep.Checked = settings.CloseToTray;
            trayAuto.Checked = settings.AutoRefresh; trayAuto.Text = "상태 자동 새로고침 (" + Interval(settings.RefreshSeconds) + ")";
        }

        static string Interval(int seconds) { return seconds >= 60 && seconds % 60 == 0 ? (seconds / 60) + "분마다" : seconds + "초마다"; }
        static string IntervalShort(int seconds) { return seconds >= 60 && seconds % 60 == 0 ? (seconds / 60) + "분" : seconds + "초"; }
        static string Fit(string text, int max) { return text.Length <= max ? text : text.Substring(0, max - 1) + "…"; }

        // ================================================================ settings

        void SetKeepInTray(bool value)
        {
            bool changed = settings.CloseToTray != value; settings.CloseToTray = value; trayKeep.Checked = value; if (!changed) return;
            SaveSettings(); Say(value ? "창을 닫아도 알림 영역에서 계속 실행합니다." : "창을 닫으면 프로그램을 종료합니다.", BannerKind.Info);
        }

        void SetAutoRefresh(bool value)
        {
            bool changed = settings.AutoRefresh != value; settings.AutoRefresh = value; trayAuto.Checked = value; if (!changed) return;
            SaveSettings(); UpdateButtons();
            Say(value ? "상태를 " + Interval(settings.RefreshSeconds) + " 자동으로 확인합니다. 상태가 바뀔 때만 기록합니다." : "상태 자동 새로고침을 껐습니다.", BannerKind.Info);
            if (value) PollTick(); else poller.Stop();
        }

        void SetRefreshSeconds(int seconds)
        {
            seconds = AppSettings.ClampSeconds(seconds); if (settings.RefreshSeconds == seconds) return;
            settings.RefreshSeconds = seconds; poller.Interval = seconds * 1000; SaveSettings(); UpdateButtons();
        }

        void SetPowerOnAttempts(int attempts)
        {
            attempts = AppSettings.ClampAttempts(attempts); if (settings.PowerOnAttempts == attempts) return;
            settings.PowerOnAttempts = attempts; SaveSettings();
        }

        void OpenSettings()
        {
            using (var dialog = new SettingsForm(settings))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SetThemeMode(dialog.SelectedTheme, true); SetRefreshSeconds(dialog.RefreshSeconds); SetAutoRefresh(dialog.AutoRefresh);
                SetPowerOnAttempts(dialog.PowerOnAttempts); SetKeepInTray(dialog.CloseToTray);
                Say("설정을 저장했습니다.", BannerKind.Success);
            }
        }

        void ShowHelp() { using (var help = new HelpForm(settingsPath, settings.PowerOnAttempts)) help.ShowDialog(this); }

        void SaveSettings() { if (!settings.Save(settingsPath)) Append("설정을 저장하지 못했습니다: " + settingsPath); }

        void Remember()
        {
            MonitorInfo m = Selected, saved = settings.Monitor;
            if (m == null || (saved != null && saved.Target == m.Target && saved.Id == m.Id && saved.Name == m.Name)) return;
            settings.Monitor = m.Copy(); SaveSettings();
        }

        // ================================================================ info, log, clipboard

        void Append(string message) { if (!IsDisposed) log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }

        void AddDetail(string label, string value) { details.Items.Add(new ListViewItem(new[] { label, value }) { ToolTipText = value }); }

        void DrawDetail(object sender, DrawListViewSubItemEventArgs e)
        {
            Palette p = Theme.Current;
            bool selected = e.Item.Selected;
            using (var brush = new SolidBrush(selected ? Theme.Blend(p.Surface, p.Accent, 0.22) : p.Surface)) e.Graphics.FillRectangle(brush, e.Bounds);
            Draw.Text(e.Graphics, e.SubItem.Text, details.Font, new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), e.ColumnIndex == 0 ? p.SubText : p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Each monitor reads as its name, then the port in muted text, then a warning-coloured flag when it is no longer listed.
        void PaintMonitor(object sender, DrawItemEventArgs e)
        {
            Palette p = Theme.Current;
            var m = monitors.Items[e.Index] as MonitorInfo; if (m == null) return;
            bool face = (e.State & DrawItemState.ComboBoxEdit) != 0, disabled = (e.State & DrawItemState.Disabled) != 0;
            int x = e.Bounds.X + (face ? 6 : 10);
            x += Segment(e.Graphics, m.DisplayName, x, e.Bounds, disabled ? p.Muted : p.Text) + 12;
            x += Segment(e.Graphics, m.Location, x, e.Bounds, disabled ? p.Muted : p.SubText) + 12;
            if (!m.Available) Segment(e.Graphics, "연결 확인 필요", x, e.Bounds, disabled ? p.Muted : p.Warning);
        }

        int Segment(Graphics g, string text, int x, Rectangle bounds, Color color)
        {
            // Measured without EndEllipsis: with it, an unconstrained measure comes back clipped.
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            if (String.IsNullOrEmpty(text) || x >= bounds.Right - 4) return 0;
            Draw.Text(g, text, monitors.Font, new Rectangle(x, bounds.Y, bounds.Right - 4 - x, bounds.Height), color, flags | TextFormatFlags.EndEllipsis);
            return TextRenderer.MeasureText(g, text, monitors.Font, Size.Empty, flags | TextFormatFlags.NoPadding).Width;
        }

        void CopyDetails(IEnumerable rows)
        {
            var lines = new List<string>(); foreach (ListViewItem row in rows) lines.Add(row.Text + ": " + row.SubItems[1].Text);
            CopyText(String.Join(Environment.NewLine, lines.ToArray()), "모니터 정보를 클립보드에 복사했습니다.");
        }

        void CopyText(string text, string done)
        {
            if (String.IsNullOrEmpty(text)) { Say("복사할 내용이 없습니다.", BannerKind.Info); return; }
            try { Clipboard.SetText(text); Say(done, BannerKind.Success); }
            catch (ExternalException) { Say("클립보드를 사용할 수 없습니다. 잠시 후 다시 시도해 주세요.", BannerKind.Warning); }
        }

        // ================================================================ monitor commands

        void ShowSelected()
        {
            MonitorInfo m = Selected; lastCode = int.MinValue;
            details.Items.Clear();
            if (m != null) { AddDetail("모니터 이름", m.Name); AddDetail("그래픽 어댑터", m.Adapter); AddDetail("장치 이름", m.Device); AddDetail("모니터 ID", m.Id); AddDetail("짧은 모니터 ID", m.ShortId); }
            remote = SystemInformation.TerminalServerSession || (m != null && m.Remote); UpdateSessionPill();
            Suggest(false);
            if (!File.Exists(tool)) SetStatus("연동 도구 필요", "ControlMyMonitor.exe를 프로그램 폴더에 넣은 뒤 목록을 새로고침해 주세요.", Tone.Fail);
            else if (remote) SetStatus("제어할 수 없음", "Windows 원격 데스크톱에서는 물리 모니터를 제어할 수 없습니다. Chrome 원격 데스크톱 또는 PC에서 직접 실행해 주세요.", Tone.Warn);
            else if (m == null) SetStatus("모니터 없음", "모니터 전원과 케이블 연결을 확인한 뒤 목록을 새로고침해 주세요.", Tone.Neutral);
            else if (!m.Available) SetStatus("연결 확인 필요", "목록에서 사라진 모니터입니다. 전원이 꺼져 있다면 마지막 선택 대상으로 켜기를 시도할 수 있습니다.", Tone.Warn);
            else SetStatus("상태 확인 전", "상태 확인(F5)을 누르면 현재 전원 상태를 확인합니다.", Tone.Neutral);
            UpdateButtons();
        }

        async Task Execute(Func<Task> action)
        {
            if (busy) return;
            busy = true; UpdateButtons(); progress.Value = -1; progress.Visible = true; Say("처리 중…", BannerKind.Progress);
            // Show the busy state at once, but let an automatic refresh that is already talking to the monitor finish first.
            try { if (polling != null) await polling; await action(); }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                bool missing = ex is FileNotFoundException; string title = missing ? "연동 도구 필요" : "연결 확인 필요";
                SetStatus(title, ex.Message, missing ? Tone.Fail : Tone.Warn); Suggest(false); Append(ex.Message); Notify(title, ex.Message, true);
            }
            finally { busy = false; if (!IsDisposed) { progress.Visible = false; UpdateButtons(); } }
        }

        async Task RefreshMonitors()
        {
            Say("연결된 모니터를 찾는 중입니다…", BannerKind.Progress);
            // On the first load, fall back to the monitor saved last time so a monitor that dropped off the list after OFF can still be turned on.
            MonitorInfo previous = Selected ?? (loaded ? null : settings.Monitor);
            List<MonitorInfo> list = await client.List(); if (IsDisposed) return; loaded = true;
            MonitorInfo restore = null;
            if (previous != null) { restore = list.Find(m => m.Target == previous.Target && m.Id == previous.Id); if (restore == null) { previous.Available = false; list.Add(previous); restore = previous; } }
            monitors.Items.Clear(); foreach (MonitorInfo m in list) monitors.Items.Add(m);
            if (restore != null) monitors.SelectedItem = restore; else if (monitors.Items.Count > 0) monitors.SelectedIndex = 0;
            ShowSelected(); Append("모니터 목록을 새로고침했습니다. (" + list.FindAll(m => m.Available).Count + "대)");
            if (Selected != null && Selected.Available && !Selected.Remote && !SystemInformation.TerminalServerSession) await ReadState();
        }

        static string FailureNote(int code, string otherwise) { return code == 31 ? "모니터와 통신할 수 없습니다. 전원이 꺼져 있다면 모니터의 전원 버튼으로 켜 주세요. (Error 31)" : otherwise + " 오류 코드: " + code; }

        Task ReadState() { return QueryState(false); }

        // An automatic query refreshes the card and pill (the detail carries the time) but leaves the banner, log, screen reader and balloons alone unless the answer changed.
        async Task QueryState(bool automatic)
        {
            MonitorInfo target = Selected; if (target == null) return;
            if (!automatic) Say("모니터에 전원 상태를 묻는 중입니다…", BannerKind.Progress);
            CommandResult result = await client.Get(target); if (IsDisposed || Selected != target) return;
            string title; Tone value;
            switch (result.Code)
            {
                case 1: title = "전원 켜짐"; value = Tone.On; break;
                case 2: title = "대기 중"; value = Tone.Off; break;
                case 3: title = "절전 모드"; value = Tone.Off; break;
                case 4: case 5: title = "전원 꺼짐"; value = Tone.Off; break;
                default: title = "상태 확인 불가"; value = Tone.Warn; break;
            }
            bool known = value != Tone.Warn, changed = result.Code != lastCode; lastCode = result.Code;
            string detail = DescribeTarget() + "  ·  응답 " + DateTime.Now.ToString("HH:mm:ss");
            string message = known ? "모니터 응답으로 전원 상태를 확인했습니다." : FailureNote(result.Code, "모니터의 응답을 확인할 수 없습니다. 전원과 연결을 확인해 주세요.");
            Suggest(result.Code == 1);
            if (automatic && !changed) { ShowState(title, value, detail, title + ". " + detail, false); return; }
            SetStatus(title, message, value, !automatic, detail);
            Append((automatic ? "자동 상태 조회 · " : "상태 조회 · ") + MonitorClient.Quote(target.Target) + " · " + MonitorClient.PowerText(result.Code));
            if (!String.IsNullOrWhiteSpace(result.Output)) Append(result.Output.Trim());
            if (!automatic) Notify(title, target.DisplayName + "  ·  " + message, !known);
        }

        // D6=1 sets ON rather than toggling, so resending it is harmless. A monitor waking from standby often misses the first few, so ON is resent
        // after each 0.3s check until the monitor itself reports ON, up to the configured number of tries. Cancel (Esc) stops between tries.
        async Task TurnOn(MonitorInfo target)
        {
            int attempts = settings.PowerOnAttempts, attempt;
            var cts = new CancellationTokenSource(); powerOn = cts; UpdateButtons();
            try
            {
                SetStatus("전원 켜는 중", "켜질 때까지 0.3초마다 상태를 확인하며 켜기 신호를 보냅니다. 멈추려면 취소(Esc)를 누르세요.", Tone.Busy, true, "켜기 신호 1/" + attempts + "회");
                progress.Value = 0;
                CommandResult sent = null, read = null;
                for (attempt = 1; attempt <= attempts && !cts.IsCancellationRequested; attempt++)
                {
                    if (attempt > 1) ShowState("전원 켜는 중", Tone.Busy, "켜기 신호 " + attempt + "/" + attempts + "회", null, false);
                    sent = await client.Set(target, true); if (IsDisposed) return;
                    await Task.Delay(PowerOnCheckMs); if (IsDisposed) return;
                    read = await client.Get(target); if (IsDisposed) return;
                    progress.Value = (double)attempt / attempts;
                    Append("전원 켜기 " + attempt + "/" + attempts + " · " + MonitorClient.Quote(target.Target) + " · D6=1 · " + (sent.Code == 0 ? "전송됨" : MonitorClient.ErrorText(sent.Code)) + " · 상태: " + MonitorClient.PowerText(read.Code));
                    foreach (string output in new[] { sent.Output, read.Output }) if (!String.IsNullOrWhiteSpace(output)) Append(output.Trim());
                    if (read.Code != 1) continue;
                    string time = DateTime.Now.ToString("HH:mm:ss"); lastCode = 1;
                    SetStatus("전원 켜짐", (attempt > 1 ? attempt + "번째 신호에 켜졌습니다. " : "") + "모니터 응답으로 확인했습니다.", Tone.On, true, DescribeTarget() + "  ·  응답 " + time);
                    Suggest(true); Notify("전원 켜짐", target.DisplayName + "  ·  " + state.AccessibleName, false); return;
                }
                int tried = attempt - 1; lastCode = read == null ? int.MinValue : read.Code; Suggest(false);
                if (cts.IsCancellationRequested && tried < attempts)
                {
                    SetStatus("켜기 중단", "켜기를 멈췄습니다. 신호를 " + tried + "번 보냈지만 켜짐을 확인하지 못했습니다. 다시 누르거나 모니터의 전원 버튼으로 켜 주세요.", Tone.Warn);
                    Append("전원 켜기 중단 · " + tried + "/" + attempts + "번 보냄"); return;
                }
                // Never confirmed: a command that did not get through keeps the Error 31 guidance; otherwise the monitor took the signal but never reported ON.
                bool rejected = sent.Code != 0; string title = rejected ? "명령 전송 실패" : "켜짐 확인 안 됨";
                string note = rejected ? FailureNote(sent.Code, "전원과 연결을 확인한 뒤 다시 시도해 주세요.") : "신호를 " + attempts + "번 보냈지만 모니터가 켜짐을 알리지 않았습니다. 모니터의 전원 버튼으로 켜 주세요.";
                SetStatus(title, note, Tone.Fail); Notify(title, target.DisplayName + "  ·  " + note, true);
            }
            finally { if (powerOn == cts) powerOn = null; cts.Dispose(); if (!IsDisposed) UpdateButtons(); }
        }

        void CancelPowerOn()
        {
            if (powerOn == null || powerOn.IsCancellationRequested) return;
            powerOn.Cancel(); cancel.Enabled = false; Say("켜기를 멈추는 중입니다. 지금 보낸 신호의 응답만 확인하고 멈춥니다.", BannerKind.Progress);
        }

        async Task SetPower(bool turnOn)
        {
            MonitorInfo target = Selected; if (target == null) return;
            if (turnOn) { await TurnOn(target); return; }
            SetStatus("전원 끄는 중", "선택한 모니터에 끄기 신호를 보내고 있습니다.", Tone.Busy);
            CommandResult result = await client.Set(target, false); if (IsDisposed) return;
            Append("전원 끄기 · " + MonitorClient.Quote(target.Target) + " · D6=4 · " + MonitorClient.ErrorText(result.Code)); if (!String.IsNullOrWhiteSpace(result.Output)) Append(result.Output.Trim());
            if (result.Code != 0)
            {
                string note = FailureNote(result.Code, "전원과 연결을 확인한 뒤 다시 시도해 주세요."); SetStatus("명령 전송 실패", note, Tone.Fail); Suggest(true);
                Notify("명령 전송 실패", target.DisplayName + "  ·  " + note, true); return;
            }
            SetStatus("끄기 요청 완료", "끄기 신호를 보냈습니다. 실제 전원 상태는 아직 확인되지 않았습니다. 꺼진 뒤에는 연결이 끊겨 켜기가 실패할 수 있으니, 그때는 모니터의 전원 버튼을 눌러 주세요.",
                Tone.Pending, true, null, BannerKind.Warning);
            Suggest(false); Notify("끄기 요청 완료", target.DisplayName + "  ·  실제 전원 상태는 아직 확인되지 않았습니다.", false);
        }

        // Automatic refresh stays in the background: no busy lock, progress bar or balloon, and it logs and announces only when the reported state changes.
        // Like the startup check it only asks monitors that are listed and controllable. The next tick is scheduled after the answer arrives.
        async void PollTick()
        {
            poller.Stop();
            if (CanControl && polling == null && Selected.Available) { polling = Poll(); await polling; polling = null; }
            if (!IsDisposed && settings.AutoRefresh) { poller.Interval = settings.RefreshSeconds * 1000; poller.Start(); }
        }

        async Task Poll()
        {
            try { await QueryState(true); }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                bool repeat = lastCode == int.MinValue && banner.Text == ex.Message; lastCode = int.MinValue;
                if (repeat) { ShowState("연결 확인 필요", Tone.Warn, null, null, false); return; }
                SetStatus("연결 확인 필요", ex.Message, Tone.Warn, false); Suggest(false); Append(ex.Message);
            }
        }
    }
}
