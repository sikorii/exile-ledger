namespace Poe2PriceChecker;

internal sealed class SettingsForm : Form
{
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
        ClientSize = new Size(640, 430);

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };

        tabs.TabPages.Add(BuildGeneralTab(state));
        tabs.TabPages.Add(BuildAiCountReaderTab(state));
        tabs.TabPages.Add(BuildDisplayOverlayTab(state));
        tabs.TabPages.Add(BuildDebugAdvancedTab(state));

        var closeButton = new Button
        {
            Text = "Close",
            Size = new Size(92, 30),
            Anchor = AnchorStyles.Right
        };
        closeButton.Click += (_, _) => Close();

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(0, 8, 12, 10),
            FlowDirection = FlowDirection.RightToLeft
        };
        footer.Controls.Add(closeButton);

        Controls.Add(tabs);
        Controls.Add(footer);
    }

    private TabPage BuildGeneralTab(SettingsFormState state)
    {
        var page = CreateTabPage("General");
        var panel = CreateStackPanel();

        panel.Controls.Add(CreateHeader("Application folders"));
        panel.Controls.Add(CreateReadOnlyPathRow("App data folder", state.AppDataPath, _actions.OpenAppDataFolder));
        panel.Controls.Add(CreateReadOnlyPathRow("Saved scan/config folder", state.ConfigPath, _actions.OpenConfigFolder));
        panel.Controls.Add(CreateStatusRow("Active saved-scan file", state.LatestStashScansPath));
        if (!string.IsNullOrWhiteSpace(state.MigrationSourceConfigPath))
        {
            panel.Controls.Add(CreateStatusRow("Migrated from", state.MigrationSourceConfigPath));
        }

        panel.Controls.Add(CreateHeader("Hotkeys"));
        panel.Controls.Add(CreateHotkeySection(state));

        page.Controls.Add(panel);
        return page;
    }

    private Control CreateHotkeySection(SettingsFormState state)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 570,
            Margin = new Padding(0, 0, 0, 12)
        };

        var statusLabel = new Label
        {
            Text = state.HotkeyStatus,
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 8)
        };

        var runeshapingBox = new TextBox
        {
            Text = state.RuneshapingHotkey,
            Dock = DockStyle.Fill
        };

        var scanBox = new TextBox
        {
            Text = state.ScanCurrentStashHotkey,
            Dock = DockStyle.Fill
        };

        var saveButton = new Button
        {
            Text = "Save Hotkeys",
            AutoSize = true,
            MinimumSize = new Size(110, 30),
            Margin = new Padding(0, 0, 8, 0)
        };
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

    private TabPage BuildAiCountReaderTab(SettingsFormState state)
    {
        var page = CreateTabPage("AI Count Reader");
        var panel = CreateStackPanel();

        panel.Controls.Add(CreateHeader("OpenAI status"));
        panel.Controls.Add(CreateOpenAiApiKeySection(state));
        panel.Controls.Add(CreateStatusRow("Count model", state.OpenAiCountModelStatus));
        panel.Controls.Add(CreateWrappedLabel("The API key value is never displayed here. AI count reads still use the existing prompt, strict JSON response expectations, apply rules, and debug cleanup behavior."));
        panel.Controls.Add(CreateButtonRow(("Open AI Count Debug Folder", _actions.OpenAiCountDebugFolder)));

        page.Controls.Add(panel);
        return page;
    }

    private Control CreateOpenAiApiKeySection(SettingsFormState state)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Width = 570,
            Margin = new Padding(0, 0, 0, 12)
        };

        var statusLabel = new Label
        {
            Text = state.OpenAiApiKeyStatus,
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 8)
        };

        var keyRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 570,
            Height = 34,
            Margin = new Padding(0, 0, 0, 4)
        };
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        keyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

        var keyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "Paste OpenAI API key"
        };

        var showKeyCheckBox = new CheckBox
        {
            Text = "Show",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        showKeyCheckBox.CheckedChanged += (_, _) => keyBox.UseSystemPasswordChar = !showKeyCheckBox.Checked;

        keyRow.Controls.Add(keyBox, 0, 0);
        keyRow.Controls.Add(showKeyCheckBox, 1, 0);

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0)
        };

        var saveButton = new Button
        {
            Text = "Save",
            AutoSize = true,
            MinimumSize = new Size(88, 30),
            Margin = new Padding(0, 0, 8, 0)
        };
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

    private TabPage BuildDisplayOverlayTab(SettingsFormState state)
    {
        var page = CreateTabPage("Display / Overlay");
        var panel = CreateStackPanel();

        panel.Controls.Add(CreateHeader("Manual visual layout editor"));

        var layoutEditorCheckBox = new CheckBox
        {
            Text = "Enable manual visual layout editor",
            Checked = state.ManualLayoutEditorEnabled,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8)
        };
        layoutEditorCheckBox.CheckedChanged += (_, _) => _actions.SetManualLayoutEditorEnabled(layoutEditorCheckBox.Checked);
        panel.Controls.Add(layoutEditorCheckBox);

        panel.Controls.Add(CreateWrappedLabel("The editor adjusts visual/layout bounds only. Detection and crop bounds are left unchanged."));
        panel.Controls.Add(CreateButtonRow(
            ("Save Layout Overrides", _actions.SaveLayoutOverrides),
            ("Reload Layout Overrides", _actions.ReloadLayoutOverrides)));
        panel.Controls.Add(CreateButtonRow(
            ("Reset Selected Slot", _actions.ResetSelectedSlot),
            ("Reset Current Tab", _actions.ResetCurrentTab)));

        page.Controls.Add(panel);
        return page;
    }

    private TabPage BuildDebugAdvancedTab(SettingsFormState state)
    {
        var page = CreateTabPage("Debug / Advanced");
        var panel = CreateStackPanel();

        panel.Controls.Add(CreateHeader("Count crop debug"));

        var saveCropsCheckBox = new CheckBox
        {
            Text = "Save count debug crops",
            Checked = state.SaveCountDebugCrops,
            AutoSize = true,
            Margin = new Padding(0, 2, 0, 8)
        };
        saveCropsCheckBox.CheckedChanged += (_, _) => _actions.SetSaveCountDebugCrops(saveCropsCheckBox.Checked);
        panel.Controls.Add(saveCropsCheckBox);

        panel.Controls.Add(CreateButtonRow(
            ("Review Count Crops Report", _actions.ReviewCountCrops),
            ("Open Count Crop Folder", _actions.OpenCountCropFolder)));

        panel.Controls.Add(CreateHeader("Debug folders"));
        panel.Controls.Add(CreateReadOnlyPathRow("Debug folder", state.DebugPath, _actions.OpenDebugFolder));
        panel.Controls.Add(CreateReadOnlyPathRow("Count crop folder", state.CountCropPath, _actions.OpenCountCropFolder));
        panel.Controls.Add(CreateReadOnlyPathRow("AI count debug folder", state.AiCountDebugPath, _actions.OpenAiCountDebugFolder));

        page.Controls.Add(panel);
        return page;
    }

    private static TabPage CreateTabPage(string text)
    {
        return new TabPage(text)
        {
            Padding = new Padding(14)
        };
    }

    private static FlowLayoutPanel CreateStackPanel()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
    }

    private static Label CreateHeader(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static Control CreateStatusRow(string label, string value)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Width = 570,
            Height = 30,
            Margin = new Padding(0, 0, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(new Label { Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0);
        return panel;
    }

    private static Control CreateReadOnlyPathRow(string label, string path, Action openAction)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Width = 570,
            Height = 34,
            Margin = new Padding(0, 0, 0, 8)
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
        var openButton = new Button
        {
            Text = "Open",
            Dock = DockStyle.Fill
        };
        openButton.Click += (_, _) => openAction();

        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
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
            Width = 570,
            Height = 34,
            Margin = new Padding(0, 0, 0, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
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
            Margin = new Padding(0, 0, 0, 12)
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
            MaximumSize = new Size(570, 0),
            Margin = new Padding(0, 0, 0, 12)
        };
    }
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
