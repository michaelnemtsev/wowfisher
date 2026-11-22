using System.Runtime.InteropServices;

namespace WowFisher;

internal static class NativeMethods
{
    private const uint ScanCode = 0x0008;
    private const uint KeyUp = 0x0002;
    private const ushort SlashScanCode = 0x35;
    private const uint RootWindow = 2;

    internal const int HotkeyMessage = 0x0312;
    internal const uint ControlShiftNoRepeat = 0x4006;

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out WindowRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref Point point);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    public static IntPtr WindowAt(Point point) => GetAncestor(WindowFromPoint(point), RootWindow);

    public static Rectangle ClientBounds(IntPtr window)
    {
        Point origin = Point.Empty;
        return GetClientRect(window, out WindowRect rect) && ClientToScreen(window, ref origin)
            ? new Rectangle(origin, new Size(rect.Right - rect.Left, rect.Bottom - rect.Top))
            : Rectangle.Empty;
    }

    public static bool IsModifierDown() => IsDown(Keys.ShiftKey) || IsDown(Keys.ControlKey) ||
        IsDown(Keys.Menu) || IsDown(Keys.LWin) || IsDown(Keys.RWin);

    public static bool IsSlashDown() => IsDown(Keys.OemQuestion);

    public static bool TryPressSlash()
    {
        Input[] press = [SlashInput(ScanCode), SlashInput(ScanCode | KeyUp)];
        int size = Marshal.SizeOf<Input>();
        uint sent = SendInput((uint)press.Length, press, size);
        if (sent == 1)
        {
            SendInput(1, [press[1]], size);
        }

        return sent == press.Length;
    }

    private static bool IsDown(Keys key) => GetAsyncKeyState((int)key) < 0;

    private static Input SlashInput(uint flags) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputData { Keyboard = new KeyboardInput { Scan = SlashScanCode, Flags = flags } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
}
