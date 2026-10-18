using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;

namespace TwoCtrl
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool smoke = Array.IndexOf(args, "--smoke-test") >= 0;
            bool created;
            using (var mutex = new Mutex(true, "Local\\TwoCtrl.Tray", out created))
            {
                if (!created && smoke) { Environment.ExitCode = 2; return; }
                if (!created) { MessageBox.Show("2ctrl is already running in the notification area.", "2ctrl"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (var app = new TrayApp(smoke))
                    using (var timer = new System.Windows.Forms.Timer())
                    {
                        if (smoke) { timer.Interval = 500; timer.Tick += delegate { timer.Stop(); app.ExitThread(); }; timer.Start(); }
                        Application.Run(app);
                    }
                }
                catch (Exception ex) { Environment.ExitCode = 1; if (!smoke) MessageBox.Show(ex.Message, "2ctrl could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private readonly TapDetector detector = new TapDetector();
        private readonly Control dispatcher = new Control();
        private readonly NotifyIcon tray = new NotifyIcon();
        private Icon appIcon;
        private readonly Native.HookProc keyboardProc;
        private readonly Native.HookProc mouseProc;
        private readonly System.Windows.Forms.Timer checkTimer = new System.Windows.Forms.Timer();
        private IntPtr keyboardHook, mouseHook, candidateWindow, expectedLayout;
        private uint expectedThread;
        private bool paused, disposed;
        private int generation;
        private string leftId = "", rightId = "";
        private readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "2ctrl", "settings.xml");
        private ToolStripMenuItem statusItem;
        private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupName = "2ctrl";

        private static string StartupCommand { get { return "\"" + Application.ExecutablePath + "\""; } }

        private static bool RunsOnStartup()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey))
                return key != null && string.Equals(key.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetRunOnStartup(bool enabled)
        {
            if (enabled)
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupKey))
                    key.SetValue(StartupName, StartupCommand, RegistryValueKind.String);
            }
            else
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupKey, true))
                    if (key != null) key.DeleteValue(StartupName, false);
            }
        }

        private static void RefreshStartupItem(ToolStripMenuItem item)
        {
            try { item.Checked = RunsOnStartup(); item.Enabled = true; }
            catch { item.Checked = false; item.Enabled = false; }
        }

        public TrayApp(bool smoke)
        {
            paused = smoke;
            keyboardProc = OnKeyboard;
            mouseProc = OnMouse;
            dispatcher.CreateControl();
            IntPtr handle = dispatcher.Handle;
            ChooseDefaults();
            LoadSettings();
            using (Stream iconStream = typeof(TrayApp).Assembly.GetManifestResourceStream("TwoCtrl.AppIcon.ico"))
            {
                if (iconStream == null) throw new InvalidOperationException("Embedded application icon is missing.");
                using (var loadedIcon = new Icon(iconStream, SystemInformation.SmallIconSize))
                    appIcon = (Icon)loadedIcon.Clone();
            }
            tray.Icon = appIcon;
            tray.Text = "2ctrl: left Ctrl / right Ctrl";
            BuildMenu();
            checkTimer.Interval = 180;
            checkTimer.Tick += VerifySwitch;
            // Seed only concrete key codes (generic Shift/Ctrl/Alt would be duplicates).
            for (int key = 1; key < 255; key++)
                if (key != 0x10 && key != 0x11 && key != 0x12 && key > 6 && (Native.GetAsyncKeyState(key) & 0x8000) != 0)
                    detector.Seed(key);
            using (var process = Process.GetCurrentProcess())
            {
                IntPtr module = Native.GetModuleHandle(process.MainModule.ModuleName);
                keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, module, 0);
                mouseHook = Native.SetWindowsHookEx(14, mouseProc, module, 0);
            }
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new InvalidOperationException("Could not install input hooks. Windows error: " + error);
            }
            tray.Visible = !smoke;
            if (!smoke) tray.ShowBalloonTip(3500, "2ctrl is running", "Solo left Ctrl: " + Label(leftId) + "\nSolo right Ctrl: " + Label(rightId) + "\nRight-click the tray icon to configure or exit.", ToolTipIcon.Info);
        }

        private static string Id(InputLanguage language) { return language.Handle.ToInt64().ToString("X"); }
        private static InputLanguage Find(string id)
        {
            foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
                if (Id(language) == id) return language;
            return null;
        }
        private static string Label(string id)
        {
            InputLanguage language = Find(id);
            return language == null ? "not configured" : language.Culture.DisplayName + " / " + language.LayoutName;
        }
        private void ChooseDefaults()
        {
            foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
            {
                if (leftId.Length == 0 && language.Culture.TwoLetterISOLanguageName == "en") leftId = Id(language);
                if (rightId.Length == 0 && language.Culture.TwoLetterISOLanguageName == "ru") rightId = Id(language);
            }
        }
        private void LoadSettings()
        {
            if (!File.Exists(settingsPath)) return;
            try
            {
                XElement xml = XElement.Load(settingsPath);
                string left = (string)xml.Attribute("left"), right = (string)xml.Attribute("right");
                if (left != null && Find(left) != null) leftId = left;
                if (right != null && Find(right) != null) rightId = right;
            }
            catch (Exception ex) { MessageBox.Show("Could not read settings. Using defaults.\n" + ex.Message, "2ctrl"); }
        }
        private void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                new XElement("settings", new XAttribute("left", leftId), new XAttribute("right", rightId)).Save(settingsPath);
            }
            catch (Exception ex) { MessageBox.Show("Settings apply for this session but could not be saved.\n" + ex.Message, "2ctrl"); }
        }
        private void BuildMenu()
        {
            var menu = new ContextMenuStrip();
            statusItem = new ToolStripMenuItem("Ready — solo Ctrl switches on release");
            statusItem.Enabled = false;
            menu.Items.Add(statusItem);
            menu.Items.Add(LayoutMenu("Left Ctrl", true));
            menu.Items.Add(LayoutMenu("Right Ctrl", false));
            var pause = new ToolStripMenuItem("Pause") { CheckOnClick = true };
            pause.CheckedChanged += delegate { paused = pause.Checked; generation++; detector.Cancel(); checkTimer.Stop(); tray.Text = paused ? "2ctrl: paused" : "2ctrl: left Ctrl / right Ctrl"; };
            menu.Items.Add(pause);
            var startup = new ToolStripMenuItem("Run on startup");
            startup.Click += delegate
            {
                try { SetRunOnStartup(!RunsOnStartup()); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not change run on startup.\n" + ex.Message, "2ctrl", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshStartupItem(startup);
            };
            menu.Items.Add(startup);
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate { detector.Cancel(); RefreshStartupItem(startup); };
            tray.ContextMenuStrip = menu;
        }
        private ToolStripMenuItem LayoutMenu(string title, bool left)
        {
            var parent = new ToolStripMenuItem(title + ": " + Label(left ? leftId : rightId));
            parent.DropDownOpening += delegate
            {
                parent.DropDownItems.Clear();
                foreach (InputLanguage language in InputLanguage.InstalledInputLanguages)
                {
                    string id = Id(language);
                    var item = new ToolStripMenuItem(Label(id)) { Checked = id == (left ? leftId : rightId) };
                    item.Click += delegate
                    {
                        if (left) leftId = id; else rightId = id;
                        generation++; detector.Cancel();
                        parent.Text = title + ": " + Label(id);
                        SaveSettings();
                    };
                    parent.DropDownItems.Add(item);
                }
            };
            return parent;
        }
        private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                var input = (Native.KeyboardInput)Marshal.PtrToStructure(data, typeof(Native.KeyboardInput));
                int msg = message.ToInt32();
                bool down = msg == 0x100 || msg == 0x104;
                bool up = msg == 0x101 || msg == 0x105;
                if (down || up)
                {
                    int key = (int)input.Key;
                    if (key == 0x11) key = (input.Flags & 1) != 0 ? TapDetector.RightControl : TapDetector.LeftControl;
                    if (down && (key == TapDetector.LeftControl || key == TapDetector.RightControl)) candidateWindow = Native.GetForegroundWindow();
                    int tap = detector.Process(key, down, (input.Flags & 0x10) != 0);
                    if (paused) detector.Cancel();
                    if (tap != 0 && !paused)
                    {
                        IntPtr target = candidateWindow;
                        int version = generation;
                        string id = tap == TapDetector.LeftControl ? leftId : rightId;
                        dispatcher.BeginInvoke((Action)delegate { if (!disposed && !paused && version == generation) Switch(target, id); });
                    }
                }
            }
            // Never suppress Ctrl or any other keyboard event.
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
        {
            // Button down/up, wheel and horizontal wheel cancel; pointer movement does not.
            if (code >= 0 && message.ToInt32() != 0x200) detector.Cancel();
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        private void Switch(IntPtr target, string id)
        {
            if (target == IntPtr.Zero || Native.GetForegroundWindow() != target) return;
            InputLanguage language = Find(id);
            if (language == null) { statusItem.Text = "Choose an installed layout in the Ctrl menus"; return; }
            uint process;
            uint thread = Native.GetWindowThreadProcessId(target, out process);
            var info = new Native.GuiThreadInfo();
            info.Size = (uint)Marshal.SizeOf(typeof(Native.GuiThreadInfo));
            IntPtr focus = target;
            if (Native.GetGUIThreadInfo(thread, ref info) && info.Focus != IntPtr.Zero) focus = info.Focus;
            expectedThread = Native.GetWindowThreadProcessId(focus, out process);
            expectedLayout = language.Handle;
            if (!Native.PostMessage(focus, 0x50, IntPtr.Zero, expectedLayout))
            {
                statusItem.Text = "Switch request failed (Windows " + Marshal.GetLastWin32Error() + ")";
                return;
            }
            statusItem.Text = "Requested: " + language.LayoutName;
            checkTimer.Stop();
            checkTimer.Start();
        }
        private void VerifySwitch(object sender, EventArgs e)
        {
            checkTimer.Stop();
            statusItem.Text = Native.GetKeyboardLayout(expectedThread) == expectedLayout
                ? "Layout switch confirmed" : "Layout not confirmed — this app may reject switching";
        }
        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                disposed = true;
                if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
                if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
                if (disposing)
                {
                    checkTimer.Dispose();
                    tray.Visible = false;
                    if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose();
                    tray.Dispose();
                    if (appIcon != null) appIcon.Dispose();
                    dispatcher.Dispose();
                }
            }
            base.Dispose(disposing);
        }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    }

    internal static class Native
    {
        internal delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
        {
            public uint Size, Flags;
            public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            public Rect CaretRect;
        }
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
    }
}
