using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace CodexUsageMonitor
{
    internal static class Native
    {
        internal const int GWL_EXSTYLE = -20;
        internal const int WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
        internal const int SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; }
        internal delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr window, StringBuilder name, int count);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        internal delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int count);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr security, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
        internal static IntPtr CreateKillJob(IntPtr process)
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return IntPtr.Zero;
            var limits = new ExtendedLimits();
            limits.Basic.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            int size = Marshal.SizeOf(limits);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limits, buffer, false);
                if (!SetInformationJobObject(job, 9, buffer, (uint)size) || !AssignProcessToJobObject(job, process))
                { CloseHandle(job); return IntPtr.Zero; }
                return job;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal static double Scale(IntPtr window)
        {
            try { uint dpi = GetDpiForWindow(window); return dpi >= 96 ? dpi / 96.0 : 1; }
            catch (EntryPointNotFoundException) { return 1; }
        }
        internal static Rect Client(IntPtr window)
        {
            RECT rect;
            var origin = new POINT();
            if (!GetClientRect(window, out rect) || !ClientToScreen(window, ref origin)) return Rect.Empty;
            return new Rect(origin.X, origin.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
        internal static string Executable(IntPtr window)
        {
            uint id;
            GetWindowThreadProcessId(window, out id);
            IntPtr process = OpenProcess(0x1000, false, id);
            if (process == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(2048);
                int count = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref count) ? path.ToString() : null;
            }
            finally { CloseHandle(process); }
        }
        internal static bool IsCodex(IntPtr window)
        {
            if (!IsWindow(window) || !IsWindowVisible(window)) return false;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "Chrome_WidgetWin_1") return false;
            int extended = GetWindowLong(window, GWL_EXSTYLE);
            if ((extended & (WS_EX_TOOLWINDOW | 0x20)) != 0) return false;
            string path = Executable(window);
            Rect client = Client(window);
            return IsDesktopExecutable(path) && (IsIconic(window) || (!client.IsEmpty && client.Width >= 480 && client.Height >= 320));
        }
        internal static bool IsDesktopExecutable(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string filename = Path.GetFileName(path);
            if (string.Equals(filename, "Codex.exe", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(filename, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase) &&
                (path.IndexOf(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 path.IndexOf(@"\OpenAI\Codex\", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        internal static IntPtr FindCodex()
        {
            IntPtr foreground = GetForegroundWindow();
            if (IsCodex(foreground)) return foreground;
            IntPtr found = IntPtr.Zero;
            EnumWindows((window, data) => { if (IsCodex(window)) { found = window; return false; } return true; }, IntPtr.Zero);
            return found;
        }
        internal static bool ForegroundEligible(IntPtr window)
        {
            return window != IntPtr.Zero && IsWindow(window) && IsWindowVisible(window) && !IsIconic(window) && GetForegroundWindow() == window;
        }
    }
    internal sealed class ForegroundWatch : IDisposable
    {
        private readonly Native.WinEventProc callback;
        private IntPtr hook;
        public ForegroundWatch(Action changed)
        {
            callback = (handle, type, window, objectId, childId, thread, time) => changed();
            hook = Native.SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
        }
        public void Dispose() { if (hook != IntPtr.Zero) { Native.UnhookWinEvent(hook); hook = IntPtr.Zero; } }
    }}
