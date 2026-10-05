using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MonitorPower
{
    static class Program
    {
        [STAThread] static void Main()
        {
            bool first;
            using (var mutex = new System.Threading.Mutex(true, @"Local\MonitorPower.SingleInstance", out first))
            {
                // A second launch brings the running copy (possibly hidden in the tray) to the front instead of opening another window.
                if (!first) { NativeMethods.AllowSetForegroundWindow(-1); NativeMethods.PostMessage((IntPtr)0xFFFF, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
            }
        }
    }

    static class NativeMethods
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(int action, int param, out bool value, int update);
    }

    // Windows "Animation effects" (Settings > Accessibility > Visual effects). When it is off, hover fades are instant and progress bars stand still.
    static class Motion
    {
        const int GetClientAreaAnimation = 0x1042;
        public static bool Enabled { get { bool on; return !NativeMethods.SystemParametersInfo(GetClientAreaAnimation, 0, out on, 0) || on; } }
    }
}
