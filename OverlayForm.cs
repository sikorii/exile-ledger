namespace Poe2PriceChecker;

internal sealed class OverlayForm : Form
{
    private static readonly Color InlineTransparencyColor = Color.FromArgb(255, 255, 0, 255);
    private const int InlineOverlayAutoHideMilliseconds = 8000;
    private const int WsExToolWindow = 0x80;
    private const int WsExNoActivate = 0x08000000;
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;

    private readonly FlowLayoutPanel _panel = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Padding = new Padding(12, 10, 44, 10),
        BackColor = Color.Black
    };

    private readonly Button _closeButton = new()
    {
        Text = "x",
        Size = new Size(28, 26),
        Anchor = AnchorStyles.Top | AnchorStyles.Right,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(32, 32, 32),
        ForeColor = Color.White,
        TabStop = false
    };
    private readonly System.Windows.Forms.Timer _inlineHideTimer = new()
    {
        Interval = InlineOverlayAutoHideMilliseconds
    };
    private readonly List<RuneshapingOverlayLabel> _inlineLabels = [];

    public OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.88;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        Controls.Add(_panel);
        Controls.Add(_closeButton);
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);
        _closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
        _closeButton.Click += (_, _) =>
        {
            _inlineHideTimer.Stop();
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        };
        _closeButton.BringToFront();
        _inlineHideTimer.Tick += (_, _) =>
        {
            _inlineHideTimer.Stop();
            Hide();
        };
    }

    public event EventHandler? Dismissed;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            var lParam = m.LParam.ToInt64();
            var screenPoint = new Point(unchecked((short)(lParam & 0xFFFF)), unchecked((short)((lParam >> 16) & 0xFFFF)));
            var clientPoint = PointToClient(screenPoint);
            if (!_closeButton.Visible || !_closeButton.Bounds.Contains(clientPoint))
            {
                m.Result = HtTransparent;
                return;
            }
        }

        base.WndProc(ref m);
    }

    public void ShowResult(ScanResult result)
    {
        if (result.OverlayLabels.Count == 0)
        {
            HideRuneshapingInlineOverlay();
            Hide();
            return;
        }

        ShowInlineRuneshapingResult(result);
    }

    public void HideRuneshapingInlineOverlay()
    {
        _inlineHideTimer.Stop();
        ClearInlineLabels();
        if (!_panel.Visible)
        {
            Hide();
        }
    }

    public void ShowCurrencyResult(CurrencyScanResult result)
    {
        _inlineHideTimer.Stop();
        ClearInlineLabels();
        TransparencyKey = Color.Empty;
        BackColor = Color.Black;
        Opacity = 0.88;
        _panel.Visible = true;
        _closeButton.Visible = true;
        _panel.Controls.Clear();
        AddHeader($"Currency Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div");

        foreach (var stack in result.TopStacks)
        {
            AddLine(stack.DisplayText, Color.FromArgb(90, 255, 112), 14f, FontStyle.Bold);
        }

        if (result.UnknownOccupiedSlots > 0)
        {
            AddLine($"{result.UnknownOccupiedSlots} occupied slots not mapped yet", Color.FromArgb(255, 220, 72), 12f, FontStyle.Bold);
        }

        if (result.TopStacks.Count == 0 && result.UnknownOccupiedSlots == 0)
        {
            AddLine("No currency stacks detected", Color.LightGray, 13f, FontStyle.Regular);
        }

        Width = 820;
        Height = Math.Min(560, 70 + (result.TopStacks.Count + 2) * 40);
        _closeButton.Location = new Point(Width - _closeButton.Width - 6, 6);
        var x = result.ScreenBounds.Left + 48;
        var y = result.ScreenBounds.Top + 1440;
        if (y + Height > result.ScreenBounds.Bottom - 24)
        {
            y = result.ScreenBounds.Bottom - Height - 24;
        }

        Location = new Point(x, y);
        Show();
        BringToFront();
    }

    private void ShowInlineRuneshapingResult(ScanResult result)
    {
        _inlineHideTimer.Stop();
        ClearInlineLabels();
        _panel.Visible = false;
        _closeButton.Visible = false;
        Opacity = 1;
        BackColor = InlineTransparencyColor;
        TransparencyKey = InlineTransparencyColor;
        Bounds = result.ScreenBounds;

        var labels = result.OverlayLabels;
        if (labels.Count == 0)
        {
            HideRuneshapingInlineOverlay();
            Hide();
            return;
        }

        _inlineLabels.AddRange(labels
            .Select((label, index) => new { Label = label, Index = index })
            .OrderBy(item => item.Label.LabelBounds.Top)
            .ThenBy(item => item.Index)
            .Select(item => item.Label));

        Show();
        BringToFront();
        TopMost = false;
        TopMost = true;
        Invalidate();
        _inlineHideTimer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_inlineLabels.Count == 0)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var label in _inlineLabels)
        {
            var bounds = new Rectangle(
                label.LabelBounds.X - Left,
                label.LabelBounds.Y - Top,
                label.LabelBounds.Width,
                label.LabelBounds.Height);
            DrawPricePill(e.Graphics, bounds, label.Text, ToColor(label.Color));
        }
    }

    private void ClearInlineLabels()
    {
        _inlineLabels.Clear();
        Invalidate();
    }

    private void AddHeader(string text)
    {
        AddLine(text, Color.White, 12f, FontStyle.Bold);
    }

    private void AddChoice(RewardChoice choice)
    {
        AddLine(choice.DisplayText, ToColor(choice.Color), 16f, FontStyle.Bold);
    }

    private void AddLine(string text, Color color, float size, FontStyle style)
    {
        var label = new Label
        {
            AutoSize = false,
            Width = 750,
            Height = 40,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Black,
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false
        };
        _panel.Controls.Add(label);
    }

    private static Color ToColor(ChoiceColor color)
    {
        return color switch
        {
            ChoiceColor.Green => Color.FromArgb(90, 255, 112),
            ChoiceColor.Yellow => Color.FromArgb(255, 220, 72),
            _ => Color.FromArgb(255, 80, 80)
        };
    }

    private static Size MeasurePillSize(string text)
    {
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(Math.Clamp(size.Width + 20, 58, 170), 28);
    }

    private static void DrawPricePill(Graphics graphics, Rectangle bounds, string text, Color textColor)
    {
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var backgroundBrush = new SolidBrush(Color.FromArgb(230, 9, 12, 14));
        using var borderPen = new Pen(Color.FromArgb(210, textColor), 1f);
        using var path = RoundedRectangle(new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1), 7);
        graphics.FillPath(backgroundBrush, path);
        graphics.DrawPath(borderPen, path);

        var textBounds = new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            new Rectangle(textBounds.X + 1, textBounds.Y + 1, textBounds.Width, textBounds.Height),
            Color.FromArgb(230, 0, 0, 0),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            graphics,
            text,
            font,
            textBounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
