using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MonitorPower
{
    public sealed class MonitorInfo
    {
        public string Device = "", Name = "", Adapter = "", Id = "", ShortId = "";
        public bool Available = true;
        public string Target { get { return !String.IsNullOrWhiteSpace(Device) ? Device : Id; } }
        public bool Remote { get { return (Name + " " + Adapter + " " + Id).IndexOf("remote", StringComparison.OrdinalIgnoreCase) >= 0; } }
        public string DisplayName { get { return String.IsNullOrWhiteSpace(Name) ? "이름 없는 모니터" : Name; } }
        public MonitorInfo Copy() { return (MonitorInfo)MemberwiseClone(); }
        public string Location { get { return Device.Replace(@"\\.\", "").Replace(@"\Monitor", " / "); } }
        public override string ToString() { return DisplayName + "  ·  " + Location + (Available ? "" : "  (연결 확인 필요)"); }
    }

    // Remembers the last selected monitor and window preferences between runs. Any read or write failure falls back to defaults.
    public sealed class AppSettings
    {
        public const int MinRefreshSeconds = 1, MaxRefreshSeconds = 3600, DefaultRefreshSeconds = 5;
        public const int MinPowerOnAttempts = 1, MaxPowerOnAttempts = 30, DefaultPowerOnAttempts = 10;
        public const string PanelNone = "none", PanelInfo = "info", PanelLog = "log";
        public MonitorInfo Monitor;
        public bool CloseToTray, AutoRefresh;
        public int RefreshSeconds = DefaultRefreshSeconds, PowerOnAttempts = DefaultPowerOnAttempts;
        // ThemeName is system, dark or light; DetailPanel is the panel open under the main window (none, info or log).
        public string ThemeName = "system", DetailPanel = PanelNone;
        public static int ClampSeconds(int seconds) { return Math.Max(MinRefreshSeconds, Math.Min(MaxRefreshSeconds, seconds)); }
        public static int ClampAttempts(int attempts) { return Math.Max(MinPowerOnAttempts, Math.Min(MaxPowerOnAttempts, attempts)); }
        static string OneOf(string value, string fallback, params string[] allowed) { string v = (value ?? "").Trim().ToLowerInvariant(); return Array.IndexOf(allowed, v) >= 0 ? v : fallback; }
        public static string DefaultPath { get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorPower"), "settings.ini"); } }
        public static AppSettings Load(string path)
        {
            var settings = new AppSettings();
            var monitor = new MonitorInfo();
            bool panelSaved = false, legacyDetails = false;
            try
            {
                if (!File.Exists(path)) return settings;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string value = line.Substring(split + 1);
                    switch (line.Substring(0, split).Trim().ToLowerInvariant())
                    {
                        case "device": monitor.Device = value; break;
                        case "name": monitor.Name = value; break;
                        case "adapter": monitor.Adapter = value; break;
                        case "id": monitor.Id = value; break;
                        case "shortid": monitor.ShortId = value; break;
                        case "closetotray": settings.CloseToTray = value.Trim() == "1"; break;
                        case "autorefresh": settings.AutoRefresh = value.Trim() == "1"; break;
                        case "refreshseconds": int seconds; if (Int32.TryParse(value.Trim(), out seconds)) settings.RefreshSeconds = ClampSeconds(seconds); break;
                        case "poweronattempts": int attempts; if (Int32.TryParse(value.Trim(), out attempts)) settings.PowerOnAttempts = ClampAttempts(attempts); break;
                        case "theme": settings.ThemeName = OneOf(value, "system", "system", "dark", "light"); break;
                        case "detailpanel": settings.DetailPanel = OneOf(value, PanelNone, PanelNone, PanelInfo, PanelLog); panelSaved = true; break;
                        case "showdetails": legacyDetails = value.Trim() == "1"; break; // 1.2 and earlier: one details area, opened on the info tab
                    }
                }
            }
            catch (Exception) { return new AppSettings(); }
            if (!panelSaved && legacyDetails) settings.DetailPanel = PanelInfo;
            if (monitor.Target.Length > 0) settings.Monitor = monitor;
            return settings;
        }
        public bool Save(string path)
        {
            var lines = new List<string> { "closeToTray=" + (CloseToTray ? "1" : "0"), "autoRefresh=" + (AutoRefresh ? "1" : "0"), "refreshSeconds=" + ClampSeconds(RefreshSeconds),
                "powerOnAttempts=" + ClampAttempts(PowerOnAttempts), "theme=" + OneOf(ThemeName, "system", "system", "dark", "light"), "detailPanel=" + OneOf(DetailPanel, PanelNone, PanelNone, PanelInfo, PanelLog) };
            if (Monitor != null)
            {
                lines.Add("device=" + Clean(Monitor.Device)); lines.Add("name=" + Clean(Monitor.Name)); lines.Add("adapter=" + Clean(Monitor.Adapter));
                lines.Add("id=" + Clean(Monitor.Id)); lines.Add("shortId=" + Clean(Monitor.ShortId));
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, lines.ToArray(), new UTF8Encoding(false));
                return true;
            }
            catch (Exception) { return false; }
        }
        static string Clean(string value) { return (value ?? "").Replace("\r", " ").Replace("\n", " "); }
    }

    public static class MonitorParser
    {
        public static List<MonitorInfo> Parse(string text)
        {
            var result = new List<MonitorInfo>();
            MonitorInfo current = null;
            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Regex.Match(line, @"^\s*(Monitor Device Name|Monitor Name|Adapter Name|Monitor ID|Short Monitor ID)\s*:\s*(.*?)\s*$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string key = m.Groups[1].Value.ToLowerInvariant();
                string value = m.Groups[2].Value.Trim().Trim('"');
                if (key == "monitor device name")
                {
                    if (current != null && current.Target.Length > 0) result.Add(current);
                    current = new MonitorInfo();
                }
                if (current == null) current = new MonitorInfo();
                switch (key)
                {
                    case "monitor device name": current.Device = value; break;
                    case "monitor name": current.Name = value; break;
                    case "adapter name": current.Adapter = value; break;
                    case "monitor id": current.Id = value; break;
                    case "short monitor id": current.ShortId = value; break;
                }
            }
            if (current != null && current.Target.Length > 0) result.Add(current);
            return result;
        }
    }

    public sealed class CommandResult
    {
        public int Code;
        public string Output;
    }

    public sealed class MonitorClient
    {
        readonly string executable;
        readonly int timeout;
        public MonitorClient(string executable, int timeoutMilliseconds = 15000)
        {
            this.executable = executable;
            timeout = timeoutMilliseconds;
        }
        // Quote each argument according to Windows CommandLineToArgvW rules; never invoke a shell.
        public static string Quote(string value)
        {
            var builder = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') builder.Append('\\', slashes * 2 + 1).Append(c);
                else builder.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return builder.Append('\\', slashes * 2).Append('"').ToString();
        }
        public async Task<CommandResult> Run(params string[] arguments)
        {
            if (!File.Exists(executable)) throw new FileNotFoundException("ControlMyMonitor.exe가 없습니다. 프로그램과 같은 폴더에 넣고 다시 시도하세요.", executable);
            var quoted = new List<string>();
            foreach (string argument in arguments) quoted.Add(Quote(argument));
            var start = new ProcessStartInfo(executable, String.Join(" ", quoted.ToArray())) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)
            };
            using (var process = new Process { StartInfo = start })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                bool finished = await Task.Run(() => process.WaitForExit(timeout));
                if (!finished)
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("15초 안에 응답하지 않았습니다. 명령이 이미 적용되었을 수 있으니 실제 상태를 확인하세요.");
                }
                return new CommandResult { Code = process.ExitCode, Output = (await stdout) + (await stderr) };
            }
        }
        public async Task<List<MonitorInfo>> List()
        {
            string file = Path.Combine(Path.GetTempPath(), "MonitorPower-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                CommandResult result = await Run("/smonitors", file);
                if (result.Code != 0) throw new IOException("모니터 목록 조회 실패: " + ErrorText(result.Code));
                if (!File.Exists(file)) throw new IOException("모니터 목록 파일이 생성되지 않았습니다. ControlMyMonitor 버전과 실행 권한을 확인하세요.");
                // StreamReader detects UTF-8/UTF-16 BOM; ANSI exports fall back to the Windows code page.
                using (var reader = new StreamReader(file, Encoding.Default, true)) return MonitorParser.Parse(reader.ReadToEnd());
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }
        public Task<CommandResult> Get(MonitorInfo monitor) { return Run("/GetValue", monitor.Target, "D6"); }
        public Task<CommandResult> Set(MonitorInfo monitor, bool on) { return Run("/SetValue", monitor.Target, "D6", on ? "1" : "4"); }
        public static string PowerText(int value)
        {
            switch (value)
            {
                case 1: return "D6 = 1 · 켜짐 (ON)";
                case 2: return "D6 = 2 · 대기 (Standby)";
                case 3: return "D6 = 3 · 절전 (Suspend)";
                case 4: return "D6 = 4 · 꺼짐 (OFF, 이 LG 환경)";
                case 5: return "D6 = 5 · 꺼짐 (다른 모니터의 OFF 값)";
                default: return "D6 조회 불가 · " + ErrorText(value) + " · 실제 전원 상태는 알 수 없습니다.";
            }
        }
        public static string ErrorText(int code)
        {
            if (code == 31) return "Error 31: DDC/CI 통신 실패. OFF 후에는 모니터의 물리 전원 버튼으로 켜야 할 수 있습니다.";
            return "반환 코드 " + code + " (0x" + unchecked((uint)code).ToString("X8") + ")";
        }
    }
}

