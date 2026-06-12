namespace Poe2PriceChecker;

internal sealed class SlotMappingDialog : Form
{
    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _countOverrideBox = new();
    private readonly ListBox _suggestionsList = new();
    private readonly IReadOnlyList<PoeNinjaIconMatch> _iconSuggestions;
    private readonly Dictionary<string, Image> _suggestionImages = new(StringComparer.OrdinalIgnoreCase);

    public SlotMappingDialog(
        string currentName,
        int? currentQuantity,
        int? countOverride,
        IReadOnlyList<PoeNinjaIconMatch>? iconSuggestions = null)
    {
        ItemName = currentName;
        CountOverride = countOverride;
        _iconSuggestions = iconSuggestions ?? [];
        BuildUi(currentName, currentQuantity, countOverride);
    }

    public string ItemName { get; private set; }
    public int? CountOverride { get; private set; }

    private void BuildUi(string currentName, int? currentQuantity, int? countOverride)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Map Currency Slot";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        ClientSize = _iconSuggestions.Count > 0
            ? new Size(640, 440)
            : new Size(480, 240);
        MinimumSize = _iconSuggestions.Count > 0
            ? new Size(560, 360)
            : new Size(460, 260);

        var label = new Label
        {
            Text = "Item name",
            Location = new Point(16, 18),
            AutoSize = true
        };

        _itemNameBox.Location = new Point(16, 44);
        _itemNameBox.Size = new Size(ClientSize.Width - 32, 28);
        _itemNameBox.Text = currentName;
        _itemNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var countLabel = new Label
        {
            Text = currentQuantity is null
                ? "Count override (blank = local read, 0 = empty)"
                : $"Count override (blank = local read, 0 = empty, current x{currentQuantity})",
            Location = new Point(16, 86),
            AutoSize = true
        };

        _countOverrideBox.Location = new Point(16, 112);
        _countOverrideBox.Size = new Size(120, 28);
        _countOverrideBox.Text = countOverride?.ToString() ?? string.Empty;

        var buttonTop = ClientSize.Height - 48;
        var controls = new List<Control> { label, _itemNameBox, countLabel, _countOverrideBox };

        if (currentQuantity is > 0)
        {
            var confirmCountButton = new Button
            {
                Text = $"Confirm x{currentQuantity}",
                Location = new Point(148, 111),
                Size = new Size(124, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            confirmCountButton.Click += (_, _) =>
            {
                _countOverrideBox.Text = currentQuantity.Value.ToString();
                _countOverrideBox.SelectAll();
                _countOverrideBox.Focus();
            };
            controls.Add(confirmCountButton);
        }

        if (_iconSuggestions.Count > 0)
        {
            LoadSuggestionImages();

            var suggestionsLabel = new Label
            {
                Text = "Icon suggestions",
                Location = new Point(16, 150),
                AutoSize = true
            };

            _suggestionsList.Location = new Point(16, 176);
            _suggestionsList.Size = new Size(ClientSize.Width - 164, ClientSize.Height - 236);
            _suggestionsList.DrawMode = DrawMode.OwnerDrawFixed;
            _suggestionsList.ItemHeight = 46;
            _suggestionsList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _suggestionsList.DrawItem += SuggestionsList_DrawItem;
            _suggestionsList.Items.AddRange(_iconSuggestions
                .Select(suggestion => new IconSuggestionListItem(suggestion))
                .Cast<object>()
                .ToArray());
            _suggestionsList.DoubleClick += (_, _) => ApplySelectedSuggestion();
            if (_suggestionsList.Items.Count > 0)
            {
                _suggestionsList.SelectedIndex = 0;
            }

            var useSuggestionButton = new Button
            {
                Text = "Use",
                Location = new Point(ClientSize.Width - 132, 176),
                Size = new Size(116, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            useSuggestionButton.Click += (_, _) => ApplySelectedSuggestion();
            controls.AddRange([suggestionsLabel, _suggestionsList, useSuggestionButton]);
        }

        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(ClientSize.Width - 204, buttonTop),
            Size = new Size(86, 32),
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        saveButton.Click += (_, _) =>
        {
            var countText = _countOverrideBox.Text.Trim();
            if (countText.Length > 0 && (!int.TryParse(countText, out var parsedCount) || parsedCount < 0))
            {
                MessageBox.Show(this, "Count override must be 0 for empty, a positive whole number, or blank to use OCR.", "Invalid Count", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            ItemName = _itemNameBox.Text.Trim();
            CountOverride = countText.Length == 0
                ? null
                : int.Parse(countText);
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(ClientSize.Width - 110, buttonTop),
            Size = new Size(86, 32),
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        controls.AddRange([saveButton, cancelButton]);
        Controls.AddRange(controls.ToArray());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var image in _suggestionImages.Values)
        {
            image.Dispose();
        }

        _suggestionImages.Clear();
        base.OnFormClosed(e);
    }

    private void ApplySelectedSuggestion()
    {
        if (_suggestionsList.SelectedItem is not IconSuggestionListItem item)
        {
            return;
        }

        _itemNameBox.Text = item.Match.ItemName;
        _itemNameBox.SelectAll();
        _itemNameBox.Focus();
    }

    private void LoadSuggestionImages()
    {
        foreach (var suggestion in _iconSuggestions)
        {
            if (string.IsNullOrWhiteSpace(suggestion.LocalPath) ||
                _suggestionImages.ContainsKey(suggestion.LocalPath) ||
                !File.Exists(suggestion.LocalPath))
            {
                continue;
            }

            try
            {
                _suggestionImages[suggestion.LocalPath] = CurrencyScanner.LoadBitmapWithoutFileLock(suggestion.LocalPath);
            }
            catch
            {
                // Suggestions still work as text if a cached thumbnail cannot be loaded.
            }
        }
    }

    private void SuggestionsList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= _suggestionsList.Items.Count)
        {
            return;
        }

        if (_suggestionsList.Items[e.Index] is not IconSuggestionListItem item)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var textColor = selected ? SystemColors.HighlightText : SystemColors.ControlText;
        var imageRect = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + 5, 36, 36);
        if (_suggestionImages.TryGetValue(item.Match.LocalPath, out var image))
        {
            e.Graphics.DrawImage(image, imageRect);
        }

        using var brush = new SolidBrush(textColor);
        using var nameFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var textLeft = imageRect.Right + 8;
        var textWidth = e.Bounds.Right - textLeft - 4;
        e.Graphics.DrawString(item.Match.ItemName, nameFont, brush, new RectangleF(textLeft, e.Bounds.Top + 5, textWidth, 21), format);
        var meta = $"{item.Match.Confidence:0.000} {item.Match.SourceKind} {item.Match.Type} gap {item.Match.SecondBestGap:0.000}";
        e.Graphics.DrawString(meta, metaFont, brush, new RectangleF(textLeft, e.Bounds.Top + 27, textWidth, 16), format);
        e.DrawFocusRectangle();
    }

    private sealed record IconSuggestionListItem(PoeNinjaIconMatch Match)
    {
        public override string ToString() => $"{Match.ItemName} ({Match.Confidence:0.000})";
    }
}
