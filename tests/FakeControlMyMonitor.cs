using System;
using System.IO;
using System.Text;
using System.Threading;

class FakeControlMyMonitor
{
    static int Main(string[] args)
    {
        if (args[0] == "/smonitors")
        {
            File.WriteAllText(args[1], "==================================================\r\nMonitor Device Name: \\\\.\\DISPLAY1\\Monitor0\r\nMonitor Name: LG FULL HD\r\nAdapter Name: AMD Radeon RX 6600 XT\r\nMonitor ID: MONITOR\\GSM5B55\\example\\0012\r\nShort Monitor ID: GSM5B55\r\n==================================================\r\nMonitor Device Name: \\\\.\\DISPLAY2\\Monitor0\r\nMonitor Name: Remote_Monitor\r\nAdapter Name: Remote Display Adapter\r\nMonitor ID: MONITOR\\REMOTE\\0001\r\nShort Monitor ID: REMOTE\r\n", Encoding.Unicode);
            return 0;
        }
        if (args.Length != 3 && args.Length != 4) return 90;
        if (args[2] != "D6") return 91;
        if (args[1] == "timeout") { Thread.Sleep(5000); return 0; }
        if (args[1] == "error31") return 31;
        // "wake" acts like a monitor whose DDC/CI is still waking up: it keeps reporting OFF until it has received ON three times.
        if (args[1] == "wake")
        {
            string counter = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wake-count.txt");
            int received = File.Exists(counter) ? int.Parse(File.ReadAllText(counter)) : 0;
            if (args[0] == "/SetValue") { File.WriteAllText(counter, (received + 1).ToString()); return 0; }
            return received >= 3 ? 1 : 4;
        }
        // "stayoff" accepts every command but never reports ON.
        if (args[1] == "stayoff") return args[0] == "/GetValue" ? 4 : 0;
        if (args[0] == "/GetValue") return 1;
        if (args[0] == "/SetValue")
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last-command.txt"), String.Join("\n", args), Encoding.UTF8);
            return args[3] == "1" || args[3] == "4" ? 0 : 92;
        }
        return 93;
    }
}
