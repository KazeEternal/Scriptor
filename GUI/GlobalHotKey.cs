using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GUI
{
    internal sealed class GlobalHotKey : IDisposable
    {
        private const int HotKeyId = 1;
        private const uint ModAlt = 0x0001;
        private const uint ModWin = 0x0008;
        private const uint VirtualKeyS = 0x53;
        private const uint WmHotKey = 0x0312;
        private static readonly IntPtr MessageOnlyWindow = new(-3);
        private static readonly WindowProcedureCallback WindowProcedureDelegate = WindowProcedure;
        private static readonly Dictionary<IntPtr, GlobalHotKey> Instances = new();

        private readonly string _className = $"ScriptorGlobalHotKey{Environment.ProcessId}";
        private IntPtr _windowHandle;

        public event EventHandler? Pressed;

        public bool TryRegister(out string? error)
        {
            error = null;
            if (!OperatingSystem.IsWindows())
            {
                error = "Global hotkeys are supported only on Windows.";
                return false;
            }

            var moduleHandle = GetModuleHandle(null);
            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(WindowProcedureDelegate),
                Instance = moduleHandle,
                ClassName = _className,
            };

            if (RegisterClassEx(ref windowClass) == 0)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            _windowHandle = CreateWindowEx(
                0,
                _className,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                MessageOnlyWindow,
                IntPtr.Zero,
                moduleHandle,
                IntPtr.Zero);
            if (_windowHandle == IntPtr.Zero)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            lock (Instances)
            {
                Instances[_windowHandle] = this;
            }

            if (RegisterHotKey(_windowHandle, HotKeyId, ModAlt | ModWin, VirtualKeyS))
            {
                return true;
            }

            error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            Dispose();
            return false;
        }

        public void Dispose()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            UnregisterHotKey(_windowHandle, HotKeyId);
            lock (Instances)
            {
                Instances.Remove(_windowHandle);
            }

            DestroyWindow(_windowHandle);
            _windowHandle = IntPtr.Zero;
        }

        private static IntPtr WindowProcedure(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (message == WmHotKey && wParam.ToInt32() == HotKeyId)
            {
                lock (Instances)
                {
                    if (Instances.TryGetValue(windowHandle, out var hotKey))
                    {
                        hotKey.Pressed?.Invoke(hotKey, EventArgs.Empty);
                    }
                }
            }

            return DefWindowProc(windowHandle, message, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            public uint Size;
            public uint Style;
            public IntPtr WindowProcedure;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string? MenuName;
            public string ClassName;
            public IntPtr SmallIcon;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WindowProcedureCallback(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WindowClass windowClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        public static bool TryGetCursorPosition(out int x, out int y)
        {
            if (OperatingSystem.IsWindows() && GetCursorPos(out var point))
            {
                x = point.X;
                y = point.Y;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }
    }
}
