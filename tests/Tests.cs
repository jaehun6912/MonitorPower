using System;
using System.IO;
using System.Threading.Tasks;
using MonitorPower;

class Tests
{
    static int count;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        count++;
    }
    // Print failures ourselves: on this system the runtime's own unhandled-exception printer fails on the Korean text and hides which check broke.
    static int Main(string[] args)
    {
        // run-tests.ps1 starts each new executable once before testing; the probe also starts the mock the way the tests do.
        if (args.Length > 1 && args[0] == "--probe") return new MonitorClient(args[1]).Get(new MonitorInfo { Device = "probe" }).GetAwaiter().GetResult().Code == 1 ? 0 : 2;
        try { Test(args[0]).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.WriteLine(ex.GetType().Name + ": " + ex.Message); Console.WriteLine(ex.StackTrace); return 1; }
    }
    static async Task Test(string fake)
    {
        var client = new MonitorClient(fake);
        var monitors = await client.List();
        Check(monitors.Count == 2, "UTF-16 /smonitors multiple monitors");
        Check(monitors[0].Device == @"\\.\DISPLAY1\Monitor0" && monitors[0].Name == "LG FULL HD" && monitors[0].Adapter.Contains("RX 6600 XT") && monitors[0].Id.Contains("GSM5B55") && monitors[0].ShortId == "GSM5B55", "all five monitor fields");
        Check(!monitors[0].Remote && monitors[1].Remote, "physical / remote detection");
        var quoted = MonitorParser.Parse("Monitor Device Name: \"\\\\.\\DISPLAY3\\Monitor0\"\r\nMonitor Name: \"LG\"\r\nShort Monitor ID: GSM5B55");
        Check(quoted.Count == 1 && quoted[0].Name == "LG", "quoted fields");
        Check(MonitorParser.Parse("").Count == 0, "empty list");
        Check((await client.Get(monitors[0])).Code == 1, "/GetValue uses exit code");
        Check((await client.Set(monitors[0], false)).Code == 0, "OFF command accepted");
        string recorded = File.ReadAllText(Path.Combine(Path.GetDirectoryName(fake), "last-command.txt"));
        Check(recorded == "/SetValue\n" + monitors[0].Target + "\nD6\n4", "OFF targets selected device and value 4");
        Check((await client.Set(monitors[0], true)).Code == 0, "ON command accepted");
        recorded = File.ReadAllText(Path.Combine(Path.GetDirectoryName(fake), "last-command.txt"));
        Check(recorded.EndsWith("\nD6\n1"), "ON value 1");
        var unusual = new MonitorInfo { Device = "한글 space \\\"quoted\" trailing\\" };
        await client.Set(unusual, true);
        recorded = File.ReadAllText(Path.Combine(Path.GetDirectoryName(fake), "last-command.txt"));
        Check(recorded == "/SetValue\n" + unusual.Target + "\nD6\n1", "Windows argument quoting round trip");
        Check((await client.Get(new MonitorInfo { Device = "error31" })).Code == 31 && MonitorClient.PowerText(31).Contains("Error 31") && !MonitorClient.PowerText(31).Contains("D6 = 31"), "Error 31 is not a power value");
        Check(MonitorClient.PowerText(0).Contains("조회 불가") && MonitorClient.PowerText(unchecked((int)0xC0262582)).Contains("C0262582"), "unknown and driver failure codes");
        bool missing = false;
        try { await new MonitorClient(fake + ".missing").List(); } catch (FileNotFoundException) { missing = true; }
        Check(missing, "missing executable friendly exception");
        bool timeout = false;
        try { await new MonitorClient(fake, 100).Get(new MonitorInfo { Device = "timeout" }); } catch (TimeoutException) { timeout = true; }
        Check(timeout, "process timeout");
        string settingsFile = Path.Combine(Path.Combine(Path.GetDirectoryName(fake), "settings-test"), "settings.ini");
        var saved = new AppSettings { CloseToTray = true, DetailPanel = AppSettings.PanelLog, ThemeName = "light", PowerOnAttempts = 15, AutoRefresh = true, RefreshSeconds = 30,
            Monitor = new MonitorInfo { Device = @"\\.\DISPLAY1\Monitor0", Name = "한글 = 모니터", Adapter = "AMD", Id = @"MONITOR\GSM5B55\x", ShortId = "GSM5B55" } };
        Check(saved.Save(settingsFile), "settings saved (folder created)");
        var loaded = AppSettings.Load(settingsFile);
        Check(loaded.AutoRefresh && loaded.RefreshSeconds == 30, "auto refresh settings round trip");
        Check(loaded.ThemeName == "light" && loaded.DetailPanel == AppSettings.PanelLog && loaded.PowerOnAttempts == 15, "theme, open panel and power-on tries round trip");
        Check(loaded.CloseToTray && loaded.Monitor != null && loaded.Monitor.Target == saved.Monitor.Target && loaded.Monitor.Name == "한글 = 모니터" && loaded.Monitor.Id == saved.Monitor.Id && loaded.Monitor.ShortId == "GSM5B55", "settings round trip");
        var missingSettings = AppSettings.Load(settingsFile + ".missing");
        Check(!missingSettings.CloseToTray && missingSettings.DetailPanel == AppSettings.PanelNone && missingSettings.ThemeName == "system" && missingSettings.PowerOnAttempts == AppSettings.DefaultPowerOnAttempts
            && !missingSettings.AutoRefresh && missingSettings.RefreshSeconds == AppSettings.DefaultRefreshSeconds && missingSettings.Monitor == null, "missing settings fall back to defaults");
        File.WriteAllText(settingsFile, "theme=purple\r\ndetailPanel=sidebar\r\npowerOnAttempts=0\r\n", System.Text.Encoding.UTF8); loaded = AppSettings.Load(settingsFile);
        File.WriteAllText(settingsFile, "powerOnAttempts=99\r\n", System.Text.Encoding.UTF8);
        Check(loaded.ThemeName == "system" && loaded.DetailPanel == AppSettings.PanelNone && loaded.PowerOnAttempts == 1 && AppSettings.Load(settingsFile).PowerOnAttempts == 30, "unknown theme and panel fall back, power-on tries clamped to 1-30");
        File.WriteAllText(settingsFile, "refreshSeconds=abc\r\n", System.Text.Encoding.UTF8);
        Check(AppSettings.Load(settingsFile).RefreshSeconds == AppSettings.DefaultRefreshSeconds, "unreadable refresh interval keeps the default");
        File.WriteAllText(settingsFile, "refreshSeconds=0\r\n", System.Text.Encoding.UTF8); int low = AppSettings.Load(settingsFile).RefreshSeconds;
        File.WriteAllText(settingsFile, "refreshSeconds=99999\r\n", System.Text.Encoding.UTF8); int high = AppSettings.Load(settingsFile).RefreshSeconds;
        Check(low == 1 && high == 3600, "refresh interval clamped to 1-3600 seconds");
        File.WriteAllText(settingsFile, "garbage\r\n=novalue\r\nshowDetails=1\r\n", System.Text.Encoding.UTF8);
        loaded = AppSettings.Load(settingsFile);
        Check(loaded.DetailPanel == AppSettings.PanelInfo && loaded.Monitor == null, "malformed lines ignored; 1.2 showDetails opens the info panel");
        File.WriteAllText(settingsFile, "showDetails=1\r\ndetailPanel=none\r\n", System.Text.Encoding.UTF8);
        Check(AppSettings.Load(settingsFile).DetailPanel == AppSettings.PanelNone, "a saved detailPanel wins over the old showDetails");
        Console.WriteLine("TOTAL: " + count + " passed (no physical monitor commands)");
    }
}
