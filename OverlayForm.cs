namespace WowFisher;

internal sealed class OverlayForm : Form
{
    private readonly Button _startButton = CreateButton("Start Fishing", Color.FromArgb(22, 97, 77));
    private readonly Button _stopButton = CreateButton("Stop Fishing", Color.FromArgb(111, 49, 54));
    private Rectangle? _captureArea;
    private Rectangle? _gameClient;

    public OverlayForm()
    {
        Text = "WowFisher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        Controls.AddRange([_startButton, _stopButton]);
        _startButton.Click += (_, _) => StartFishing?.Invoke(this, EventArgs.Empty);
        _stopButton.Click += (_, _) => StopFishing?.Invoke(this, EventArgs.Empty);
        SetState(false, false);
        PositionNear(null);
    }

    public event EventHandler? StartFishing;
    public event EventHandler? StopFishing;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public void SetState(bool fishing, bool recording)
    {
        _startButton.Enabled = !fishing && !recording;
        _stopButton.Enabled = fishing;
    }

    public void PositionNear(Rectangle? captureArea, Rectangle? gameClient = null)
    {
        _captureArea = captureArea;
        _gameClient = gameClient;
        Screen screen = captureArea is { } area
            ? Screen.FromRectangle(area)
            : Screen.PrimaryScreen ?? Screen.FromPoint(Point.Empty);
        PlaceIn(screen.WorkingArea, DeviceDpi);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionNear(_captureArea, _gameClient);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = 3; // MA_NOACTIVATE
            return;
        }

        base.WndProc(ref message);
    }

    private void PlaceIn(Rectangle workArea, int dpi)
    {
        int Scale(int pixels) => (pixels * dpi + 48) / 96;

        int width = Scale(120);
        int height = Scale(38);
        int gap = Scale(6);
        int margin = Scale(16);
        var size = new Size(width * 2 + gap, height);
        Rectangle placement = _gameClient is { } client ? Rectangle.Intersect(workArea, client) : workArea;
        if (placement.Width < size.Width + margin * 2 || placement.Height < size.Height + margin * 2)
        {
            placement = workArea;
        }

        _startButton.Bounds = new Rectangle(0, 0, width, height);
        _stopButton.Bounds = new Rectangle(width + gap, 0, width, height);

        // A shaped window leaves the space between the buttons open to the game.
        var buttons = new Region(_startButton.Bounds);
        buttons.Union(_stopButton.Bounds);
        Region = buttons;

        int left = placement.Left + margin;
        int right = placement.Right - size.Width - margin;
        int top = placement.Top + margin;
        int bottom = placement.Bottom - size.Height - margin;
        Point[] corners = [new(right, top), new(left, top), new(right, bottom), new(left, bottom)];
        Point location = corners[0];

        foreach (Point corner in corners)
        {
            if (_captureArea is { } area && area.IntersectsWith(new Rectangle(corner, size)))
            {
                continue;
            }

            location = corner;
            break;
        }

        Bounds = new Rectangle(location, size);
    }

    private static Button CreateButton(string text, Color color)
    {
        var button = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Cursor = Cursors.Hand,
            TabStop = false,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = ControlPaint.Light(color);
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color);
        return button;
    }
}
