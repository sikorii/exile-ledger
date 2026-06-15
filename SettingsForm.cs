namespace Poe2PriceChecker;

internal sealed class SettingsForm : Form
{
    private static readonly Color AppBackground = Color.FromArgb(16, 20, 24);
    private static readonly Color CardBackground = Color.FromArgb(24, 31, 38);
    private static readonly Color CardBackgroundAlt = Color.FromArgb(28, 35, 43);
    private static readonly Color BorderColor = Color.FromArgb(43, 52, 61);
    private static readonly Color TextPrimary = Color.FromArgb(236, 241, 244);
    private static readonly Color TextSecondary = Color.FromArgb(168, 179, 188);
    private static readonly Color AccentCyan = Color.FromArgb(83, 224, 218);
    private static readonly Color AccentTeal = Color.FromArgb(21, 148, 146);
    private static readonly Color AccentGold = Color.FromArgb(214, 171, 69);

    private readonly SettingsFormActions _actions;

    public SettingsForm(SettingsFormState state, SettingsFormActions actions)
    {
        _actions = actions;

        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 640);
        BackColor = AppBackground;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var tabShell = CreateDarkTabShell(
            ("General", BuildGeneralTab(state)),
            ("AI Count Reader", BuildAiCountReaderTab(state)),
            ("Display / Overlay", BuildDisplayOverlayTab(state)),
            ("Debug / Advanced", BuildDebugAdvancedTab(state)));

        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(92, 30),
            Anchor = AnchorStyles.Right
        };
        StyleButton(closeButton);
        closeButton.Click += (_, _) => Close();

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            Padding = new Padding(0, 10, 14, 12),
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = AppBackground
        };
        footer.Controls.Add(closeButton);

        Controls.Add(tabShell);
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

    private Control BuildGeneralTab(SettingsFormState state)
    {
        var panel = CreateStackPanel();

        var foldersCard = CreateCard("Application folders");
        foldersCard.Controls.Add(CreateReadOnlyPathRow("App data folder", state.AppDataPath, _actions.OpenAppDataFolder));
        foldersCard.Controls.Add(CreateReadOnlyPathRow("Saved scan/config folder", state.ConfigPath, _actions.OpenConfigFolder));
        foldersCard.Controls.Add(CreateStatusRow("Active saved-scan file", state.LatestStashScansPath));
        if (!string.IsNullOrWhiteSpace(state.MigrationSourceConfigPath))
        {
            foldersCard.Controls.Add(CreateStatusRow("Migrated from", state.MigrationSourceConfigPath));
        }
        panel.Controls.Add(foldersCard);

        var hotkeysCard = CreateCard("Hotkeys");
        hotkeysCard.Controls.Add(CreateHotkeySection(state));
        panel.Controls.Add(hotkeysCard);

        return panel;
    }

    private Control CreateHotkeySection(SettingsFormState state)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 660,
            BackColor = CardBackground,
            Margin = new Padding(0, 0, 0, 12)
        };

        var statusLabel = new Label
        {
            Text = state.HotkeyStatus,
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 8),
            ForeColor = TextSecondary,
            BackColor = CardBackground
        };

        var runeshapingBox = new TextBox
        {
            Text = state.RuneshapingHotkey,
            Dock = DockStyle.Fill
        };
        StyleTextBox(runeshapingBox);

        var scanBox = new TextBox
        {
            Text = state.ScanCurrentStashHotkey,
            Dock = DockStyle.Fill
        };
        StyleTextBox(scanBox);

        var saveButton = new Button
        {
            Text = "Save Hotkeys",
            AutoSize = true,
            MinimumSize = new Size(110, 30),
            Margin = new Padding(0, 0, 8, 0)
        };
        StyleButton(saveButton, primary: true);
        saveButton.Click += (_, _) =>
        {
            var result = _actions.SaveHotkeys(runeshapingBox.Text, scanBox.Text);
            statusLabel.Text = result.Status;
            if (result.Saved)
            {
                runeshapingBox.Text = result.RuneshapingHotkey;
                scanBox.Text = result.ScanCurrentStashHotkey;
            }
        };

        panel.Controls.Add(statusLabel);
        panel.Controls.Add(CreateTextBoxRow("Runeshaping", runeshapingBox));
        panel.Controls.Add(CreateTextBoxRow("Scan current stash", scanBox));
        panel.Controls.Add(saveButton);
        panel.Controls.Add(CreateWrappedLabel("Examples: F8, F7, Ctrl+Shift+R, Ctrl+Alt+S."));
        return panel;
    }

    private Control BuildAiCountReaderTab(SettingsFormState state)
    {
        var panel = CreateStackPanel();

        var openAiCard = CreateCard("OpenAI / AI count reading");
        openAiCard.Controls.Add(CreateOpenAiApiKeySection(state));
        openAiCard.Controls.Add(CreateStatusRow("Count model", state.OpenAiCountModelStatus));
        openAiCard.Controls.Add(CreateWrappedLabel("The API key value is never displayed here. AI count reads still use the existing prompt, strict JSON response expectations, apply rules, and debug cleanup behavior."));
        openAiCard.Controls.Add(CreateButtonRow(("Open AI Count Debug Folder", _actions.OpenAiCountDebugFolder)));
        panel.Controls.Add(openAiCard);

        return panel;
    }

    private Control CreateOpenAiApiKeySection(SettingsFormState state)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 660,
            BackColor = CardBackground,
            Margin = new Padding(0, 0, 0, 12)
        };

        var statusLabel = new Label
        {
            Text = state.OpenAiApiKeyStatus,
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 8),
            ForeColor = TextSecondary,
            BackColor = CardBackground
        };

        var keyRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 660,
            Height = 34,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = CardBackground
        };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        var keyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "Paste OpenAI API key"
        };
        StyleTextBox(keyBox);

        var showKeyCheckBox = new CheckBox
        {
            Text = "Show",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        StyleCheckBox(showKeyCheckBox);
        showKeyCheckBox.CheckedChanged += (_, _) => keyBox.UseSystemPasswordChar = !showKeyCheckBox.Checked;

        keyRow.Controls.Add(keyBox, 0, 0);
        keyRow.Controls.Add(showKeyCheckBox, 1, 0);

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0),
            BackColor = CardBackground
        };

        var saveButton = new Button
        {
            Text = "Save",
            AutoSize = true,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(0, 0, 8, 0)
        };
        StyleButton(saveButton, primary: true);
        saveButton.Click += async (_, _) =>
        {
            var status = await _actions.SaveOpenAiApiKeyAsync(keyBox.Text).ConfigureAwait(true);
            keyBox.Clear();
            keyBox.PlaceholderText = status == "OpenAI API key configured"
                ? "Stored key configured"
                : "Paste OpenAI API key";
            statusLabel.Text = status;
        };

        var clearButton = new Button
        {
            Text = "Clear",
            AutoSize = true,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(0, 0, 8, 0)
        };
        StyleButton(clearButton);
        clearButton.Click += async (_, _) =>
        {
            var status = await _actions.ClearOpenAiApiKeyAsync().ConfigureAwait(true);
            keyBox.Clear();
            keyBox.PlaceholderText = "Paste OpenAI API key";
            statusLabel.Text = status;
        };

        buttonRow.Controls.Add(saveButton);
        buttonRow.Controls.Add(clearButton);

        panel.Controls.Add(statusLabel);
        panel.Controls.Add(keyRow);
        panel.Controls.Add(buttonRow);
        return panel;
    }

    private Control BuildDisplayOverlayTab(SettingsFormState state)
    {
        var panel = CreateStackPanel();

        var editorCard = CreateCard("Manual visual layout editor");

        var layoutEditorCheckBox = new CheckBox
        {
            Text = "Enable manual visual layout editor",
            Checked = state.ManualLayoutEditorEnabled,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8)
        };
        StyleCheckBox(layoutEditorCheckBox);
        layoutEditorCheckBox.CheckedChanged += (_, _) => _actions.SetManualLayoutEditorEnabled(layoutEditorCheckBox.Checked);
        editorCard.Controls.Add(layoutEditorCheckBox);

        editorCard.Controls.Add(CreateWrappedLabel("The editor adjusts visual/layout bounds only. Detection and crop bounds are left unchanged."));
        editorCard.Controls.Add(CreateButtonRow(
            ("Save Layout Overrides", _actions.SaveLayoutOverrides),
            ("Reload Layout Overrides", _actions.ReloadLayoutOverrides)));
        editorCard.Controls.Add(CreateButtonRow(
            ("Reset Selected Slot", _actions.ResetSelectedSlot),
            ("Reset Current Tab", _actions.ResetCurrentTab)));
        panel.Controls.Add(editorCard);

        return panel;
    }

    private Control BuildDebugAdvancedTab(SettingsFormState state)
    {
        var panel = CreateStackPanel();

        var countCropCard = CreateCard("Count crop debug");

        var saveCropsCheckBox = new CheckBox
        {
            Text = "Save count debug crops",
            Checked = state.SaveCountDebugCrops,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8)
        };
        StyleCheckBox(saveCropsCheckBox);
        saveCropsCheckBox.CheckedChanged += (_, _) => _actions.SetSaveCountDebugCrops(saveCropsCheckBox.Checked);
        countCropCard.Controls.Add(saveCropsCheckBox);

        countCropCard.Controls.Add(CreateButtonRow(
            ("Review Count Crops Report", _actions.ReviewCountCrops),
            ("Open Count Crop Folder", _actions.OpenCountCropFolder)));
        panel.Controls.Add(countCropCard);

        var debugFoldersCard = CreateCard("Debug folders");
        debugFoldersCard.Controls.Add(CreateReadOnlyPathRow("Debug folder", state.DebugPath, _actions.OpenDebugFolder));
        debugFoldersCard.Controls.Add(CreateReadOnlyPathRow("Count crop folder", state.CountCropPath, _actions.OpenCountCropFolder));
        debugFoldersCard.Controls.Add(CreateReadOnlyPathRow("AI count debug folder", state.AiCountDebugPath, _actions.OpenAiCountDebugFolder));
        panel.Controls.Add(debugFoldersCard);

        return panel;
    }

    private static Panel CreateDarkTabShell(params (string Text, Control Content)[] tabs)
    {
        var shell = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 12, 14, 0),
            BackColor = AppBackground
        };

        var tabStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            Margin = new Padding(0),
            BackColor = AppBackground
        };

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 0),
            BackColor = AppBackground
        };

        var buttons = new List<Button>(tabs.Length);
        var contents = new List<Control>(tabs.Length);

        for (var index = 0; index < tabs.Length; index++)
        {
            var content = tabs[index].Content;
            content.Dock = DockStyle.Fill;
            content.Visible = false;
            content.BackColor = AppBackground;
            contentHost.Controls.Add(content);
            contents.Add(content);

            var button = CreateDarkTabButton(tabs[index].Text);
            var selectedIndex = index;
            button.Click += (_, _) => SelectDarkTab(buttons, contents, selectedIndex);
            tabStrip.Controls.Add(button);
            buttons.Add(button);
        }

        shell.Controls.Add(contentHost);
        shell.Controls.Add(tabStrip);

        if (buttons.Count > 0)
        {
            SelectDarkTab(buttons, contents, 0);
        }

        return shell;
    }

    private static Button CreateDarkTabButton(string text)
    {
        var font = new Font("Segoe UI", 9f, FontStyle.Bold);
        var width = Math.Max(104, TextRenderer.MeasureText(text, font).Width + 30);
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 34),
            Margin = new Padding(0, 0, 8, 0),
            Font = font,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        StyleDarkTabButton(button, selected: false);
        return button;
    }

    private static void SelectDarkTab(IReadOnlyList<Button> buttons, IReadOnlyList<Control> contents, int selectedIndex)
    {
        for (var index = 0; index < buttons.Count; index++)
        {
            var selected = index == selectedIndex;
            StyleDarkTabButton(buttons[index], selected);
            contents[index].Visible = selected;
            if (selected)
            {
                contents[index].BringToFront();
            }
        }
    }

    private static void StyleDarkTabButton(Button button, bool selected)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = selected ? Color.FromArgb(20, 108, 113) : CardBackground;
        button.ForeColor = selected ? Color.White : TextSecondary;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = selected ? AccentCyan : BorderColor;
        button.FlatAppearance.MouseOverBackColor = selected ? Color.FromArgb(24, 132, 137) : CardBackgroundAlt;
        button.FlatAppearance.MouseDownBackColor = selected ? Color.FromArgb(14, 86, 90) : Color.FromArgb(18, 24, 30);
    }

    private static FlowLayoutPanel CreateStackPanel()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0),
            BackColor = AppBackground
        };
    }

    private static FlowLayoutPanel CreateCard(string title)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 690,
            Padding = new Padding(16, 14, 16, 14),
            Margin = new Padding(0, 0, 0, 14),
            BackColor = CardBackground
        };
        panel.Paint += PaintCardBorder;
        panel.Controls.Add(CreateHeader(title));
        return panel;
    }

    private static Label CreateHeader(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
            ForeColor = AccentCyan,
            BackColor = CardBackground
        };
    }

    private static Control CreateStatusRow(string label, string value)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 660,
            Height = 30,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = CardBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(CreateRowLabel(label), 0, 0);
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = TextPrimary, BackColor = CardBackground }, 1, 0);
        return panel;
    }

    private static Control CreateReadOnlyPathRow(string label, string path, Action openAction)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Width = 660,
            Height = 34,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = CardBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        var pathBox = new TextBox
        {
            Text = path,
            ReadOnly = true,
            Dock = DockStyle.Fill
        };
        StyleTextBox(pathBox);
        var openButton = new Button
        {
            Text = "Open",
            Dock = DockStyle.Fill
        };
        StyleButton(openButton);
        openButton.Click += (_, _) => openAction();

        panel.Controls.Add(CreateRowLabel(label), 0, 0);
        panel.Controls.Add(pathBox, 1, 0);
        panel.Controls.Add(openButton, 2, 0);
        return panel;
    }

    private static Control CreateTextBoxRow(string label, TextBox textBox)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 660,
            Height = 34,
            Margin = new Padding(0, 0, 0, 4),
            BackColor = CardBackground
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        StyleTextBox(textBox);
        panel.Controls.Add(CreateRowLabel(label), 0, 0);
        panel.Controls.Add(textBox, 1, 0);
        return panel;
    }

    private static Control CreateButtonRow(params (string Text, Action Action)[] buttons)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = CardBackground
        };

        foreach (var (text, action) in buttons)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(120, 30),
                Margin = new Padding(0, 0, 8, 0)
            };
            StyleButton(button);
            button.Click += (_, _) => action();
            panel.Controls.Add(button);
        }

        return panel;
    }

    private static Label CreateWrappedLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(660, 0),
            Margin = new Padding(0, 0, 0, 12),
            ForeColor = TextSecondary,
            BackColor = CardBackground
        };
    }

    private static Label CreateRowLabel(string text)
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

    private static void StyleButton(Button button, bool primary = false)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? AccentTeal : Color.FromArgb(25, 32, 40);
        button.ForeColor = primary ? Color.White : TextPrimary;
        button.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? AccentCyan : BorderColor;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(28, 172, 169) : Color.FromArgb(34, 43, 52);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(15, 120, 118) : Color.FromArgb(18, 24, 30);
    }

    private static void StyleTextBox(TextBox textBox)
    {
        textBox.BackColor = Color.FromArgb(13, 17, 21);
        textBox.ForeColor = TextPrimary;
        textBox.BorderStyle = BorderStyle.FixedSingle;
    }

    private static void StyleCheckBox(CheckBox checkBox)
    {
        checkBox.ForeColor = TextPrimary;
        checkBox.BackColor = CardBackground;
        checkBox.FlatStyle = FlatStyle.Flat;
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

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}

internal sealed record SettingsFormState(
    bool ManualLayoutEditorEnabled,
    bool SaveCountDebugCrops,
    string OpenAiApiKeyStatus,
    string OpenAiCountModelStatus,
    string RuneshapingHotkey,
    string ScanCurrentStashHotkey,
    string HotkeyStatus,
    string AppDataPath,
    string ConfigPath,
    string LatestStashScansPath,
    string DebugPath,
    string CountCropPath,
    string AiCountDebugPath,
    string? MigrationSourceConfigPath);

internal sealed record SettingsFormActions(
    Action<bool> SetManualLayoutEditorEnabled,
    Action SaveLayoutOverrides,
    Action ReloadLayoutOverrides,
    Action ResetSelectedSlot,
    Action ResetCurrentTab,
    Action<bool> SetSaveCountDebugCrops,
    Action ReviewCountCrops,
    Func<string, Task<string>> SaveOpenAiApiKeyAsync,
    Func<Task<string>> ClearOpenAiApiKeyAsync,
    Func<string, string, HotkeySettingsSaveResult> SaveHotkeys,
    Action OpenAppDataFolder,
    Action OpenConfigFolder,
    Action OpenDebugFolder,
    Action OpenCountCropFolder,
    Action OpenAiCountDebugFolder);
