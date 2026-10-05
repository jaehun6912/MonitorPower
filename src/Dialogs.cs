using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace MonitorPower
{
    // Shared look for the dialogs: background, title and subtitle at the top, buttons at the bottom right.
    abstract class ThemedDialog : Form
    {
        protected const int Gutter = 20;
        protected ThemedDialog(string caption, string heading, string subtitle, Size client)
        {
            Palette p = Theme.Current;
            Text = caption; Font = Theme.UiFont(9.5f); BackColor = p.Background; ForeColor = p.Text;
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent; ClientSize = client; KeyPreview = true;
            Controls.Add(new Label { Text = heading, Font = Theme.UiFont(14f, FontStyle.Bold), AutoSize = true, Location = new Point(Gutter, 14), ForeColor = p.Text });
            Controls.Add(new Label { Text = subtitle, AutoSize = true, Location = new Point(Gutter + 2, 46), ForeColor = p.SubText });
            HandleCreated += (s, e) => Theme.ApplyTitleBar(this);
        }

        // Places buttons right to left along the bottom edge; the first one is the rightmost.
        protected void PlaceButtons(params FlatButton[] buttons)
        {
            int x = ClientSize.Width - Gutter;
            foreach (FlatButton b in buttons)
            {
                b.Size = new Size(Math.Max(96, b.PreferredWidth()), 36);
                x -= b.Width; b.Location = new Point(x, ClientSize.Height - 14 - b.Height); x -= 8;
                Controls.Add(b);
            }
        }

        protected static Label Hint(string text, int x, int y, int width)
        {
            return new Label { Text = text, Font = Theme.UiFont(8.5f), ForeColor = Theme.Current.SubText, Location = new Point(x, y), Size = new Size(width, 34) };
        }
    }

    // Settings in RemoteAccessHub's form: one card per topic with an accent heading.
    sealed class SettingsForm : ThemedDialog
    {
        readonly ThemedComboBox theme = new ThemedComboBox { Width = 220, AccessibleName = "테마" };
        readonly CheckBox autoRefresh = new CheckBox { Text = "상태 자동 새로고침", AutoSize = true };
        readonly NumericUpDown seconds = new NumericUpDown { Minimum = AppSettings.MinRefreshSeconds, Maximum = AppSettings.MaxRefreshSeconds, Width = 84, TextAlign = HorizontalAlignment.Right, AccessibleName = "자동 새로고침 간격(초)" };
        readonly NumericUpDown attempts = new NumericUpDown { Minimum = AppSettings.MinPowerOnAttempts, Maximum = AppSettings.MaxPowerOnAttempts, Width = 84, TextAlign = HorizontalAlignment.Right, AccessibleName = "전원 켜기 최대 시도 횟수" };
        readonly CheckBox closeToTray = new CheckBox { Text = "닫아도 알림 영역에서 실행", AutoSize = true };
        int y = 80;

        public SettingsForm(AppSettings settings) : base("설정", "설정", "바꾼 값은 저장을 누르면 바로 적용되고 다음 실행에도 유지됩니다.", new Size(560, 676))
        {
            Palette p = Theme.Current;
            theme.Items.AddRange(new object[] { "시스템 설정 따르기", "어둡게", "밝게" });
            ThemeMode mode = Theme.ParseMode(settings.ThemeName);
            theme.SelectedIndex = mode == ThemeMode.Dark ? 1 : mode == ThemeMode.Light ? 2 : 0;
            autoRefresh.Checked = settings.AutoRefresh; seconds.Value = AppSettings.ClampSeconds(settings.RefreshSeconds);
            attempts.Value = AppSettings.ClampAttempts(settings.PowerOnAttempts); closeToTray.Checked = settings.CloseToTray;
            foreach (NumericUpDown n in new[] { seconds, attempts }) { n.BackColor = p.SurfaceAlt; n.ForeColor = p.Text; n.BorderStyle = BorderStyle.FixedSingle; }
            foreach (CheckBox c in new[] { autoRefresh, closeToTray }) { c.ForeColor = p.Text; c.BackColor = Color.Transparent; c.Cursor = Cursors.Hand; }

            CardPanel look = Section("화면", 92);
            look.Controls.Add(RowLabel("테마", 16, 50)); theme.Location = new Point(110, 44); look.Controls.Add(theme);

            CardPanel status = Section("상태 확인", 152);
            autoRefresh.Location = new Point(16, 44); status.Controls.Add(autoRefresh);
            status.Controls.Add(RowLabel("간격", 16, 82)); seconds.Location = new Point(110, 78); status.Controls.Add(seconds); status.Controls.Add(RowLabel("초마다", 200, 82));
            status.Controls.Add(Hint("1~3600초. 버튼을 잠그지 않고 뒤에서 확인하며, 상태가 바뀔 때만 활동 기록에 남깁니다. 목록에 있는 모니터만 확인합니다.", 16, 108, status.Width - 32));

            CardPanel power = Section("전원 켜기", 120);
            power.Controls.Add(RowLabel("최대 시도", 16, 50)); attempts.Location = new Point(110, 46); power.Controls.Add(attempts); power.Controls.Add(RowLabel("번", 200, 50));
            power.Controls.Add(Hint("켜기 신호를 보낸 뒤 0.3초마다 상태를 확인하고, 모니터가 켜짐을 알릴 때까지 다시 보냅니다. 켜짐이 확인되면 바로 멈춥니다.", 16, 76, power.Width - 32));

            CardPanel window = Section("창", 116);
            closeToTray.Location = new Point(16, 44); window.Controls.Add(closeToTray);
            window.Controls.Add(Hint("켜 두면 X 버튼이 프로그램을 끝내지 않고 알림 영역으로 숨깁니다. 완전히 끝내려면 ⋯ 메뉴나 알림 영역 아이콘 메뉴의 ‘종료’를 누르세요.", 16, 72, window.Width - 32));

            var save = new FlatButton { Text = "저장", Glyph = Theme.Glyph.Check, Variant = ButtonVariant.Primary, DialogResult = DialogResult.OK };
            var close = new FlatButton { Text = "취소", Variant = ButtonVariant.Secondary, DialogResult = DialogResult.Cancel };
            PlaceButtons(save, close); AcceptButton = save; CancelButton = close;
        }

        public ThemeMode SelectedTheme { get { return theme.SelectedIndex == 1 ? ThemeMode.Dark : theme.SelectedIndex == 2 ? ThemeMode.Light : ThemeMode.System; } }
        public bool AutoRefresh { get { return autoRefresh.Checked; } }
        public int RefreshSeconds { get { return (int)seconds.Value; } }
        public int PowerOnAttempts { get { return (int)attempts.Value; } }
        public bool CloseToTray { get { return closeToTray.Checked; } }

        CardPanel Section(string heading, int height)
        {
            var card = new CardPanel { Location = new Point(Gutter, y), Size = new Size(ClientSize.Width - Gutter * 2, height) };
            card.Controls.Add(new Label { Text = heading, Font = Theme.UiFont(10.5f, FontStyle.Bold), AutoSize = true, Location = new Point(16, 12), ForeColor = Theme.Current.Accent });
            Controls.Add(card); y += height + 12;
            return card;
        }

        static Label RowLabel(string text, int x, int y) { return new Label { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = Theme.Current.Text }; }
    }

    // Help as sections with accent headings, like RemoteAccessHub's setup screen. Colours come from the current palette.
    sealed class HelpForm : ThemedDialog
    {
        public HelpForm(string settingsPath, int powerOnAttempts) : base("도움말", "도움말", "Monitor Power 사용법과 문제 해결", new Size(640, 560))
        {
            var card = new CardPanel { Location = new Point(Gutter, 80), Size = new Size(ClientSize.Width - Gutter * 2, ClientSize.Height - 80 - 64), Padding = new Padding(16, 12, 8, 12) };
            // An explicit Font stops the dialog's ambient font from flowing in later and reapplying one font over the formatted text.
            var text = new RichTextBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Current.Surface, Font = Theme.UiFont(10f), ReadOnly = true, DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical, AccessibleName = "도움말", Rtf = HelpRtf(settingsPath, powerOnAttempts) };
            card.Controls.Add(text); Controls.Add(card);
            var close = new FlatButton { Text = "닫기", Variant = ButtonVariant.Secondary, DialogResult = DialogResult.Cancel };
            PlaceButtons(close); CancelButton = close;
        }

        static string Rtf(string text)
        {
            var rtf = new StringBuilder();
            foreach (char c in text) { if (c == '\\' || c == '{' || c == '}') rtf.Append('\\').Append(c); else if (c > 127) rtf.Append("\\u").Append((int)(short)c).Append('?'); else rtf.Append(c); }
            return rtf.ToString();
        }

        static string RtfColor(Color c) { return "\\red" + c.R + "\\green" + c.G + "\\blue" + c.B + ";"; }
        static string Version() { System.Version v = typeof(HelpForm).Assembly.GetName().Version; return v.Major + "." + v.Minor; }

        static string HelpRtf(string settingsPath, int attempts)
        {
            Palette p = Theme.Current;
            var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset129 Malgun Gothic;}{\f1\fnil\fcharset2 Symbol;}}{\colortbl ;" + RtfColor(p.Text) + RtfColor(p.SubText) + RtfColor(p.Accent) + @"}\cf1\fs20");
            Action<string, bool> heading = (text, first) => rtf.Append(first ? @"\pard\sa80\cf3\b " : @"\pard\sb300\sa80\cf3\b ").Append(Rtf(text)).Append(@"\b0\cf1\par");
            // Malgun Gothic already sets a tall line, so 1.1 times it reads as roughly 1.5 em; paragraphs part more than wrapped lines do.
            Action<string> line = text => rtf.Append(@"\pard\sl264\slmult1\sa140 ").Append(Rtf(text)).Append(@"\par");
            // RichEdit maps text through the font's code page (949 has no U+2022), so bullets use RTF's own Symbol-font list marker.
            Action<string> bullet = text => rtf.Append(@"\pard{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent320{\pntxtb\'B7}}\li320\fi-320\sl264\slmult1\sa140 ").Append(Rtf(text)).Append(@"\par");
            heading("빠르게 사용하기", true);
            bullet("전원 켜기 · 전원 끄기 · 상태 확인(F5): 위에서 고른 모니터에 명령을 보냅니다. 마지막으로 확인한 상태에 따라 다음에 누를 가능성이 높은 버튼이 초록색으로 강조됩니다.");
            bullet("전원 켜기는 모니터가 켜짐을 알릴 때까지 0.3초마다 상태를 확인하며 켜기 신호를 최대 " + attempts + "번 보냅니다. 진행 중에는 취소(Esc)로 멈출 수 있습니다. 횟수는 설정(Ctrl+,)에서 바꿉니다.");
            bullet("자동 새로고침: 누르면 켜고 끕니다. 오른쪽 화살표로 간격을 고릅니다. 버튼을 잠그지 않고 뒤에서 확인하며, 상태가 바뀔 때만 기록합니다.");
            bullet("머리글의 전원 배지를 누르면 상태를 바로 확인합니다. 정보(Ctrl+I)와 기록(Ctrl+L)은 창 아래에 펼쳐집니다.");
            bullet("Ctrl+R: 목록 새로고침   ·   Ctrl+,: 설정   ·   F1: 도움말   ·   모니터 정보에서 Ctrl+C: 선택 항목 복사");
            bullet("작업 표시줄 알림 영역의 아이콘을 오른쪽 클릭하면 창을 열지 않고 켜기·끄기·상태 확인을 할 수 있습니다. 마지막으로 선택한 모니터는 다음 실행에도 기억합니다.");
            heading("원격으로 사용할 때", false);
            line("Chrome 원격 데스크톱에서 사용하세요. Windows 원격 데스크톱(RDP)에서는 물리 모니터를 제어할 수 없어 전원 버튼이 비활성화됩니다.");
            heading("전원이 다시 켜지지 않을 때", false);
            line("OFF 후 DDC/CI 연결이 끊기면 Error 31이 발생할 수 있습니다. 모니터의 전원 버튼으로 켠 뒤 목록을 새로고침하세요.");
            heading("ControlMyMonitor가 없을 때", false);
            line("ControlMyMonitor.exe를 MonitorPower.exe와 같은 폴더에 넣고 목록을 새로고침하세요.");
            heading("전원 설정", false);
            line("현재 LG 환경에 맞춰 켜기 D6=1, 끄기 D6=4를 사용합니다. 실제 상태는 모니터 응답으로 확인하며, 명령 전송만으로 전원 상태를 확정하지 않습니다.");
            rtf.Append(@"\pard\sb260\cf2\fs18 ").Append(Rtf("설정 파일: " + settingsPath)).Append(@"\line ").Append(Rtf("Monitor Power " + Version() + "  ·  ControlMyMonitor 연동")).Append(@"\par}");
            return rtf.ToString();
        }
    }
}
