using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DynamicIsland
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;
        public const string ShowInstanceMessageName = "AiaDinamic_ShowInstance_Message";
        public static readonly uint WM_SHOW_INSTANCE = RegisterWindowMessage(ShowInstanceMessageName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int HWND_BROADCAST = 0xFFFF;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try { File.WriteAllText("D:\\Projek-dev\\aia-ide\\crash.log", args.ExceptionObject?.ToString()); } catch { }
            };
            DispatcherUnhandledException += (s, args) =>
            {
                try { File.WriteAllText("D:\\Projek-dev\\aia-ide\\crash.log", args.Exception?.ToString()); } catch { }
                args.Handled = true;
            };

            bool createdNew = false;
            try
            {
                const string mutexName = "Local\\AiaDinamic_SingleInstance_Mutex";
                _singleInstanceMutex = new Mutex(true, mutexName, out createdNew);
            }
            catch (AbandonedMutexException)
            {
                createdNew = true;
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Another instance is already running! Broadcast message to wake it up and bring it to front
                PostMessage((IntPtr)HWND_BROADCAST, WM_SHOW_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                try { _singleInstanceMutex?.Dispose(); } catch { }
                _singleInstanceMutex = null;
                Environment.Exit(0);
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
            base.OnExit(e);
        }
    }
}
