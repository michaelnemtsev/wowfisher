using System.Diagnostics;
using System.Text.Json;

namespace WowFisher;

internal sealed record CaptureArea(string ProcessName, int X, int Y, int Width, int Height,
    int ClientWidth, int ClientHeight)
{
    private Rectangle RelativeBounds => new(X, Y, Width, Height);

    public Rectangle ClientBoundsAt(Rectangle screenBounds) => new(
        screenBounds.X - X, screenBounds.Y - Y, ClientWidth, ClientHeight);

    public static CaptureArea? FromSelection(Rectangle bounds)
    {
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        IntPtr window = NativeMethods.WindowAt(center);
        Rectangle client = NativeMethods.ClientBounds(window);
        NativeMethods.GetWindowThreadProcessId(window, out uint processId);
        string? process = GetProcessName(processId);

        if (process is null || !client.Contains(bounds) || !Screen.FromRectangle(bounds).Bounds.Contains(bounds))
        {
            return null;
        }

        return new CaptureArea(process, bounds.Left - client.Left, bounds.Top - client.Top,
            bounds.Width, bounds.Height, client.Width, client.Height);
    }

    public CaptureWindow? BindForeground()
    {
        IntPtr handle = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        string? process = GetProcessName(processId);
        return string.Equals(process, ProcessName, StringComparison.OrdinalIgnoreCase)
            ? new CaptureWindow(handle, processId)
            : null;
    }

    public Rectangle ScreenBounds(CaptureWindow window)
    {
        Rectangle client = NativeMethods.ClientBounds(window.Handle);
        Rectangle region = RelativeBounds;
        if (client.Width != ClientWidth || client.Height != ClientHeight ||
            !new Rectangle(Point.Empty, client.Size).Contains(region))
        {
            return Rectangle.Empty;
        }

        region.Offset(client.Location);
        return Screen.FromRectangle(region).Bounds.Contains(region) ? region : Rectangle.Empty;
    }

    public static CaptureArea? Load(string path)
    {
        try
        {
            CaptureArea? area = JsonSerializer.Deserialize<CaptureArea>(File.ReadAllText(path));
            return area is { Width: >= AreaSelector.MinimumSideLength, Height: >= AreaSelector.MinimumSideLength }
                && !string.IsNullOrWhiteSpace(area.ProcessName) ? area : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, overwrite: true);
    }

    private static string? GetProcessName(uint processId)
    {
        if (processId == 0 || processId == Environment.ProcessId)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

internal readonly record struct CaptureWindow(IntPtr Handle, uint ProcessId)
{
    public bool IsOpen => NativeMethods.GetWindowThreadProcessId(Handle, out uint processId) != 0
        && processId == ProcessId;

    public bool IsForeground => NativeMethods.GetForegroundWindow() == Handle && IsOpen;
}
