namespace Poe2PriceChecker;

internal sealed class OverlayForm : Form
{
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

    public OverlayForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.Black;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0.88;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Controls.Add(_panel);
        Controls.Add(_closeButton);
        _closeButton.FlatAppearance.BorderColor = Color.FromArgb(95, 95, 95);
        _closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
        _closeButton.Click += (_, _) =>
        {
            Hide();
            Dismissed?.Invoke(this, EventArgs.Empty);
        };
        _closeButton.BringToFront();
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
            if (!_closeButton.Bounds.Contains(clientPoint))
            {
                m.Result = HtTransparent;
                return;
            }
        }

        base.WndProc(ref m);
    }

    public void ShowResult(ScanResult result)
    {
        _panel.Controls.Clear();

        if (result.Choices.Count == 0 && result.UnpricedRewards.Count == 0)
        {
            Hide();
            return;
        }

        AddHeader("Runeshaping Prices");
        foreach (var choice in result.Choices)
        {
            AddChoice(choice);
        }

        foreach (var unpriced in result.UnpricedRewards.Take(4))
        {
            AddLine($"{unpriced}   n/a", Color.LightGray, 13f, FontStyle.Regular);
        }

        if (result.UnpricedRewards.Count > 4)
        {
            AddLine($"+{result.UnpricedRewards.Count - 4} unpriced", Color.LightGray, 12f, FontStyle.Regular);
        }

        foreach (var note in result.Notes.Take(3))
        {
            AddLine(note, Color.FromArgb(150, 210, 255), 11f, FontStyle.Regular);
        }

        Width = 780;
        Height = Math.Min(600, 62 + (result.Choices.Count + Math.Min(5, result.UnpricedRewards.Count) + Math.Min(3, result.Notes.Count)) * 44);
        _closeButton.Location = new Point(Width - _closeButton.Width - 6, 6);
        var x = result.ScreenBounds.Left + result.CaptureRegion.Right + 24;
        var y = result.ScreenBounds.Top + result.CaptureRegion.Top + 10;
        if (x + Width > result.ScreenBounds.Right - 24)
        {
            x = result.ScreenBounds.Left + Math.Max(24, result.CaptureRegion.X - Width - 24);
        }

        Location = new Point(x, y);
        Show();
        BringToFront();
    }

    public void ShowCurrencyResult(CurrencyScanResult result)
    {
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
}
