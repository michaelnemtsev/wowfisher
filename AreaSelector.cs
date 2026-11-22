namespace WowFisher;

internal sealed class AreaSelector : Form
{
    private readonly Rectangle _desktop = SystemInformation.VirtualScreen;
    private readonly Rectangle _monitor = Screen.FromPoint(Cursor.Position).WorkingArea;
    private Point? _dragStart;
    private Rectangle _selection;

    public const int MinimumSideLength = 16;

    public AreaSelector()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Color.Black;
        Opacity = 0.35;
        Cursor = Cursors.Cross;
        Bounds = _desktop;
    }

    public Rectangle ScreenSelection => new(
        _selection.X + Left, _selection.Y + Top, _selection.Width, _selection.Height);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragStart = e.Location;
        _selection = Rectangle.Empty;
        Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is { } start)
        {
            UpdateSelection(start, e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _dragStart is not { } start)
        {
            return;
        }

        UpdateSelection(start, e.Location);
        _dragStart = null;
        Capture = false;

        if (_selection.Width >= MinimumSideLength && _selection.Height >= MinimumSideLength)
        {
            DialogResult = DialogResult.OK;
        }
        else
        {
            Invalidate();
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var font = new Font("Segoe UI", 16, FontStyle.Bold);
        var instruction = new Point(_monitor.Left - Left + 24, _monitor.Top - Top + 24);
        TextRenderer.DrawText(e.Graphics, "Drag around the bobber. Press Esc to cancel.", font, instruction, Color.White);
        if (_dragStart.HasValue && _selection.Width > 0 && _selection.Height > 0)
        {
            using var pen = new Pen(Color.White, 3);
            e.Graphics.DrawRectangle(pen, _selection);
        }
    }

    private void UpdateSelection(Point start, Point end)
    {
        _selection = Rectangle.Intersect(ClientRectangle, Rectangle.FromLTRB(
            Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
            Math.Max(start.X, end.X), Math.Max(start.Y, end.Y)));
    }
}
