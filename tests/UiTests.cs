using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MonitorPower;
class UiTests
{
    static int count;
    static object Field(MainForm f,string name) { return typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(f); }
    static void SetField(MainForm f,string name,object value) { typeof(MainForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(f,value); }
    static object Call(MainForm f,string name,params object[] args) { return typeof(MainForm).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(f,args); }
    static void Pump(Task task) { while(!task.IsCompleted) { Application.DoEvents(); Thread.Sleep(10); } task.GetAwaiter().GetResult(); }
    static void Idle(MainForm f) { do { Application.DoEvents(); Thread.Sleep(10); } while((bool)Field(f,"busy")); }
    static void Settle(int ms) { for(int i=0;i<ms/10;i++) { Application.DoEvents(); Thread.Sleep(10); } }
    static bool Key(MainForm f,Keys keys) { return (bool)Call(f,"ProcessCmdKey",new Message(),keys); }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out RECT value,int size);
    struct RECT { public int Left,Top,Right,Bottom; }
    // README screenshots come from the screen itself: the dark title bar, RichEdit and menus only look right there.
    // The visible frame (DWM extended frame bounds) leaves out the invisible resize border and whatever shows through it.
    static void Snap(Form f,string path)
    {
        f.TopMost=true; f.Activate(); Settle(400);
        RECT r; Rectangle box=DwmGetWindowAttribute(f.Handle,9,out r,16)==0 ? Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom) : f.Bounds;
        using(var bitmap=new Bitmap(box.Width,box.Height)) { using(var g=Graphics.FromImage(bitmap)) g.CopyFromScreen(box.Location,Point.Empty,box.Size); bitmap.Save(path); }
        f.TopMost=false;
    }
    static void Check(bool value,string name) { if(!value)throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); count++; }
    // Print failures ourselves: on this system the runtime's own unhandled-exception printer fails on the Korean text and hides which check broke.
    [STAThread] static int Main(string[] args)
    {
        // run-tests.ps1 starts each new executable once before testing; the probe also starts the mock the way the tests do.
        if (args.Length > 0 && args[0] == "--probe")
            return new MonitorClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ControlMyMonitor.exe")).Get(new MonitorInfo { Device="probe" }).GetAwaiter().GetResult().Code==1 ? 0 : 2;
        try { Run(args); return 0; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); Console.WriteLine(ex.StackTrace); return 1; }
    }
    static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        // There is no Application.Run here, so keep a UI sync context installed; otherwise DoEvents drops it and awaits resume on pool threads.
        WindowsFormsSynchronizationContext.AutoInstall=false; SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        string tool=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ControlMyMonitor.exe"), settingsFile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings","settings.ini");
        bool remoteSession=SystemInformation.TerminalServerSession;
        new AppSettings { ThemeName="dark" }.Save(settingsFile);
        using(var f=new MainForm(tool,settingsFile))
        {
            f.StartPosition=FormStartPosition.Manual; f.Location=new Point(40,40); f.Show(); Settle(1500); Idle(f);
            var combo=(ComboBox)Field(f,"monitors"); var state=(Label)Field(f,"state"); var banner=(StatusBanner)Field(f,"banner");
            var on=(FlatButton)Field(f,"on"); var off=(FlatButton)Field(f,"off"); var cancel=(FlatButton)Field(f,"cancel");
            var details=(ListView)Field(f,"details"); var log=(TextBox)Field(f,"log"); int compact=((Panel)Field(f,"topArea")).Height;
            Check(combo.Items.Count==2,"UI monitor list");
            Check(state.Text=="전원 켜짐" || remoteSession,"UI verified power state");
            Check(((StatusPill)Field(f,"powerPill")).Text.StartsWith("전원 켜짐") || remoteSession,"UI header pill shows the power state");
            Check((off.Primary && !on.Primary) || remoteSession,"UI emphasises OFF while the monitor reports ON");
            Check(details.Items.Count==5,"UI all five information fields");
            Check(!(bool)Field(f,"panelOpen") && f.ClientSize.Height==compact,"UI starts compact with the panels closed");
            Check(Theme.Current==Theme.Dark && f.BackColor==Theme.Dark.Background,"UI uses the saved dark theme");
            combo.SelectedIndex=1; Check(!on.Enabled && state.Text=="제어할 수 없음","UI remote monitor disabled with explanation");
            Check(on.FillColor!=Theme.Current.Accent && off.FillColor!=Theme.Current.Accent,"UI disabled buttons drop the accent colour");
            Check(((StatusPill)Field(f,"sessionPill")).Dot==Theme.Current.Warning,"UI session pill warns about the remote monitor");
            combo.SelectedIndex=0; Check(on.Enabled || remoteSession,"UI physical monitor controls");
            Check(File.ReadAllText(settingsFile).Contains(@"device=\\.\DISPLAY1\Monitor0"),"UI remembers the selected monitor");
            Pump((Task)Call(f,"ReadState"));
            if(args.Length>0) Snap(f,args[0]);
            Call(f,"ApplyPanel","info",true); Settle(100);
            Check((bool)Field(f,"panelOpen") && details.Visible && f.ClientSize.Height==compact+MainForm.PanelHeight && AppSettings.Load(settingsFile).DetailPanel=="info","UI info panel opens below and the choice is saved");
            if(args.Length>0) Snap(f,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),Path.GetFileNameWithoutExtension(args[0])+"-details.png"));
            Key(f,Keys.Control|Keys.L); Settle(100);
            Check(log.Visible && !details.Visible && ((FlatButton)Field(f,"logButton")).Checked && AppSettings.Load(settingsFile).DetailPanel=="log","UI Ctrl+L switches to the log panel");
            Key(f,Keys.Control|Keys.L); Settle(100);
            Check(!(bool)Field(f,"panelOpen") && f.ClientSize.Height==compact && AppSettings.Load(settingsFile).DetailPanel=="none","UI panels close back to the compact size");
            Pump((Task)Call(f,"SetPower",false)); Check(state.Text=="끄기 요청 완료" && banner.Text.Contains("아직 확인") && banner.Kind==BannerKind.Warning,"UI OFF request never asserts unverified power state");
            Check(on.Primary && !off.Primary,"UI emphasises ON after an OFF request");
            Pump((Task)Call(f,"SetPower",true)); Check(state.Text=="전원 켜짐","UI ON verifies state after command");
            // ON is resent after each 0.3s check until the monitor reports ON, and stops after the last try with power-button guidance.
            var current=(MonitorInfo)combo.SelectedItem; string device=current.Device, wakeCount=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wake-count.txt"); if(File.Exists(wakeCount))File.Delete(wakeCount);
            current.Device="wake"; Pump((Task)Call(f,"SetPower",true));
            Check(state.Text=="전원 켜짐" && File.ReadAllText(wakeCount)=="3" && banner.Text.Contains("3번째") && off.Primary,"UI ON resends until the monitor reports ON, then stops");
            current.Device="stayoff"; Pump((Task)Call(f,"SetPower",true));
            Check(state.Text=="켜짐 확인 안 됨" && banner.Text.Contains("전원 버튼") && banner.Kind==BannerKind.Error && on.Primary && log.Text.Contains("10/10") && !log.Text.Contains("11/10"),"UI ON gives up after 10 tries with guidance");
            log.Clear(); Func<Task> turnOn=()=>(Task)Call(f,"SetPower",true); var running=(Task)Call(f,"Execute",turnOn); Settle(900);
            Check(cancel.Visible && cancel.Enabled,"UI cancel appears while ON is retrying");
            Check(Key(f,Keys.Escape),"UI Esc is handled while ON is retrying"); Pump(running);
            Check(state.Text=="켜기 중단" && !log.Text.Contains("10/10") && !cancel.Visible,"UI cancel stops the ON retries");
            Call(f,"SetPowerOnAttempts",3); log.Clear(); Pump((Task)Call(f,"SetPower",true));
            Check(log.Text.Contains("3/3") && !log.Text.Contains("4/3") && banner.Text.Contains("3번") && AppSettings.Load(settingsFile).PowerOnAttempts==3,"UI power-on tries follow the setting");
            Call(f,"SetPowerOnAttempts",10); current.Device=device;
            state.Text=""; Check(Key(f,Keys.F5),"UI F5 is handled"); Idle(f); Check(state.Text=="전원 켜짐","UI F5 refreshes power state");
            int lists=log.Text.Split(new[]{"모니터 목록을 새로고침했습니다."},StringSplitOptions.None).Length;
            Check(Key(f,Keys.Control|Keys.R),"UI Ctrl+R is handled"); Idle(f); Check(log.Text.Split(new[]{"모니터 목록을 새로고침했습니다."},StringSplitOptions.None).Length==lists+1,"UI Ctrl+R refreshes the monitor list");
            // Automatic refresh: off by default, saved and mirrored in the tray, polls without locking the controls, and logs only when the answer changes.
            var autoButton=(FlatButton)Field(f,"autoButton"); var trayAuto=(ToolStripMenuItem)Field(f,"trayAuto");
            Check(!autoButton.Checked && autoButton.Text=="자동 새로고침","UI auto refresh starts off");
            Call(f,"SetRefreshSeconds",1); Call(f,"SetAutoRefresh",true); Settle(500);
            Check(autoButton.Checked && autoButton.Text=="자동 1초" && trayAuto.Checked && AppSettings.Load(settingsFile).AutoRefresh && AppSettings.Load(settingsFile).RefreshSeconds==1,"UI auto refresh interval is saved and mirrored in the tray");
            if(!remoteSession)
            {
                int lines=log.Lines.Length; bool locked=false; state.Text="";
                for(int i=0;i<250;i++) { Application.DoEvents(); Thread.Sleep(10); if(!on.Enabled||(bool)Field(f,"busy"))locked=true; }
                Check(state.Text=="전원 켜짐" && !locked,"UI auto refresh reads the state without locking the controls");
                Check(log.Lines.Length==lines,"UI auto refresh logs only state changes");
            }
            trayAuto.PerformClick(); Settle(1200);
            Check(!autoButton.Checked && !AppSettings.Load(settingsFile).AutoRefresh,"UI auto refresh turns off from the tray menu");
            autoButton.PerformClick(); Settle(300); bool toggledOn=autoButton.Checked && AppSettings.Load(settingsFile).AutoRefresh;
            autoButton.PerformClick(); Settle(1200);
            Check(toggledOn && !autoButton.Checked,"UI auto button toggles automatic refresh");
            Call(f,"SetThemeMode",ThemeMode.Light,true); Settle(100);
            Check(Theme.Current==Theme.Light && f.BackColor==Theme.Light.Background && details.BackColor==Theme.Light.Surface && AppSettings.Load(settingsFile).ThemeName=="light","UI switches to the light theme and saves it");
            Call(f,"SetThemeMode",ThemeMode.Dark,true);
            Call(f,"SyncTray"); var trayOn=(ToolStripMenuItem)Field(f,"trayOn"); var trayTarget=(ToolStripMenuItem)Field(f,"trayTarget");
            Check(trayOn.Enabled==on.Enabled && trayTarget.Text.Contains("LG FULL HD"),"UI tray menu mirrors the selected monitor and controls");
            Call(f,"SetKeepInTray",true); SetField(f,"trayNoticeShown",true);
            Check(AppSettings.Load(settingsFile).CloseToTray && ((ToolStripMenuItem)Field(f,"trayKeep")).Checked,"UI close-to-tray option is saved and mirrored");
            f.Close(); Settle(100); Check(!f.IsDisposed && !f.Visible,"UI close hides to the tray when the option is on");
            Call(f,"ShowWindow"); Settle(100); Check(f.Visible,"UI tray reopens the window");
            Call(f,"SetKeepInTray",false);
            var settings=(AppSettings)Field(f,"settings");
            using(var dialog=new SettingsForm(settings)) Check(dialog.SelectedTheme==ThemeMode.Dark && dialog.RefreshSeconds==1 && dialog.PowerOnAttempts==10 && !dialog.AutoRefresh && !dialog.CloseToTray,"UI settings dialog shows the current values");
            using(var help=new HelpForm(settingsFile,10)) { help.Show(); Settle(100); RichTextBox text=null; foreach(Control c in help.Controls) foreach(Control inner in c.Controls) if(inner is RichTextBox) text=(RichTextBox)inner;
                Check(text!=null && text.Text.Contains("빠르게 사용하기") && text.Text.Contains("최대 10번"),"UI help shows its sections"); }
            var target=(MonitorInfo)combo.SelectedItem; target.Device="error31";
            Pump((Task)Call(f,"ReadState")); Check(state.Text=="상태 확인 불가" && banner.Text.Contains("Error 31") && on.Primary,"UI Error 31 guidance");
            Pump((Task)Call(f,"SetPower",true)); Check(state.Text=="명령 전송 실패" && banner.Text.Contains("전원 버튼"),"UI failed ON recovery guidance");
            Check(!File.ReadAllText(settingsFile).Contains("error31"),"UI saved monitor is a snapshot, not the live object");
            string missing=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"missing.exe");
            SetField(f,"tool",missing); SetField(f,"client",new MonitorClient(missing));
            Func<Task> read=()=> (Task)Call(f,"ReadState"); Pump((Task)Call(f,"Execute",read));
            Check(state.Text=="연동 도구 필요" && !on.Enabled && banner.Text.Contains("같은 폴더") && banner.Kind==BannerKind.Error,"UI missing tool inline guidance");
        }
        // A monitor saved last time that is no longer listed (e.g. after OFF) must still be offered as the ON target after a restart.
        new AppSettings { Monitor=new MonitorInfo { Device=@"\\.\DISPLAY9\Monitor0", Name="Saved LG", Id=@"MONITOR\SAVED\0001", ShortId="SAVED" }, DetailPanel="info", ThemeName="dark" }.Save(settingsFile);
        using(var f=new MainForm(tool,settingsFile))
        {
            f.Show(); Settle(1500); Idle(f);
            var combo=(ComboBox)Field(f,"monitors"); var selected=(MonitorInfo)combo.SelectedItem; var on=(FlatButton)Field(f,"on");
            Check(combo.Items.Count==3 && selected.Name=="Saved LG" && !selected.Available,"UI restores the saved monitor after restart even when unlisted");
            Check(((Label)Field(f,"state")).Text=="연결 확인 필요" && (on.Enabled || remoteSession) && on.Primary,"UI offers ON for the restored monitor");
            Check((bool)Field(f,"panelOpen") && ((ListView)Field(f,"details")).Visible,"UI restores the open panel");
            SetField(f,"trayNoticeShown",true); Call(f,"SetKeepInTray",true); Call(f,"ExitApplication"); Settle(100);
            Check(f.IsDisposed,"UI tray exit closes even with close-to-tray on");
        }
        Console.WriteLine("TOTAL UI: "+count+" passed (mock executable only)");
    }
}
