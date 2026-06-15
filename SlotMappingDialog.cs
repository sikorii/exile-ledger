namespace Poe2PriceChecker;

internal sealed class SlotMappingDialog : Form
{
    private static readonly Color AppBackground = Color.FromArgb(16, 20, 24);
    private static readonly Color CardBackground = Color.FromArgb(24, 31, 38);
    private static readonly Color CardBackgroundAlt = Color.FromArgb(28, 35, 43);
    private static readonly Color FieldBackground = Color.FromArgb(13, 17, 21);
    private static readonly Color BorderColor = Color.FromArgb(43, 52, 61);
    private static readonly Color TextPrimary = Color.FromArgb(236, 241, 244);
    private static readonly Color TextSecondary = Color.FromArgb(168, 179, 188);
    private static readonly Color AccentCyan = Color.FromArgb(83, 224, 218);
    private static readonly Color AccentTeal = Color.FromArgb(21, 148, 146);

    private readonly TextBox _itemNameBox = new();
    private readonly TextBox _countOverrideBox = new();
    private readonly ListBox _suggestionsList = new();
    private readonly IReadOnlyList<PoeNinjaIconMatch> _iconSuggestions;
    private readonly CountCropSaveResult? _countCropPreview;
    private readonly Dictionary<string, Image> _suggestionImages = new(StringComparer.OrdinalIgnoreCase);
    private Image? _rawCountPreviewImage;
    private Image? _cleanedCountPreviewImage;

    public SlotMappingDialog(
        string currentName,
        int? currentQuantity,
        int? countOverride,
        IReadOnlyList<PoeNinjaIconMatch>? iconSuggestions = null,
        CountCropSaveResult? countCropPreview = null)
    {
        ItemName = currentName;
        CountOverride = countOverride;
        _iconSuggestions = iconSuggestions ?? [];
        _countCropPreview = countCropPreview;
        BuildUi(currentName, currentQuantity, countOverride);
    }

    public string ItemName { get; private set; }
    public int? CountOverride { get; private set; }

    private void BuildUi(string currentName, int? currentQuantity, int? countOverride)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Map Stash Slot";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        BackColor = AppBackground;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var hasCountPreview = _countCropPreview?.Saved == true;
        var hasSuggestions = _iconSuggestions.Count > 0;
        ClientSize = _iconSuggestions.Count > 0
            ? new Size(760, hasCountPreview ? 734 : 632)
            : new Size(620, hasCountPreview ? 568 : 382);
        MinimumSize = _iconSuggestions.Count > 0
            ? new Size(680, hasCountPreview ? 624 : 532)
            : new Size(560, hasCountPreview ? 518 : 352);

        var margin = 18;
        var footerHeight = 64;
        var contentWidth = ClientSize.Width - (margin * 2);

        var titleLabel = new Label
        {
            Text = "Map Stash Slot",
            Location = new Point(margin, 12),
            Size = new Size(contentWidth, 36),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 14.5f, FontStyle.Bold),
            ForeColor = TextPrimary,
            BackColor = AppBackground
        };

        var helperLabel = new Label
        {
            Text = "Correct the item name or stack count for this slot.",
            Location = new Point(margin, 50),
            Size = new Size(contentWidth, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ForeColor = TextSecondary,
            BackColor = AppBackground
        };

        var itemCard = CreateCard("Item mapping", new Point(margin, 86), new Size(contentWidth, 190));

        var itemNameLabel = CreateFieldLabel("Item name", new Point(16, 46), new Size(140, 24));
        _itemNameBox.Location = new Point(16, 74);
        _itemNameBox.Size = new Size(itemCard.Width - 32, 28);
        _itemNameBox.Text = currentName;
        _itemNameBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        StyleTextBox(_itemNameBox);

        var countLabel = CreateFieldLabel(
            currentQuantity is null
                ? "Count override (blank = local read, 0 = empty)"
                : $"Count override (blank = local read, 0 = empty, current x{currentQuantity})",
            new Point(16, 114),
            new Size(itemCard.Width - 32, 28));
        countLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _countOverrideBox.Location = new Point(16, 146);
        _countOverrideBox.Size = new Size(124, 28);
        _countOverrideBox.Text = countOverride?.ToString() ?? string.Empty;
        StyleTextBox(_countOverrideBox);

        itemCard.Controls.AddRange([itemNameLabel, _itemNameBox, countLabel, _countOverrideBox]);

        if (currentQuantity is > 0)
        {
            var confirmCountButton = new Button
            {
                Text = $"Confirm x{currentQuantity}",
                Location = new Point(154, 144),
                Size = new Size(132, 30),
                Anchor = AnchorStyles.Top | AnchorStyles.Left
            };
            StyleButton(confirmCountButton);
            confirmCountButton.Click += (_, _) =>
            {
                _countOverrideBox.Text = currentQuantity.Value.ToString();
                _countOverrideBox.SelectAll();
                _countOverrideBox.Focus();
            };
            itemCard.Controls.Add(confirmCountButton);
        }

        var nextTop = itemCard.Bottom + 14;
        Panel? previewCard = null;
        if (hasCountPreview)
        {
            LoadCountPreviewImages();
            previewCard = CreateCard("Count crop preview", new Point(margin, nextTop), new Size(contentWidth, 166));

            var rawBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldBackground,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _rawCountPreviewImage
            };
            StylePreviewBox(rawBox);

            var cleanedBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = FieldBackground,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = _cleanedCountPreviewImage
            };
            StylePreviewBox(cleanedBox);

            var previewGrid = CreatePreviewGrid(rawBox, cleanedBox, previewCard.Width - 32);
            previewCard.Controls.Add(previewGrid);
            nextTop = previewCard.Bottom + 14;
        }

        Panel? suggestionsCard = null;
        if (hasSuggestions)
        {
            LoadSuggestionImages();
            suggestionsCard = CreateCard(
                "Icon suggestions",
                new Point(margin, nextTop),
                new Size(contentWidth, ClientSize.Height - nextTop - footerHeight - margin));
            suggestionsCard.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            _suggestionsList.Location = new Point(16, 44);
            _suggestionsList.Size = new Size(suggestionsCard.Width - 156, suggestionsCard.Height - 60);
            _suggestionsList.DrawMode = DrawMode.OwnerDrawFixed;
            _suggestionsList.ItemHeight = 50;
            _suggestionsList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _suggestionsList.BorderStyle = BorderStyle.FixedSingle;
            _suggestionsList.BackColor = FieldBackground;
            _suggestionsList.ForeColor = TextPrimary;
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
                Location = new Point(suggestionsCard.Width - 124, 44),
                Size = new Size(108, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            StyleButton(useSuggestionButton, primary: true);
            useSuggestionButton.Click += (_, _) => ApplySelectedSuggestion();
            suggestionsCard.Controls.AddRange([_suggestionsList, useSuggestionButton]);
        }

        var footer = new Panel
        {
            Location = new Point(0, ClientSize.Height - footerHeight),
            Size = new Size(ClientSize.Width, footerHeight),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = AppBackground
        };

        var saveButton = new Button
        {
            Text = "Save",
            Location = new Point(footer.Width - 218, 15),
            Size = new Size(96, 34),
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        StyleButton(saveButton, primary: true);
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
            Location = new Point(footer.Width - 112, 15),
            Size = new Size(94, 34),
            DialogResult = DialogResult.Cancel,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        StyleButton(cancelButton);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        footer.Controls.AddRange([saveButton, cancelButton]);

        Controls.AddRange([titleLabel, helperLabel, itemCard]);
        if (previewCard is not null)
        {
            Controls.Add(previewCard);
        }

        if (suggestionsCard is not null)
        {
            Controls.Add(suggestionsCard);
        }

        Controls.Add(footer);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryApplyDarkTitleBar();
    }

    private void TryApplyDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
            }
        }
        catch
        {
            // Older Windows builds may not support immersive dark title bars.
        }
    }

    private static Panel CreateCard(string title, Point location, Size size)
    {
        var card = new Panel
        {
            Location = location,
            Size = size,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = CardBackground
        };
        card.Paint += PaintCardBorder;

        card.Controls.Add(new Label
        {
            Text = title,
            Location = new Point(16, 12),
            Size = new Size(size.Width - 32, 28),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = AccentCyan,
            BackColor = CardBackground
        });

        return card;
    }

    private static Label CreateFieldLabel(string text, Point location, Size size)
    {
        return new Label
        {
            Text = text,
            Location = location,
            Size = size,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary,
            BackColor = CardBackground
        };
    }

    private static TableLayoutPanel CreatePreviewGrid(PictureBox rawBox, PictureBox cleanedBox, int width)
    {
        var grid = new TableLayoutPanel
        {
            Location = new Point(16, 48),
            Size = new Size(width, 100),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = CardBackground,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreatePreviewLabel("Raw"), 0, 0);
        grid.Controls.Add(CreatePreviewLabel("Cleaned"), 1, 0);
        grid.Controls.Add(CreatePreviewCell(rawBox), 0, 1);
        grid.Controls.Add(CreatePreviewCell(cleanedBox), 1, 1);
        return grid;
    }

    private static Label CreatePreviewLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSecondary,
            BackColor = CardBackground
        };
    }

    private static Panel CreatePreviewCell(PictureBox box)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(8),
            BackColor = FieldBackground
        };
        panel.Paint += PaintCardBorder;
        panel.Controls.Add(box);
        return panel;
    }

    private static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? AccentTeal : Color.FromArgb(25, 32, 40);
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? AccentCyan : BorderColor;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(28, 172, 169) : CardBackgroundAlt;
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(15, 120, 118) : Color.FromArgb(18, 24, 30);
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = FieldBackground;
        textBox.ForeColor = TextPrimary;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StylePreviewBox(PictureBox pictureBox)
    {
        pictureBox.BorderStyle = BorderStyle.None;
        pictureBox.BackColor = FieldBackground;
    }

    private static void PaintCardBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        using var pen = new Pen(BorderColor);
        var rect = new Rectangle(0, 0, control.ClientSize.Width - 1, control.ClientSize.Height - 1);
        e.Graphics.DrawRectangle(pen, rect);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        foreach (var image in _suggestionImages.Values)
        {
            image.Dispose();
        }

        _suggestionImages.Clear();
        _rawCountPreviewImage?.Dispose();
        _cleanedCountPreviewImage?.Dispose();
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

    private void LoadCountPreviewImages()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_countCropPreview?.RawPath) && File.Exists(_countCropPreview.RawPath))
            {
                _rawCountPreviewImage = CurrencyScanner.LoadBitmapWithoutFileLock(_countCropPreview.RawPath);
            }

            if (!string.IsNullOrWhiteSpace(_countCropPreview?.CleanedPath) && File.Exists(_countCropPreview.CleanedPath))
            {
                _cleanedCountPreviewImage = CurrencyScanner.LoadBitmapWithoutFileLock(_countCropPreview.CleanedPath);
            }
        }
        catch
        {
            _rawCountPreviewImage?.Dispose();
            _cleanedCountPreviewImage?.Dispose();
            _rawCountPreviewImage = null;
            _cleanedCountPreviewImage = null;
        }
    }

    private void SuggestionsList_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _suggestionsList.Items.Count)
        {
            return;
        }

        if (_suggestionsList.Items[e.Index] is not IconSuggestionListItem item)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var background = selected ? Color.FromArgb(20, 108, 113) : FieldBackground;
        var textColor = selected ? Color.White : TextPrimary;
        var metaColor = selected ? Color.FromArgb(217, 247, 245) : TextSecondary;

        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);

        var imageRect = new Rectangle(e.Bounds.Left + 5, e.Bounds.Top + 5, 36, 36);
        if (_suggestionImages.TryGetValue(item.Match.LocalPath, out var image))
        {
            e.Graphics.DrawImage(image, imageRect);
        }

        using var imageBorderPen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(imageBorderPen, imageRect);

        using var nameBrush = new SolidBrush(textColor);
        using var metaBrush = new SolidBrush(metaColor);
        using var nameFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var metaFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        var textLeft = imageRect.Right + 8;
        var textWidth = e.Bounds.Right - textLeft - 4;
        e.Graphics.DrawString(item.Match.ItemName, nameFont, nameBrush, new RectangleF(textLeft, e.Bounds.Top + 5, textWidth, 21), format);
        var meta = $"{item.Match.Confidence:0.000} {item.Match.SourceKind} {item.Match.Type} gap {item.Match.SecondBestGap:0.000}";
        e.Graphics.DrawString(meta, metaFont, metaBrush, new RectangleF(textLeft, e.Bounds.Top + 27, textWidth, 16), format);
        e.DrawFocusRectangle();
    }

    private sealed record IconSuggestionListItem(PoeNinjaIconMatch Match)
    {
        public override string ToString() => $"{Match.ItemName} ({Match.Confidence:0.000})";
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
