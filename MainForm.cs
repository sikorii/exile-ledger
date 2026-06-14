using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace Poe2PriceChecker;

internal sealed class MainForm : Form
{
    private const int RuneshapingHotkeyId = 9001;
    private const int CurrencyHotkeyId = 9002;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private static readonly ScanModeOption[] ScanModes =
    [
        new(FixedStashScannerProfiles.Currency.Label, FixedStashScannerProfiles.Currency.Key, ScanModeKind.CurrencyStash, FixedStashScannerProfiles.Currency),
        new(FixedStashScannerProfiles.AugmentRunes.Label, FixedStashScannerProfiles.AugmentRunes.Key, ScanModeKind.AugmentRunes, FixedStashScannerProfiles.AugmentRunes),
        new(FixedStashScannerProfiles.KalguuranRunes.Label, FixedStashScannerProfiles.KalguuranRunes.Key, ScanModeKind.KalguuranRunes, FixedStashScannerProfiles.KalguuranRunes),
        new(FixedStashScannerProfiles.Abyss.Label, FixedStashScannerProfiles.Abyss.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Abyss),
        new(FixedStashScannerProfiles.Delirium.Label, FixedStashScannerProfiles.Delirium.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Delirium),
        new(FixedStashScannerProfiles.Expedition.Label, FixedStashScannerProfiles.Expedition.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Expedition),
        new(FixedStashScannerProfiles.Ritual.Label, FixedStashScannerProfiles.Ritual.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Ritual),
        new(FixedStashScannerProfiles.BreachCatalysts.Label, FixedStashScannerProfiles.BreachCatalysts.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.BreachCatalysts),
        new(FixedStashScannerProfiles.Fragments.Label, FixedStashScannerProfiles.Fragments.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Fragments),
        new(FixedStashScannerProfiles.SoulCores.Label, FixedStashScannerProfiles.SoulCores.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.SoulCores),
        new(FixedStashScannerProfiles.Idols.Label, FixedStashScannerProfiles.Idols.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Idols),
        new(FixedStashScannerProfiles.AncientAugments.Label, FixedStashScannerProfiles.AncientAugments.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.AncientAugments),
        new(FixedStashScannerProfiles.Essence.Label, FixedStashScannerProfiles.Essence.Key, ScanModeKind.GenericFixedStash, FixedStashScannerProfiles.Essence)
    ];

    private readonly RuneshapingScanner _scanner;
    private readonly CurrencyScanner _currencyScanner;
    private readonly CurrencyMappingStore _currencyMappingStore;
    private readonly AugmentRuneScanner _runeScanner;
    private readonly CurrencyMappingStore _runeMappingStore;
    private readonly KalguuranRuneScanner _kalguuranRuneScanner;
    private readonly CurrencyMappingStore _kalguuranRuneMappingStore;
    private readonly Dictionary<string, CurrencyMappingStore> _genericMappingStores;
    private readonly Dictionary<string, FixedStashScanner> _genericScanners;
    private readonly StashLayoutSettingsStore _layoutSettingsStore;
    private readonly SlotLayoutOverrideStore _slotLayoutOverrideStore;
    private readonly LatestStashScanStore _latestScanStore;
    private readonly OpenAiApiKeyStore _openAiApiKeyStore;
    private readonly OpenAiVisionHelper _openAiVisionHelper;
    private readonly AiCountReader _aiCountReader;
    private readonly PoeNinjaIconCache _iconCache;
    private readonly OverlayForm _overlay = new();
    private readonly Button _runeshapingButton = new();
    private readonly ComboBox _modeComboBox = new();
    private readonly CheckBox _insideFolderCheckBox = new();
    private readonly Button _scanButton = new();
    private readonly Button _testButton = new();
    private readonly Button _refreshButton = new();
    private readonly Button _captureTabButton = new();
    private readonly Button _aiAnalyzeButton = new();
    private readonly Button _refreshIconsButton = new();
    private readonly Button _copySummaryButton = new();
    private readonly CheckBox _editLayoutCheckBox = new();
    private readonly Button _saveLayoutButton = new();
    private readonly Button _reloadLayoutButton = new();
    private readonly Button _resetSelectedSlotButton = new();
    private readonly Button _resetCurrentTabButton = new();
    private readonly CheckBox _saveCountDebugCropsCheckBox = new();
    private readonly Button _reviewCountCropsButton = new();
    private readonly Button _aiReadCountsButton = new();
    private readonly Label _statusLabel = new();
    private readonly Label _totalStashValueLabel = new();
    private readonly TextBox _detailsBox = new();
    private readonly PictureBox _stashPictureBox = new();
    private readonly MenuStrip _menuStrip = new();
    private readonly ToolStripMenuItem _manualLayoutEditorMenuItem = new();
    private readonly ToolStripMenuItem _saveLayoutMenuItem = new();
    private readonly ToolStripMenuItem _reloadLayoutMenuItem = new();
    private readonly ToolStripMenuItem _resetSelectedSlotMenuItem = new();
    private readonly ToolStripMenuItem _resetCurrentTabMenuItem = new();
    private readonly ToolStripMenuItem _recalculateValuesMenuItem = new();
    private readonly ToolStripMenuItem _clearCurrentScanMenuItem = new();
    private readonly ToolStripMenuItem _openScreenshotMenuItem = new();
    private readonly ToolStripMenuItem _scanCurrentStashMenuItem = new();
    private readonly ToolStripMenuItem _aiReadCountsMenuItem = new();
    private readonly ToolStripMenuItem _scanRuneshapingMenuItem = new();

    private bool _scanInProgress;
    private readonly Dictionary<string, CurrencyScanResult> _savedCurrencyResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuneScanResult> _savedRuneResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FixedStashScanResult> _savedGenericResults = new(StringComparer.OrdinalIgnoreCase);
    private CurrencyScanResult? _lastCurrencyResult;
    private RuneScanResult? _lastRuneResult;
    private RuneScanResult? _lastKalguuranRuneResult;
    private FixedStashScanResult? _lastGenericResult;
    private Image? _stashImage;
    private SlotLayoutOverrides _slotLayoutOverrides;
    private int? _selectedLayoutSlotIndex;
    private string? _lastScreenshotDirectory;

    public MainForm()
    {
        _scanner = new RuneshapingScanner(AppPaths.DebugDirectory);
        _currencyMappingStore = new CurrencyMappingStore(FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.Currency.MappingFileName));
        _currencyScanner = new CurrencyScanner(AppPaths.DebugDirectory, _currencyMappingStore);
        _runeMappingStore = new CurrencyMappingStore(
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.AugmentRunes.MappingFileName),
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.AugmentRunes.CountOverrideFileName));
        _layoutSettingsStore = new StashLayoutSettingsStore(AppPaths.ConfigFile("stash-layout-settings.json"));
        _slotLayoutOverrideStore = new SlotLayoutOverrideStore(AppPaths.SlotLayoutOverridesPath);
        _slotLayoutOverrides = _slotLayoutOverrideStore.Load();
        _latestScanStore = new LatestStashScanStore(AppPaths.LatestStashScansPath);
        _openAiApiKeyStore = new OpenAiApiKeyStore(AppPaths.ConfigFile("secrets.json"));
        _runeScanner = new AugmentRuneScanner(AppPaths.DebugDirectory, _runeMappingStore);
        _kalguuranRuneMappingStore = new CurrencyMappingStore(
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.KalguuranRunes.MappingFileName),
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.KalguuranRunes.CountOverrideFileName));
        _kalguuranRuneScanner = new KalguuranRuneScanner(AppPaths.DebugDirectory, _kalguuranRuneMappingStore);
        _genericMappingStores = ScanModes
            .Where(mode => mode.Kind == ScanModeKind.GenericFixedStash && mode.Profile is not null)
            .ToDictionary(
                mode => mode.Key,
                mode => new CurrencyMappingStore(
                    FixedStashScannerProfiles.ConfigPath(mode.Profile!.MappingFileName),
                    FixedStashScannerProfiles.ConfigPath(mode.Profile.CountOverrideFileName)),
                StringComparer.OrdinalIgnoreCase);
        _genericScanners = ScanModes
            .Where(mode => mode.Kind == ScanModeKind.GenericFixedStash && mode.Profile is not null)
            .ToDictionary(
                mode => mode.Key,
                mode => new FixedStashScanner(
                    AppPaths.DebugDirectory,
                    _genericMappingStores[mode.Key],
                    mode.Profile!),
                StringComparer.OrdinalIgnoreCase);
        _openAiVisionHelper = new OpenAiVisionHelper(Path.Combine(AppPaths.DebugDirectory, "ai-stash-analysis"), _openAiApiKeyStore);
        _aiCountReader = new AiCountReader(Path.Combine(AppPaths.DebugDirectory, "ai-counts"), _openAiApiKeyStore);
        _iconCache = PoeNinjaIconCache.CreateDefault();
        _overlay.Dismissed += (_, _) => _scanner.ClearMergedRuneshapingRewards();
        BuildUi();
        LoadPersistedLatestScans();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!RegisterHotKey(Handle, RuneshapingHotkeyId, ModNoRepeat, (uint)Keys.F8))
        {
            _statusLabel.Text = "F8 hotkey unavailable. Use the Scan Runeshaping button.";
        }

        if (!RegisterHotKey(Handle, CurrencyHotkeyId, ModNoRepeat, (uint)Keys.F7))
        {
            _statusLabel.Text = "F7 hotkey unavailable. Use the Scan Currency button.";
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, RuneshapingHotkeyId);
        UnregisterHotKey(Handle, CurrencyHotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == RuneshapingHotkeyId)
        {
            _ = ScanLiveAsync();
            return;
        }

        if (m.Msg == WmHotkey && m.WParam.ToInt32() == CurrencyHotkeyId)
        {
            _ = ScanSelectedStashModeAsync();
            return;
        }

        base.WndProc(ref m);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        return TryApplyLayoutEditorKey(keyData) || base.ProcessCmdKey(ref msg, keyData);
    }

    private void BuildUi()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "POE2 Price Checker";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ClientSize = new Size(1280, 840);
        MinimumSize = new Size(980, 680);
        TopMost = false;
        BuildMenuStrip();

        var title = new Label
        {
            Text = "POE2 Price Checker",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Location = new Point(18, 38),
            AutoSize = true
        };

        _runeshapingButton.Text = "Runeshaping";
        _runeshapingButton.Location = new Point(22, 86);
        _runeshapingButton.Size = new Size(120, 34);
        _runeshapingButton.Click += async (_, _) => await ScanLiveAsync();

        _modeComboBox.Location = new Point(154, 87);
        _modeComboBox.Size = new Size(205, 28);
        _modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeComboBox.Items.AddRange(ScanModes.Cast<object>().ToArray());
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.SelectedIndexChanged += (_, _) =>
        {
            _selectedLayoutSlotIndex = null;
            LoadSelectedModeFolderSetting();
            ShowSavedScanForSelectedMode();
            UpdateLayoutEditorControls();
        };

        _insideFolderCheckBox.Text = "Folder";
        _insideFolderCheckBox.Location = new Point(372, 85);
        _insideFolderCheckBox.AutoSize = true;
        _insideFolderCheckBox.Padding = new Padding(0, 5, 0, 5);
        _insideFolderCheckBox.CheckedChanged += (_, _) => SaveSelectedModeFolderSetting();

        _scanButton.Text = "Scan Stash";
        _scanButton.Location = new Point(466, 86);
        _scanButton.Size = new Size(100, 34);
        _scanButton.Click += async (_, _) => await ScanSelectedStashModeAsync();

        _refreshButton.Text = "Refresh Prices";
        _refreshButton.Location = new Point(578, 86);
        _refreshButton.Size = new Size(130, 34);
        _refreshButton.Click += async (_, _) => await RefreshPricesAsync();

        _testButton.Text = "Test";
        _testButton.Location = new Point(720, 86);
        _testButton.Size = new Size(86, 34);
        _testButton.Click += async (_, _) => await ScanTestScreenshotAsync();

        _captureTabButton.Text = "Capture Stash";
        _captureTabButton.Location = new Point(818, 86);
        _captureTabButton.Size = new Size(128, 34);
        _captureTabButton.Click += (_, _) => CaptureStashTabReference();

        _aiAnalyzeButton.Text = "AI Layout";
        _aiAnalyzeButton.Location = new Point(958, 86);
        _aiAnalyzeButton.Size = new Size(112, 34);
        _aiAnalyzeButton.Visible = false;
        _aiAnalyzeButton.Click += async (_, _) => await AnalyzeSelectedStashWithAiAsync();

        _refreshIconsButton.Text = "Icons";
        _refreshIconsButton.Location = new Point(958, 86);
        _refreshIconsButton.Size = new Size(72, 34);
        _refreshIconsButton.Enabled = false;
        _refreshIconsButton.Visible = false;
        _refreshIconsButton.Click += async (_, _) => await RefreshIconCacheAsync();

        _copySummaryButton.Text = "Copy";
        _copySummaryButton.Location = new Point(720, 86);
        _copySummaryButton.Size = new Size(74, 34);
        _copySummaryButton.Click += (_, _) => CopyCurrentSummary();

        _editLayoutCheckBox.Text = "Edit Layout";
        _editLayoutCheckBox.Location = new Point(22, 104);
        _editLayoutCheckBox.AutoSize = true;
        _editLayoutCheckBox.Padding = new Padding(0, 5, 0, 5);
        _editLayoutCheckBox.CheckedChanged += (_, _) =>
        {
            if (!_editLayoutCheckBox.Checked)
            {
                _selectedLayoutSlotIndex = null;
            }
            else
            {
                _stashPictureBox.Focus();
            }

            UpdateLayoutEditorControls();
            _stashPictureBox.Invalidate();
            if (_manualLayoutEditorMenuItem.Checked != _editLayoutCheckBox.Checked)
            {
                _manualLayoutEditorMenuItem.Checked = _editLayoutCheckBox.Checked;
            }
        };

        _saveLayoutButton.Text = "Save Layout";
        _saveLayoutButton.Location = new Point(132, 104);
        _saveLayoutButton.Size = new Size(104, 30);
        _saveLayoutButton.Click += (_, _) => SaveLayoutOverrides();

        _reloadLayoutButton.Text = "Reload Layout";
        _reloadLayoutButton.Location = new Point(246, 104);
        _reloadLayoutButton.Size = new Size(112, 30);
        _reloadLayoutButton.Click += (_, _) => ReloadLayoutOverrides();

        _resetSelectedSlotButton.Text = "Reset Selected Slot";
        _resetSelectedSlotButton.Location = new Point(368, 104);
        _resetSelectedSlotButton.Size = new Size(140, 30);
        _resetSelectedSlotButton.Click += (_, _) => ResetSelectedLayoutSlot();

        _resetCurrentTabButton.Text = "Reset Current Tab";
        _resetCurrentTabButton.Location = new Point(518, 104);
        _resetCurrentTabButton.Size = new Size(130, 30);
        _resetCurrentTabButton.Click += (_, _) => ResetCurrentLayoutTab();

        _saveCountDebugCropsCheckBox.Text = "Count Crops";
        _saveCountDebugCropsCheckBox.Location = new Point(662, 104);
        _saveCountDebugCropsCheckBox.AutoSize = true;
        _saveCountDebugCropsCheckBox.Padding = new Padding(0, 5, 0, 5);
        _saveCountDebugCropsCheckBox.Checked = CountCropDebugSettings.SaveCountDebugCrops;
        _saveCountDebugCropsCheckBox.CheckedChanged += (_, _) =>
        {
            CountCropDebugSettings.SaveCountDebugCrops = _saveCountDebugCropsCheckBox.Checked;
        };

        _reviewCountCropsButton.Text = "Review Crops";
        _reviewCountCropsButton.Location = new Point(772, 104);
        _reviewCountCropsButton.Size = new Size(116, 30);
        _reviewCountCropsButton.Click += (_, _) => GenerateCountCropReviewReport();

        _aiReadCountsButton.Text = "AI Read Counts";
        _aiReadCountsButton.Location = new Point(806, 86);
        _aiReadCountsButton.Size = new Size(128, 30);
        _aiReadCountsButton.Click += async (_, _) => await ReadCountsWithAiAsync();

        _statusLabel.Text = "Runeshaping is separate. Choose a stash mode, then Scan Stash. Hotkeys: F8 Runeshaping, F7 selected stash.";
        _statusLabel.Location = new Point(22, 140);
        _statusLabel.Size = new Size(1200, 24);

        _totalStashValueLabel.Text = "All scanned: 0 tabs | 0ex / 0div";
        _totalStashValueLabel.Location = new Point(500, 38);
        _totalStashValueLabel.Size = new Size(740, 34);
        _totalStashValueLabel.TextAlign = ContentAlignment.MiddleRight;
        _totalStashValueLabel.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        _totalStashValueLabel.ForeColor = Color.FromArgb(92, 255, 124);
        _totalStashValueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _totalStashValueLabel.AutoEllipsis = true;

        _stashPictureBox.Location = new Point(22, 174);
        _stashPictureBox.Size = new Size(780, 622);
        _stashPictureBox.BorderStyle = BorderStyle.FixedSingle;
        _stashPictureBox.BackColor = Color.FromArgb(24, 24, 24);
        _stashPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        _stashPictureBox.TabStop = true;
        _stashPictureBox.Paint += StashPictureBox_Paint;
        _stashPictureBox.MouseClick += StashPictureBox_MouseClick;
        _stashPictureBox.KeyDown += StashPictureBox_KeyDown;

        _detailsBox.Location = new Point(820, 174);
        _detailsBox.Size = new Size(420, 622);
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Vertical;
        _detailsBox.WordWrap = true;
        _detailsBox.Font = new Font("Segoe UI", 10f);
        _detailsBox.BackColor = Color.FromArgb(18, 18, 18);
        _detailsBox.ForeColor = Color.Gainsboro;
        _detailsBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        _stashPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange([_menuStrip, title, _runeshapingButton, _modeComboBox, _insideFolderCheckBox, _scanButton, _refreshButton, _copySummaryButton, _aiReadCountsButton, _statusLabel, _totalStashValueLabel, _stashPictureBox, _detailsBox]);
        MainMenuStrip = _menuStrip;
        LoadSelectedModeFolderSetting();
        UpdateLayoutEditorControls();
        UpdateAllScannedTabsTotal();
    }

    private void BuildMenuStrip()
    {
        _menuStrip.Items.Clear();

        var fileMenu = new ToolStripMenuItem("&File");
        _openScreenshotMenuItem.Text = "Open Screenshot...";
        _openScreenshotMenuItem.Click += async (_, _) => await OpenScreenshotAsync();
        fileMenu.DropDownItems.Add(_openScreenshotMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(CreateMenuItem("Open App Data Folder", (_, _) => OpenFolder(AppPaths.RootDirectory)));
        fileMenu.DropDownItems.Add(CreateMenuItem("Open Saved Scan Data Folder", (_, _) => OpenFolder(AppPaths.ConfigDirectory)));
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(CreateMenuItem("E&xit", (_, _) => Close()));

        var scanMenu = new ToolStripMenuItem("&Scan");
        _scanRuneshapingMenuItem.Text = "Scan Runeshaping";
        _scanRuneshapingMenuItem.Click += async (_, _) => await ScanLiveAsync();
        _scanCurrentStashMenuItem.Text = "Scan Current Stash";
        _scanCurrentStashMenuItem.Click += async (_, _) => await ScanSelectedStashModeAsync();
        _aiReadCountsMenuItem.Text = "AI Read Counts";
        _aiReadCountsMenuItem.Click += async (_, _) => await ReadCountsWithAiAsync();
        _recalculateValuesMenuItem.Text = "Recalculate Values";
        _recalculateValuesMenuItem.Click += async (_, _) => await RecalculateCurrentValuesAsync();
        _clearCurrentScanMenuItem.Text = "Clear Current View";
        _clearCurrentScanMenuItem.Click += (_, _) => ClearCurrentScanView();
        scanMenu.DropDownItems.AddRange([
            _scanRuneshapingMenuItem,
            _scanCurrentStashMenuItem,
            _aiReadCountsMenuItem,
            new ToolStripSeparator(),
            _recalculateValuesMenuItem,
            _clearCurrentScanMenuItem
        ]);

        var toolsMenu = new ToolStripMenuItem("&Tools");
        _manualLayoutEditorMenuItem.Text = "Manual Visual Layout Editor";
        _manualLayoutEditorMenuItem.CheckOnClick = true;
        _manualLayoutEditorMenuItem.CheckedChanged += (_, _) => SetLayoutEditorEnabled(_manualLayoutEditorMenuItem.Checked);
        _saveLayoutMenuItem.Text = "Save Layout Overrides";
        _saveLayoutMenuItem.Click += (_, _) => SaveLayoutOverrides();
        _reloadLayoutMenuItem.Text = "Reload Layout Overrides";
        _reloadLayoutMenuItem.Click += (_, _) => ReloadLayoutOverrides();
        _resetSelectedSlotMenuItem.Text = "Reset Selected Slot";
        _resetSelectedSlotMenuItem.Click += (_, _) => ResetSelectedLayoutSlot();
        _resetCurrentTabMenuItem.Text = "Reset Current Tab";
        _resetCurrentTabMenuItem.Click += (_, _) => ResetCurrentLayoutTab();
        toolsMenu.DropDownItems.AddRange([
            _manualLayoutEditorMenuItem,
            _saveLayoutMenuItem,
            _reloadLayoutMenuItem,
            _resetSelectedSlotMenuItem,
            _resetCurrentTabMenuItem,
            new ToolStripSeparator(),
            CreateMenuItem("Capture Stash Reference", (_, _) => CaptureStashTabReference()),
            CreateMenuItem("Scan Test Screenshot", async (_, _) => await ScanTestScreenshotAsync()),
            CreateMenuItem("AI Layout Helper", async (_, _) => await AnalyzeSelectedStashWithAiAsync()),
            CreateMenuItem("Refresh Icon Cache", async (_, _) => await RefreshIconCacheAsync()),
            new ToolStripSeparator(),
            CreateMenuItem("Review Count Crops Report", (_, _) => GenerateCountCropReviewReport()),
            CreateMenuItem("Open Count Crop Folder", (_, _) => OpenFolder(Path.Combine(AppPaths.DebugDirectory, "count-crops"))),
            CreateMenuItem("Open AI Count Debug Folder", (_, _) => OpenFolder(Path.Combine(AppPaths.DebugDirectory, "ai-counts"))),
            CreateMenuItem("Open Debug Folder", (_, _) => OpenFolder(AppPaths.DebugDirectory))
        ]);

        var settingsMenu = new ToolStripMenuItem("&Settings");
        settingsMenu.DropDownItems.Add(CreateMenuItem("Open Settings...", (_, _) => OpenSettingsDialog()));

        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add(CreateMenuItem("About", (_, _) => ShowAboutDialog()));

        _menuStrip.Items.AddRange([fileMenu, scanMenu, toolsMenu, settingsMenu, helpMenu]);
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += onClick;
        return item;
    }

    private void OpenSettingsDialog()
    {
        using var form = new SettingsForm(
            new SettingsFormState(
                _editLayoutCheckBox.Checked,
                CountCropDebugSettings.SaveCountDebugCrops,
                ResolveOpenAiApiKeyStatus(),
                ResolveOpenAiCountModelStatus(),
                AppPaths.RootDirectory,
                AppPaths.ConfigDirectory,
                AppPaths.LatestStashScansPath,
                AppPaths.DebugDirectory,
                Path.Combine(AppPaths.DebugDirectory, "count-crops"),
                Path.Combine(AppPaths.DebugDirectory, "ai-counts"),
                AppPaths.MigrationSourceConfigDirectory),
            new SettingsFormActions(
                SetLayoutEditorEnabled,
                SaveLayoutOverrides,
                ReloadLayoutOverrides,
                ResetSelectedLayoutSlot,
                ResetCurrentLayoutTab,
                SetSaveCountDebugCrops,
                GenerateCountCropReviewReport,
                SaveOpenAiApiKeyFromSettingsAsync,
                ClearOpenAiApiKeyFromSettingsAsync,
                () => OpenFolder(AppPaths.RootDirectory),
                () => OpenFolder(AppPaths.ConfigDirectory),
                () => OpenFolder(AppPaths.DebugDirectory),
                () => OpenFolder(Path.Combine(AppPaths.DebugDirectory, "count-crops")),
                () => OpenFolder(Path.Combine(AppPaths.DebugDirectory, "ai-counts"))));

        form.ShowDialog(this);
    }

    private async Task<string> SaveOpenAiApiKeyFromSettingsAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "No OpenAI API key configured";
        }

        try
        {
            await _openAiApiKeyStore.SaveOpenAiApiKeyAsync(apiKey).ConfigureAwait(true);
            _statusLabel.Text = "OpenAI API key configured.";
            return ResolveOpenAiApiKeyStatus();
        }
        catch
        {
            _statusLabel.Text = "OpenAI API key save failed.";
            return "OpenAI API key save failed";
        }
    }

    private async Task<string> ClearOpenAiApiKeyFromSettingsAsync()
    {
        try
        {
            await _openAiApiKeyStore.ClearOpenAiApiKeyAsync().ConfigureAwait(true);
            var status = ResolveOpenAiApiKeyStatus();
            _statusLabel.Text = status;
            return status;
        }
        catch
        {
            _statusLabel.Text = "OpenAI API key clear failed.";
            return "OpenAI API key clear failed";
        }
    }

    private string ResolveOpenAiApiKeyStatus()
    {
        if (_openAiApiKeyStore.StoredOpenAiApiKeyCouldNotBeRead())
        {
            return "Saved key could not be read, please re-enter it";
        }

        return _openAiApiKeyStore.GetConfiguredSource() switch
        {
            OpenAiApiKeyConfigurationSource.Stored => "OpenAI API key configured",
            OpenAiApiKeyConfigurationSource.Environment => "Using OPENAI_API_KEY environment variable",
            _ => "No OpenAI API key configured"
        };
    }

    private static string ResolveOpenAiCountModelStatus()
    {
        var overrideModel = Environment.GetEnvironmentVariable("OPENAI_COUNT_MODEL");
        return string.IsNullOrWhiteSpace(overrideModel)
            ? $"Default: {AiCountReader.DefaultModel}"
            : $"OPENAI_COUNT_MODEL override: {overrideModel}";
    }

    private void ShowAboutDialog()
    {
        var version = Application.ProductVersion;
        MessageBox.Show(
            this,
            $"POE2 Price Checker\r\nVersion: {version}\r\nBuild path: {AppContext.BaseDirectory}\r\nApp data: {AppPaths.RootDirectory}",
            "About POE2 Price Checker",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OpenFolder(string folderPath)
    {
        try
        {
            Directory.CreateDirectory(folderPath);
            Process.Start(new ProcessStartInfo(folderPath) { UseShellExecute = true });
            _statusLabel.Text = $"Opened folder: {folderPath}";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Open folder failed.";
            _detailsBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "Open Folder Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetLayoutEditorEnabled(bool enabled)
    {
        if (_editLayoutCheckBox.Checked != enabled)
        {
            _editLayoutCheckBox.Checked = enabled;
        }
        else
        {
            if (!enabled)
            {
                _selectedLayoutSlotIndex = null;
            }
            else
            {
                _stashPictureBox.Focus();
            }

            UpdateLayoutEditorControls();
            _stashPictureBox.Invalidate();
        }

        if (_manualLayoutEditorMenuItem.Checked != enabled)
        {
            _manualLayoutEditorMenuItem.Checked = enabled;
        }
    }

    private void SetSaveCountDebugCrops(bool enabled)
    {
        if (_saveCountDebugCropsCheckBox.Checked != enabled)
        {
            _saveCountDebugCropsCheckBox.Checked = enabled;
        }

        CountCropDebugSettings.SaveCountDebugCrops = enabled;
    }

    private void GenerateCountCropReviewReport()
    {
        try
        {
            var result = CountCropReviewReport.Generate(AppPaths.RootDirectory);
            _statusLabel.Text = $"Count crop review generated: {result.CropCount} crops, {result.SuspectCount} suspect.";
            _detailsBox.Text = string.Join(Environment.NewLine, [
                "Count crop review report",
                string.Empty,
                $"Report: {result.ReportPath}",
                $"Crops: {result.CropCount}",
                $"Suspect crops: {result.SuspectCount}",
                string.Empty,
                "Scanned folders:",
                .. result.ScannedFolders
            ]);

            if (!CountCropReviewReport.TryOpenInDefaultBrowser(result.ReportPath, out var error))
            {
                MessageBox.Show(
                    this,
                    $"Report generated, but the browser could not be opened.\r\n\r\n{result.ReportPath}\r\n\r\n{error}",
                    "Count Crop Review",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Count crop review failed.";
            _detailsBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "Count Crop Review Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task ReadCountsWithAiAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            _statusLabel.Text = "Choose a stash mode before AI count reading.";
            return;
        }

        var context = GetCurrentAiCountContext(mode);
        if (context is null)
        {
            _statusLabel.Text = "Scan or load a stash tab before using AI count reading.";
            return;
        }

        SetBusy(true, "Building AI count contact sheet...");
        try
        {
            var result = await _aiCountReader.ReadCountsAsync(
                context.ProfileKey,
                context.ProfileLabel,
                context.StashCropPath,
                context.Slots,
                CancellationToken.None).ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(result.ParseError))
            {
                _statusLabel.Text = "AI count reader returned invalid JSON.";
                _detailsBox.Text = string.Join(Environment.NewLine, [
                    "AI count reader invalid JSON",
                    string.Empty,
                    $"Model: {result.Model}",
                    $"Raw response: {result.RawResponsePath}",
                    $"Output JSON: {result.OutputJsonPath}",
                    $"Parse error: {result.ParseError}",
                    string.Empty,
                    result.OutputJson
                ]);
                return;
            }

            var apply = await ApplyAiCountsToCurrentScanAsync(mode, result).ConfigureAwait(true);
            if (apply.AppliedCount == 0)
            {
                _stashPictureBox.Invalidate();
            }

            var partial = apply.UnknownTileIds > 0 ||
                apply.MissingTileIds > 0 ||
                apply.InvalidOkCounts > 0 ||
                apply.ExcludedOccupiedSlots > 0 ||
                apply.RecalculationErrorPath is not null;
            var repriceStatus = apply.ValuesRecalculated
                ? " Values recalculated."
                : apply.RecalculationErrorPath is not null
                    ? " Value recalculation failed."
                    : string.Empty;
            var totalStatus = apply.ValuesRecalculated && apply.RecalculatedTotalExalts is not null && apply.RecalculatedTotalDivines is not null
                ? $" Total: {apply.RecalculatedTotalExalts.Value:0.##} ex / {apply.RecalculatedTotalDivines.Value:0.####} div."
                : string.Empty;
            _statusLabel.Text = partial
                ? $"AI counts partially applied: {apply.AppliedCount} ok, {apply.SkippedManualOverrides} locked skipped, {apply.NoCountVisibleCount} no-count, {apply.UnclearCount} unclear.{repriceStatus}{totalStatus}"
                : $"AI counts applied: {apply.AppliedCount} ok, {apply.SkippedManualOverrides} locked skipped, {apply.NoCountVisibleCount} no-count, {apply.UnclearCount} unclear.{repriceStatus}{totalStatus}";

            _detailsBox.Text = BuildAiCountSummary(result, apply);
        }
        catch (MissingOpenAiApiKeyException ex)
        {
            _statusLabel.Text = "No OpenAI API key configured.";
            _detailsBox.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Missing OpenAI API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OpenAiCountReaderException ex)
        {
            _statusLabel.Text = "AI count reader API request failed.";
            _detailsBox.Text = $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Debug response: {ex.DebugPath}";
            MessageBox.Show(this, $"OpenAI request failed. Raw response saved to:\r\n{ex.DebugPath}", "AI Count Reader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "AI count reader failed.";
            _detailsBox.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "AI Count Reader Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private AiCountCurrentScanContext? GetCurrentAiCountContext(ScanModeOption mode)
    {
        if (_lastCurrencyResult is not null)
        {
            return new AiCountCurrentScanContext(
                FixedStashScannerProfiles.Currency.Key,
                FixedStashScannerProfiles.Currency.Label,
                _lastCurrencyResult.StashCropPath,
                _lastCurrencyResult.Slots
                    .Select((slot, index) => new AiCountSlotSource(
                        index,
                        slot.SlotIndex,
                        slot.CropBounds,
                        slot.Occupied,
                        GetAiCountExistingQuantity(slot.Quantity, _currencyMappingStore.GetCountOverride(slot.SlotIndex)),
                        slot.IsCountOverridden || _currencyMappingStore.IsCountOverridden(slot.SlotIndex),
                        GetAiCountMethod(slot.CountMethod, _currencyMappingStore.IsCountOverridden(slot.SlotIndex)),
                        slot.ItemName))
                    .ToArray());
        }

        if (_lastRuneResult is not null)
        {
            return new AiCountCurrentScanContext(
                FixedStashScannerProfiles.AugmentRunes.Key,
                FixedStashScannerProfiles.AugmentRunes.Label,
                _lastRuneResult.StashCropPath,
                _lastRuneResult.Slots
                    .Select((slot, index) => new AiCountSlotSource(
                        index,
                        slot.SlotIndex,
                        slot.CropBounds,
                        slot.Occupied,
                        GetAiCountExistingQuantity(slot.Quantity, _runeMappingStore.GetCountOverride(slot.SlotIndex)),
                        slot.IsCountOverridden || _runeMappingStore.IsCountOverridden(slot.SlotIndex),
                        GetAiCountMethod(slot.CountMethod, _runeMappingStore.IsCountOverridden(slot.SlotIndex)),
                        slot.ItemName))
                    .ToArray());
        }

        if (_lastKalguuranRuneResult is not null)
        {
            return new AiCountCurrentScanContext(
                FixedStashScannerProfiles.KalguuranRunes.Key,
                FixedStashScannerProfiles.KalguuranRunes.Label,
                _lastKalguuranRuneResult.StashCropPath,
                _lastKalguuranRuneResult.Slots
                    .Select((slot, index) => new AiCountSlotSource(
                        index,
                        slot.SlotIndex,
                        slot.CropBounds,
                        slot.Occupied,
                        GetAiCountExistingQuantity(slot.Quantity, _kalguuranRuneMappingStore.GetCountOverride(slot.SlotIndex)),
                        slot.IsCountOverridden || _kalguuranRuneMappingStore.IsCountOverridden(slot.SlotIndex),
                        GetAiCountMethod(slot.CountMethod, _kalguuranRuneMappingStore.IsCountOverridden(slot.SlotIndex)),
                        slot.ItemName))
                    .ToArray());
        }

        if (_lastGenericResult is not null)
        {
            return new AiCountCurrentScanContext(
                _lastGenericResult.Profile.Key,
                _lastGenericResult.Profile.Label,
                _lastGenericResult.StashCropPath,
                _lastGenericResult.Slots
                    .Select((slot, index) => new AiCountSlotSource(
                        index,
                        slot.SlotIndex,
                        slot.CropBounds,
                        slot.Occupied,
                        GetAiCountExistingQuantity(slot.Quantity, _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(slot.SlotIndex)),
                        slot.IsCountOverridden || _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(slot.SlotIndex) is not null,
                        GetAiCountMethod(slot.CountMethod, _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(slot.SlotIndex) is not null),
                        slot.ItemName))
                    .ToArray());
        }

        return null;
    }

    private static int? GetAiCountExistingQuantity(int? scanQuantity, int? countOverride)
    {
        return countOverride ?? scanQuantity;
    }

    private static string GetAiCountMethod(string scanCountMethod, bool countOverridden)
    {
        return countOverridden ? "manual-count-override" : scanCountMethod;
    }

    private async Task<AiCountApplySummary> ApplyAiCountsToCurrentScanAsync(ScanModeOption mode, AiCountReadResult result)
    {
        var tileMap = result.TileMap.Tiles.ToDictionary(tile => tile.TileId, StringComparer.OrdinalIgnoreCase);
        var returnedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var applied = 0;
        var noCountVisible = 0;
        var unclear = 0;
        var unknown = 0;
        var invalid = 0;
        var skippedManual = 0;
        var valuesRecalculated = false;
        string? recalculationErrorPath = null;
        decimal? recalculatedTotalExalts = null;
        decimal? recalculatedTotalDivines = null;

        if (_lastCurrencyResult is not null)
        {
            var slots = _lastCurrencyResult.Slots.ToArray();
            ApplyToSlots(
                result.Results,
                tileMap,
                returnedIds,
                slots.Length,
                entry => slots[entry.ResultIndex].IsCountOverridden || _currencyMappingStore.IsCountOverridden(entry.SlotIndex),
                (entry, count) =>
            {
                var slot = slots[entry.ResultIndex];
                slots[entry.ResultIndex] = slot with
                {
                    Quantity = count,
                    Exalts = null,
                    Divines = null,
                    CountConfidence = 0.99,
                    CountMethod = "ai-count"
                };
            },
                ref applied,
                ref noCountVisible,
                ref unclear,
                ref unknown,
                ref invalid,
                ref skippedManual);

            _lastCurrencyResult = _lastCurrencyResult with { Slots = slots };
            var shouldRecalculateValues = applied > 0 || skippedManual > 0;
            if (shouldRecalculateValues)
            {
                try
                {
                    _lastCurrencyResult = await _currencyScanner.RecalculateValuesAsync(_lastCurrencyResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastCurrencyResult);
                    ShowCurrencyResult(_lastCurrencyResult);
                    valuesRecalculated = true;
                    recalculatedTotalExalts = _lastCurrencyResult.TotalExalts;
                    recalculatedTotalDivines = _lastCurrencyResult.TotalDivines;
                }
                catch (Exception ex)
                {
                    _savedCurrencyResults[mode.Key] = _lastCurrencyResult;
                    UpdateAllScannedTabsTotal();
                    recalculationErrorPath = SaveAiCountRecalculationError(result, ex);
                }
            }
            else
            {
                _savedCurrencyResults[mode.Key] = _lastCurrencyResult;
            }
        }
        else if (_lastRuneResult is not null)
        {
            var slots = _lastRuneResult.Slots.ToArray();
            ApplyToSlots(
                result.Results,
                tileMap,
                returnedIds,
                slots.Length,
                entry => slots[entry.ResultIndex].IsCountOverridden || _runeMappingStore.IsCountOverridden(entry.SlotIndex),
                (entry, count) =>
            {
                var slot = slots[entry.ResultIndex];
                slots[entry.ResultIndex] = slot with
                {
                    Quantity = count,
                    Exalts = null,
                    Divines = null,
                    CountConfidence = 0.99,
                    CountMethod = "ai-count"
                };
            },
                ref applied,
                ref noCountVisible,
                ref unclear,
                ref unknown,
                ref invalid,
                ref skippedManual);

            _lastRuneResult = _lastRuneResult with { Slots = slots };
            var shouldRecalculateValues = applied > 0 || skippedManual > 0;
            if (shouldRecalculateValues)
            {
                try
                {
                    _lastRuneResult = await _runeScanner.RecalculateValuesAsync(_lastRuneResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastRuneResult);
                    ShowRuneResult(_lastRuneResult);
                    valuesRecalculated = true;
                    recalculatedTotalExalts = _lastRuneResult.TotalExalts;
                    recalculatedTotalDivines = _lastRuneResult.TotalDivines;
                }
                catch (Exception ex)
                {
                    _savedRuneResults[mode.Key] = _lastRuneResult;
                    UpdateAllScannedTabsTotal();
                    recalculationErrorPath = SaveAiCountRecalculationError(result, ex);
                }
            }
            else
            {
                _savedRuneResults[mode.Key] = _lastRuneResult;
            }
        }
        else if (_lastKalguuranRuneResult is not null)
        {
            var slots = _lastKalguuranRuneResult.Slots.ToArray();
            ApplyToSlots(
                result.Results,
                tileMap,
                returnedIds,
                slots.Length,
                entry => slots[entry.ResultIndex].IsCountOverridden || _kalguuranRuneMappingStore.IsCountOverridden(entry.SlotIndex),
                (entry, count) =>
            {
                var slot = slots[entry.ResultIndex];
                slots[entry.ResultIndex] = slot with
                {
                    Quantity = count,
                    Exalts = null,
                    Divines = null,
                    CountConfidence = 0.99,
                    CountMethod = "ai-count"
                };
            },
                ref applied,
                ref noCountVisible,
                ref unclear,
                ref unknown,
                ref invalid,
                ref skippedManual);

            _lastKalguuranRuneResult = _lastKalguuranRuneResult with { Slots = slots };
            var shouldRecalculateValues = applied > 0 || skippedManual > 0;
            if (shouldRecalculateValues)
            {
                try
                {
                    _lastKalguuranRuneResult = await _kalguuranRuneScanner.RecalculateValuesAsync(_lastKalguuranRuneResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastKalguuranRuneResult);
                    ShowKalguuranRuneResult(_lastKalguuranRuneResult);
                    valuesRecalculated = true;
                    recalculatedTotalExalts = _lastKalguuranRuneResult.TotalExalts;
                    recalculatedTotalDivines = _lastKalguuranRuneResult.TotalDivines;
                }
                catch (Exception ex)
                {
                    _savedRuneResults[mode.Key] = _lastKalguuranRuneResult;
                    UpdateAllScannedTabsTotal();
                    recalculationErrorPath = SaveAiCountRecalculationError(result, ex);
                }
            }
            else
            {
                _savedRuneResults[mode.Key] = _lastKalguuranRuneResult;
            }
        }
        else if (_lastGenericResult is not null)
        {
            var slots = _lastGenericResult.Slots.ToArray();
            ApplyToSlots(
                result.Results,
                tileMap,
                returnedIds,
                slots.Length,
                entry => slots[entry.ResultIndex].IsCountOverridden ||
                    _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(entry.SlotIndex) is not null,
                (entry, count) =>
            {
                var slot = slots[entry.ResultIndex];
                slots[entry.ResultIndex] = slot with
                {
                    Quantity = count,
                    Exalts = null,
                    Divines = null,
                    CountConfidence = 0.99,
                    CountMethod = "ai-count"
                };
            },
                ref applied,
                ref noCountVisible,
                ref unclear,
                ref unknown,
                ref invalid,
                ref skippedManual);

            _lastGenericResult = _lastGenericResult with { Slots = slots };
            var shouldRecalculateValues = applied > 0 || skippedManual > 0;
            if (shouldRecalculateValues)
            {
                try
                {
                    _lastGenericResult = await _genericScanners[_lastGenericResult.Profile.Key]
                        .RecalculateValuesAsync(_lastGenericResult, CancellationToken.None)
                        .ConfigureAwait(true);
                    SaveLatestScan(mode, _lastGenericResult);
                    ShowGenericFixedStashResult(_lastGenericResult);
                    valuesRecalculated = true;
                    recalculatedTotalExalts = _lastGenericResult.TotalExalts;
                    recalculatedTotalDivines = _lastGenericResult.TotalDivines;
                }
                catch (Exception ex)
                {
                    _savedGenericResults[mode.Key] = _lastGenericResult;
                    UpdateAllScannedTabsTotal();
                    recalculationErrorPath = SaveAiCountRecalculationError(result, ex);
                }
            }
            else
            {
                _savedGenericResults[mode.Key] = _lastGenericResult;
            }
        }

        var missing = tileMap.Keys.Count(tileId => !returnedIds.Contains(tileId));
        return new AiCountApplySummary(
            applied,
            noCountVisible,
            unclear,
            unknown,
            missing,
            invalid,
            skippedManual,
            tileMap.Values.Count(tile => tile.Locked || tile.ManualOverride),
            result.TileMap.ExcludedOccupiedSlots.Count,
            valuesRecalculated,
            recalculationErrorPath,
            recalculatedTotalExalts,
            recalculatedTotalDivines);
    }

    private static void ApplyToSlots(
        IReadOnlyList<AiCountTileResult> results,
        IReadOnlyDictionary<string, AiCountTileMapEntry> tileMap,
        HashSet<string> returnedIds,
        int slotCount,
        Func<AiCountTileMapEntry, bool> isManualOverride,
        Action<AiCountTileMapEntry, int> applyCount,
        ref int applied,
        ref int noCountVisible,
        ref int unclear,
        ref int unknown,
        ref int invalid,
        ref int skippedManual)
    {
        foreach (var read in results)
        {
            if (!string.IsNullOrWhiteSpace(read.TileId))
            {
                returnedIds.Add(read.TileId);
            }

            if (!tileMap.TryGetValue(read.TileId, out var entry) ||
                entry.ResultIndex < 0 ||
                entry.ResultIndex >= slotCount)
            {
                unknown++;
                continue;
            }

            if (read.Status.Equals("no_count_visible", StringComparison.OrdinalIgnoreCase))
            {
                noCountVisible++;
                continue;
            }

            if (read.Status.Equals("unclear", StringComparison.OrdinalIgnoreCase))
            {
                unclear++;
                continue;
            }

            if (!read.Status.Equals("ok", StringComparison.OrdinalIgnoreCase) || read.Count is null or <= 0)
            {
                invalid++;
                continue;
            }

            if (entry.Locked || entry.ManualOverride || isManualOverride(entry))
            {
                skippedManual++;
                continue;
            }

            applyCount(entry, read.Count.Value);
            applied++;
        }
    }

    private static string BuildAiCountSummary(AiCountReadResult result, AiCountApplySummary apply)
    {
        var usage = result.Usage is null
            ? "not returned"
            : $"input {result.Usage.InputTokens?.ToString() ?? "?"}, output {result.Usage.OutputTokens?.ToString() ?? "?"}, total {result.Usage.TotalTokens?.ToString() ?? "?"}";
        var estimatedCost = result.Usage?.EstimatedCostUsd is null
            ? "unavailable"
            : $"${result.Usage.EstimatedCostUsd.Value:0.00000}";

        return string.Join(Environment.NewLine, [
            "AI count reader",
            string.Empty,
            $"Model: {result.Model}",
            $"Applied ok counts: {apply.AppliedCount}",
            $"No count visible: {apply.NoCountVisibleCount}",
            $"Unclear: {apply.UnclearCount}",
            $"Locked/manual override tiles on sheet: {apply.LockedManualTileCount}",
            $"Skipped locked/manual override ok counts: {apply.SkippedManualOverrides}",
            $"Unknown tile IDs ignored: {apply.UnknownTileIds}",
            $"Missing tile IDs: {apply.MissingTileIds}",
            $"Invalid ok counts ignored: {apply.InvalidOkCounts}",
            $"Excluded occupied slots: {apply.ExcludedOccupiedSlots}",
            $"Value recalculation: {FormatAiCountRecalculationStatus(apply)}",
            $"Recalculated total: {FormatAiCountRecalculatedTotal(apply)}",
            $"Usage tokens: {usage}",
            $"Estimated cost: {estimatedCost}",
            string.Empty,
            "Debug output:",
            $"Contact sheet: {result.ContactSheetPath}",
            $"Tile map: {result.TileMapPath}",
            $"Raw response: {result.RawResponsePath}",
            $"Output JSON: {result.OutputJsonPath}",
            $"Parsed JSON: {result.ParsedJsonPath ?? "not saved"}",
            string.Empty,
            "Excluded occupied slots:",
            .. (result.TileMap.ExcludedOccupiedSlots.Count == 0
                ? ["none"]
                : result.TileMap.ExcludedOccupiedSlots.Select(slot =>
                    $"slot {slot.SlotIndex} result {slot.ResultIndex}: {slot.Reason} manualOverride={slot.ManualOverride} existingCount={slot.ExistingCount?.ToString() ?? "unknown"}")),
            string.Empty,
            apply.ValuesRecalculated
                ? "Applied AI counts were repriced with the current scanner price cache and saved as the latest scan."
                : apply.AppliedCount == 0
                    ? "No AI counts were applied, so values were left unchanged."
                    : $"Recalculation failed after applying counts. Details saved: {apply.RecalculationErrorPath}"
        ]);
    }

    private static string FormatAiCountRecalculationStatus(AiCountApplySummary apply)
    {
        if (apply.ValuesRecalculated)
        {
            return "completed after applying AI counts";
        }

        if (apply.RecalculationErrorPath is not null)
        {
            return $"failed, debug saved to {apply.RecalculationErrorPath}";
        }

        return apply.AppliedCount == 0
            ? "not run because no counts were applied"
            : "not run";
    }

    private static string FormatAiCountRecalculatedTotal(AiCountApplySummary apply)
    {
        return apply.ValuesRecalculated && apply.RecalculatedTotalExalts is not null && apply.RecalculatedTotalDivines is not null
            ? $"{apply.RecalculatedTotalExalts.Value:0.##} ex / {apply.RecalculatedTotalDivines.Value:0.####} div"
            : "not available";
    }

    private static string SaveAiCountRecalculationError(AiCountReadResult result, Exception exception)
    {
        var debugDirectory = Path.GetDirectoryName(result.RawResponsePath) ??
            Path.Combine(AppPaths.DebugDirectory, "ai-counts");
        Directory.CreateDirectory(debugDirectory);
        var path = Path.Combine(
            debugDirectory,
            $"ai-count-recalculation-error-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.txt");
        File.WriteAllText(
            path,
            string.Join(Environment.NewLine, [
                "AI count value recalculation failed",
                string.Empty,
                $"Timestamp: {DateTimeOffset.UtcNow:O}",
                $"Model: {result.Model}",
                $"Contact sheet: {result.ContactSheetPath}",
                $"Tile map: {result.TileMapPath}",
                $"Raw response: {result.RawResponsePath}",
                $"Parsed JSON: {result.ParsedJsonPath ?? "not saved"}",
                string.Empty,
                exception.ToString()
            ]));
        return path;
    }

    private async Task RecalculateCurrentValuesAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        if (_lastCurrencyResult is null &&
            _lastRuneResult is null &&
            _lastKalguuranRuneResult is null &&
            _lastGenericResult is null)
        {
            _statusLabel.Text = "Scan or load a saved stash tab before recalculating values.";
            return;
        }

        SetBusy(true, "Recalculating current scan values...");
        try
        {
            switch (mode.Kind)
            {
                case ScanModeKind.CurrencyStash when _lastCurrencyResult is not null:
                    _lastCurrencyResult = await _currencyScanner.RecalculateValuesAsync(_lastCurrencyResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastCurrencyResult);
                    ShowCurrencyResult(_lastCurrencyResult);
                    break;
                case ScanModeKind.AugmentRunes when _lastRuneResult is not null:
                    _lastRuneResult = await _runeScanner.RecalculateValuesAsync(_lastRuneResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastRuneResult);
                    ShowRuneResult(_lastRuneResult);
                    break;
                case ScanModeKind.KalguuranRunes when _lastKalguuranRuneResult is not null:
                    _lastKalguuranRuneResult = await _kalguuranRuneScanner.RecalculateValuesAsync(_lastKalguuranRuneResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastKalguuranRuneResult);
                    ShowKalguuranRuneResult(_lastKalguuranRuneResult);
                    break;
                case ScanModeKind.GenericFixedStash when _lastGenericResult is not null && _genericScanners.TryGetValue(mode.Key, out var scanner):
                    _lastGenericResult = await scanner.RecalculateValuesAsync(_lastGenericResult, CancellationToken.None).ConfigureAwait(true);
                    SaveLatestScan(mode, _lastGenericResult);
                    ShowGenericFixedStashResult(_lastGenericResult);
                    break;
                default:
                    _statusLabel.Text = "The displayed scan does not match the selected stash mode.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Value recalculation failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshPricesAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        SetBusy(true, "Refreshing poe.ninja prices...");
        try
        {
            await _scanner.RefreshPricesAsync(CancellationToken.None, forceRefresh: true);
            await _currencyScanner.RefreshPricesAsync(CancellationToken.None, forceRefresh: true);
            await _runeScanner.RefreshPricesAsync(CancellationToken.None, forceRefresh: true);
            await _kalguuranRuneScanner.RefreshPricesAsync(CancellationToken.None, forceRefresh: true);
            foreach (var scanner in _genericScanners.Values)
            {
                await scanner.RefreshPricesAsync(CancellationToken.None, forceRefresh: true);
            }

            _statusLabel.Text = "Prices refreshed.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Price refresh failed.";
            _detailsBox.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshIconCacheAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        SetBusy(true, "Refreshing poe.ninja icon cache...");
        try
        {
            var index = await _iconCache.BuildAsync(forceDownload: false, CancellationToken.None).ConfigureAwait(true);
            _statusLabel.Text = $"Icon cache refreshed: {index.ItemCount} items, {index.FailedDownloadCount} failed downloads.";
            _detailsBox.Text = string.Join(Environment.NewLine, new[]
            {
                "poe.ninja icon cache",
                string.Empty,
                $"League: {index.League}",
                $"Items indexed: {index.ItemCount}",
                $"Downloaded this run: {index.DownloadedCount}",
                $"Failed downloads: {index.FailedDownloadCount}",
                string.Empty,
                "By type:"
            }
            .Concat(index.Items
                .GroupBy(item => item.Type)
                .OrderBy(group => group.Key)
                .Select(group => $"{group.Key}: {group.Count()}")));
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Icon cache refresh failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopyCurrentSummary()
    {
        var summary = BuildCurrentSummary();
        if (string.IsNullOrWhiteSpace(summary))
        {
            _statusLabel.Text = "Nothing to copy yet.";
            return;
        }

        try
        {
            Clipboard.SetText(summary);
            _statusLabel.Text = "Summary copied to clipboard.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Copy failed.";
            _detailsBox.Text = ex.ToString();
        }
    }

    private string BuildCurrentSummary()
    {
        var lines = new List<string>
        {
            "POE2 Price Checker",
            $"Status: {_statusLabel.Text}",
            $"Copied: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            string.Empty
        };

        if (_lastCurrencyResult is not null)
        {
            lines.AddRange([
                "Currency Stash",
                $"Total: {_lastCurrencyResult.TotalExalts:0.##} ex / {_lastCurrencyResult.TotalDivines:0.####} div",
                $"Known occupied: {_lastCurrencyResult.KnownOccupiedSlots}",
                $"Unknown occupied: {_lastCurrencyResult.UnknownOccupiedSlots}",
                string.Empty,
                "Top stacks:"
            ]);
            lines.AddRange(_lastCurrencyResult.TopStacks.Select(stack => stack.DisplayText));
        }
        else if (_lastRuneResult is not null)
        {
            lines.AddRange([
                "Augment Runes",
                $"Total: {_lastRuneResult.TotalExalts:0.##} ex / {_lastRuneResult.TotalDivines:0.####} div",
                $"Known occupied: {_lastRuneResult.KnownOccupiedSlots}",
                $"Unknown occupied: {_lastRuneResult.UnknownOccupiedSlots}",
                $"Profitable upgrades: {_lastRuneResult.UpgradeSuggestions.Count}",
                string.Empty,
                "Upgrade suggestions:"
            ]);
            lines.AddRange(_lastRuneResult.UpgradeSuggestions
                .Take(10)
                .Select(suggestion => $"UPGRADE {suggestion.UpgradeCount}x {suggestion.FromItemName} -> {suggestion.ToItemName} ({suggestion.ProfitExalts:+0.##;-0.##;0} ex)"));
            lines.AddRange([string.Empty, "Top stacks:"]);
            lines.AddRange(_lastRuneResult.TopStacks.Select(stack => stack.DisplayText));
        }
        else if (_lastKalguuranRuneResult is not null)
        {
            lines.AddRange([
                "Augment Kalguuran Runes",
                $"Total: {_lastKalguuranRuneResult.TotalExalts:0.##} ex / {_lastKalguuranRuneResult.TotalDivines:0.####} div",
                $"Known occupied: {_lastKalguuranRuneResult.KnownOccupiedSlots}",
                $"Unknown occupied: {_lastKalguuranRuneResult.UnknownOccupiedSlots}",
                string.Empty,
                "Top stacks:"
            ]);
            lines.AddRange(_lastKalguuranRuneResult.TopStacks.Select(stack => stack.DisplayText));
        }
        else if (_lastGenericResult is not null)
        {
            lines.AddRange([
                _lastGenericResult.Profile.Label,
                $"Total: {_lastGenericResult.TotalExalts:0.##} ex / {_lastGenericResult.TotalDivines:0.####} div",
                $"Known occupied: {_lastGenericResult.KnownOccupiedSlots}",
                $"Unknown occupied: {_lastGenericResult.UnknownOccupiedSlots}",
                string.Empty,
                "Top stacks:"
            ]);
            lines.AddRange(_lastGenericResult.TopStacks.Select(stack => stack.DisplayText));
        }
        else if (!string.IsNullOrWhiteSpace(_detailsBox.Text))
        {
            lines.Add(_detailsBox.Text);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private async Task OpenScreenshotAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            _statusLabel.Text = "Choose a stash mode before opening a screenshot.";
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = $"Open {mode.Label} screenshot",
            Filter = "Screenshot images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false
        };

        var initialDirectory = GetInitialScreenshotDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var screenshotPath = dialog.FileName;
        var directory = Path.GetDirectoryName(screenshotPath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            _lastScreenshotDirectory = directory;
        }

        await ScanSelectedStashModeFileAsync(mode, screenshotPath);
    }

    private string? GetInitialScreenshotDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_lastScreenshotDirectory) && Directory.Exists(_lastScreenshotDirectory))
        {
            return _lastScreenshotDirectory;
        }

        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrWhiteSpace(pictures) && Directory.Exists(pictures))
        {
            return pictures;
        }

        return Directory.Exists(Environment.CurrentDirectory)
            ? Environment.CurrentDirectory
            : null;
    }

    private async Task ScanSelectedStashModeAsync()
    {
        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        switch (mode.Kind)
        {
            case ScanModeKind.CurrencyStash:
                await ScanCurrencyAsync();
                break;
            case ScanModeKind.AugmentRunes:
                await ScanRunesAsync();
                break;
            case ScanModeKind.KalguuranRunes:
                await ScanKalguuranRunesAsync();
                break;
            case ScanModeKind.GenericFixedStash:
                await ScanGenericFixedStashAsync(mode);
                break;
            default:
                _statusLabel.Text = $"{mode.Label} is not implemented yet. Use Capture Stash when you want to build it next.";
                break;
        }
    }

    private async Task ScanSelectedStashModeFileAsync(ScanModeOption mode, string screenshotPath)
    {
        if (_scanInProgress)
        {
            return;
        }

        SetBusy(true, $"Loading {Path.GetFileName(screenshotPath)}...");
        try
        {
            switch (mode.Kind)
            {
                case ScanModeKind.CurrencyStash:
                {
                    var result = await _currencyScanner.ScanFileAsync(screenshotPath, CancellationToken.None, GetSelectedStashLayout());
                    SaveLatestScan(mode, result);
                    ShowCurrencyResult(result);
                    _statusLabel.Text = $"Loaded {Path.GetFileName(screenshotPath)} ({FormatResolutionProfile(result.ScreenBounds.Size)}). Currency total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div.";
                    break;
                }
                case ScanModeKind.AugmentRunes:
                {
                    var result = await _runeScanner.ScanFileAsync(screenshotPath, CancellationToken.None, GetSelectedStashLayout());
                    SaveLatestScan(mode, result);
                    ShowRuneResult(result);
                    var profitable = result.UpgradeSuggestions.Count(suggestion => suggestion.IsProfitable);
                    _statusLabel.Text = $"Loaded {Path.GetFileName(screenshotPath)} ({FormatResolutionProfile(result.ScreenBounds.Size)}). Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div. Profitable upgrades: {profitable}.";
                    break;
                }
                case ScanModeKind.KalguuranRunes:
                {
                    var result = await _kalguuranRuneScanner.ScanFileAsync(screenshotPath, CancellationToken.None, GetSelectedStashLayout());
                    SaveLatestScan(mode, result);
                    ShowKalguuranRuneResult(result);
                    _statusLabel.Text = $"Loaded {Path.GetFileName(screenshotPath)} ({FormatResolutionProfile(result.ScreenBounds.Size)}). Kalguuran Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div.";
                    break;
                }
                case ScanModeKind.GenericFixedStash when mode.Profile is not null && _genericScanners.TryGetValue(mode.Key, out var scanner):
                {
                    var result = await scanner.ScanFileAsync(screenshotPath, CancellationToken.None, GetSelectedStashLayout());
                    SaveLatestScan(mode, result);
                    ShowGenericFixedStashResult(result);
                    _statusLabel.Text = $"Loaded {Path.GetFileName(screenshotPath)} ({FormatResolutionProfile(result.ScreenBounds.Size)}). {result.Profile.Label} total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div.";
                    break;
                }
                default:
                    _statusLabel.Text = $"{mode.Label} is not implemented for screenshot loading.";
                    break;
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Open screenshot failed: {Path.GetFileName(screenshotPath)}.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LoadSelectedModeFolderSetting()
    {
        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            _insideFolderCheckBox.Checked = false;
            return;
        }

        _insideFolderCheckBox.Checked = _layoutSettingsStore.GetInsideFolder(mode.Key, mode.Profile?.DefaultInsideFolder ?? false);
    }

    private void ShowSavedScanForSelectedMode()
    {
        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        if (mode.Kind == ScanModeKind.CurrencyStash && _savedCurrencyResults.TryGetValue(mode.Key, out var currencyResult))
        {
            ShowCurrencyResult(currencyResult, savedView: true);
            return;
        }

        if ((mode.Kind == ScanModeKind.AugmentRunes || mode.Kind == ScanModeKind.KalguuranRunes) &&
            _savedRuneResults.TryGetValue(mode.Key, out var runeResult))
        {
            if (mode.Kind == ScanModeKind.AugmentRunes)
            {
                ShowRuneResult(runeResult, savedView: true);
            }
            else
            {
                ShowKalguuranRuneResult(runeResult, savedView: true);
            }

            return;
        }

        if (mode.Kind == ScanModeKind.GenericFixedStash &&
            _savedGenericResults.TryGetValue(mode.Key, out var genericResult))
        {
            ShowGenericFixedStashResult(genericResult, savedView: true);
            return;
        }

        ClearDisplayedStashScan();
        _statusLabel.Text = $"No saved {mode.Label} scan yet. Open that tab in game and press F7.";
        _detailsBox.Text = string.Join(Environment.NewLine, [
            $"{mode.Label}",
            string.Empty,
            "No saved scan for this mode yet.",
            "Scan this tab once and the app will remember its latest result, even after restart.",
            "Switching modes will then reload the latest saved scan instantly."
        ]);
    }

    private void LoadPersistedLatestScans()
    {
        var snapshot = _latestScanStore.Load(FixedStashScannerProfiles.BuiltIn);
        foreach (var pair in snapshot.Currency)
        {
            _savedCurrencyResults[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.Runes)
        {
            _savedRuneResults[pair.Key] = pair.Value;
        }

        foreach (var pair in snapshot.Generic)
        {
            _savedGenericResults[pair.Key] = pair.Value;
        }

        UpdateAllScannedTabsTotal();
        ShowSavedScanForSelectedMode();
    }

    private void ClearDisplayedStashScan()
    {
        _lastCurrencyResult = null;
        _lastRuneResult = null;
        _lastKalguuranRuneResult = null;
        _lastGenericResult = null;
        _selectedLayoutSlotIndex = null;
        _stashImage?.Dispose();
        _stashImage = null;
        _stashPictureBox.Image = null;
        _stashPictureBox.Invalidate();
        UpdateLayoutEditorControls();
    }

    private void ClearCurrentScanView()
    {
        ClearDisplayedStashScan();
        _statusLabel.Text = "Current scan view cleared. Saved latest scans were left unchanged.";
        _detailsBox.Text = "Current scan view cleared.";
    }

    private void SaveLatestScan(ScanModeOption mode, CurrencyScanResult result)
    {
        _savedCurrencyResults[mode.Key] = result;
        PersistLatestScans();
        UpdateAllScannedTabsTotal();
    }

    private void SaveLatestScan(ScanModeOption mode, RuneScanResult result)
    {
        _savedRuneResults[mode.Key] = result;
        PersistLatestScans();
        UpdateAllScannedTabsTotal();
    }

    private void SaveLatestScan(ScanModeOption mode, FixedStashScanResult result)
    {
        _savedGenericResults[mode.Key] = result;
        PersistLatestScans();
        UpdateAllScannedTabsTotal();
    }

    private void PersistLatestScans()
    {
        try
        {
            _latestScanStore.Save(_savedCurrencyResults, _savedRuneResults, _savedGenericResults);
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(AppPaths.DebugDirectory);
            File.WriteAllText(
                AppPaths.DebugFile("latest-stash-scans-save-error.txt"),
                ex.ToString());
        }
    }

    private void UpdateAllScannedTabsTotal()
    {
        var totalExalts =
            _savedCurrencyResults.Values.Sum(result => result.TotalExalts) +
            _savedRuneResults.Values.Sum(result => result.TotalExalts) +
            _savedGenericResults.Values.Sum(result => result.TotalExalts);
        var totalDivines =
            _savedCurrencyResults.Values.Sum(result => result.TotalDivines) +
            _savedRuneResults.Values.Sum(result => result.TotalDivines) +
            _savedGenericResults.Values.Sum(result => result.TotalDivines);
        var scannedTabs =
            _savedCurrencyResults.Count +
            _savedRuneResults.Count +
            _savedGenericResults.Count;

        _totalStashValueLabel.Text = scannedTabs == 0
            ? "All scanned: 0 tabs | 0ex / 0div"
            : $"All scanned: {scannedTabs} tabs | {totalExalts:0.#}ex / {totalDivines:0.##}div";
    }

    private void SaveSelectedModeFolderSetting()
    {
        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        _layoutSettingsStore.SetInsideFolder(mode.Key, _insideFolderCheckBox.Checked);
    }

    private StashLayoutProfile GetSelectedStashLayout()
    {
        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return StashLayoutProfile.Normal;
        }

        var insideFolder = _layoutSettingsStore.GetInsideFolder(mode.Key, mode.Profile?.DefaultInsideFolder ?? false);
        return mode.Kind switch
        {
            ScanModeKind.AugmentRunes => insideFolder
                ? StashLayoutProfile.Folder
                : StashLayoutProfile.NormalFromFolderMap,
            ScanModeKind.KalguuranRunes => insideFolder
                ? StashLayoutProfile.FolderFull
                : StashLayoutProfile.NormalFromFolderMap,
            ScanModeKind.CurrencyStash => insideFolder
                ? StashLayoutProfile.FolderFromNormalMap
                : StashLayoutProfile.Normal,
            ScanModeKind.GenericFixedStash => insideFolder
                ? StashLayoutProfile.FolderFull
                : StashLayoutProfile.NormalFromFolderMap,
            _ => insideFolder
                ? StashLayoutProfile.Folder
                : StashLayoutProfile.Normal
        };
    }

    private async Task ScanLiveAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        SetBusy(true, "Scanning active screen...");
        try
        {
            var result = await _scanner.ScanScreenAsync(CancellationToken.None);
            ShowResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanCurrencyAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        SetBusy(true, "Scanning currency stash...");
        try
        {
            var result = await _currencyScanner.ScanScreenAsync(CancellationToken.None, GetSelectedStashLayout());
            SaveLatestScan(mode, result);
            ShowCurrencyResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Currency scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanRunesAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        SetBusy(true, "Scanning Augment Runes tab...");
        try
        {
            var result = await _runeScanner.ScanScreenAsync(CancellationToken.None, GetSelectedStashLayout());
            SaveLatestScan(mode, result);
            ShowRuneResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Augment Runes scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanKalguuranRunesAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            return;
        }

        SetBusy(true, "Scanning Augment Kalguuran Runes tab...");
        try
        {
            var result = await _kalguuranRuneScanner.ScanScreenAsync(CancellationToken.None, GetSelectedStashLayout());
            SaveLatestScan(mode, result);
            ShowKalguuranRuneResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Augment Kalguuran Runes scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ScanGenericFixedStashAsync(ScanModeOption mode)
    {
        if (_scanInProgress)
        {
            return;
        }

        if (mode.Profile is null || !_genericScanners.TryGetValue(mode.Key, out var scanner))
        {
            _statusLabel.Text = $"{mode.Label} is missing scanner profile data.";
            return;
        }

        SetBusy(true, $"Scanning {mode.Label}...");
        try
        {
            var result = await scanner.ScanScreenAsync(CancellationToken.None, GetSelectedStashLayout());
            SaveLatestScan(mode, result);
            ShowGenericFixedStashResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"{mode.Label} scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }


    private async Task ScanTestScreenshotAsync()
    {
        const string testPath = @"C:\Users\maran\OneDrive\Desktop\runeshaping\Screenshot 2026-06-09 092011.png";
        if (!File.Exists(testPath))
        {
            _statusLabel.Text = "Test screenshot not found.";
            return;
        }

        SetBusy(true, "Scanning saved test screenshot...");
        try
        {
            var result = await _scanner.ScanFileAsync(testPath, CancellationToken.None);
            ShowResult(result);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Test scan failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CaptureStashTabReference()
    {
        if (_scanInProgress)
        {
            return;
        }

        SetBusy(true, "Capturing stash tab reference...");
        try
        {
            var screen = ScreenCaptureService.SelectPoeScreen();
            using var screenshot = ScreenCaptureService.CaptureScreen(screen.Bounds);
            var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
            var cropRegion = ClampRectangle(mapper.ScaleLayoutFromBase(GetSelectedStashLayout()).DisplayCropRegion, screenshot.Size);
            using var stashCrop = screenshot.Clone(cropRegion, screenshot.PixelFormat);

            var captureDirectory = Path.Combine(AppPaths.DebugDirectory, "stash-tab-captures");
            Directory.CreateDirectory(captureDirectory);

            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            var fullPath = Path.Combine(captureDirectory, $"stash-tab-fullscreen-{stamp}.png");
            var cropPath = Path.Combine(captureDirectory, $"stash-tab-crop-{stamp}.png");
            var latestFullPath = Path.Combine(captureDirectory, "latest-stash-tab-fullscreen.png");
            var latestCropPath = Path.Combine(captureDirectory, "latest-stash-tab-crop.png");

            SaveBitmap(screenshot, fullPath);
            SaveBitmap(stashCrop, cropPath);
            SaveBitmap(screenshot, latestFullPath);
            SaveBitmap(stashCrop, latestCropPath);

            _lastCurrencyResult = null;
            _lastRuneResult = null;
            _lastKalguuranRuneResult = null;
            _lastGenericResult = null;
            _selectedLayoutSlotIndex = null;
            _stashImage?.Dispose();
            _stashImage = LoadImageWithoutFileLock(cropPath);
            _stashPictureBox.Image = _stashImage;
            _stashPictureBox.Invalidate();
            UpdateLayoutEditorControls();

            _statusLabel.Text = "Stash tab reference captured.";
            _detailsBox.Text = string.Join(Environment.NewLine, [
                "Stash tab capture",
                string.Empty,
                "Use this when you want me to build the next fixed-layout tab.",
                "Open the tab in PoE, click Capture Stash Tab, then tell me which tab it is.",
                string.Empty,
                $"Screen: {screen.Bounds.Width}x{screen.Bounds.Height} at {screen.Bounds.Left},{screen.Bounds.Top}",
                $"Resolution profile: {mapper.Profile.Label}",
                $"Crop region: {cropRegion.X},{cropRegion.Y},{cropRegion.Width},{cropRegion.Height}",
                string.Empty,
                "Timestamped files:",
                fullPath,
                cropPath,
                string.Empty,
                "Latest convenience files:",
                latestFullPath,
                latestCropPath
            ]);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Stash tab capture failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AnalyzeSelectedStashWithAiAsync()
    {
        if (_scanInProgress)
        {
            return;
        }

        if (_modeComboBox.SelectedItem is not ScanModeOption mode)
        {
            _statusLabel.Text = "Choose a stash mode before AI Layout.";
            return;
        }

        SetBusy(true, "Capturing stash crop for AI layout helper...");
        try
        {
            var layout = GetSelectedStashLayout();
            var screen = ScreenCaptureService.SelectPoeScreen();
            using var screenshot = ScreenCaptureService.CaptureScreen(screen.Bounds);
            var mapper = StashCoordinateMapper.FromScreenshotSize(screenshot.Size);
            var actualLayout = mapper.ScaleLayoutFromBase(layout);
            var cropRegion = ClampRectangle(actualLayout.DisplayCropRegion, screenshot.Size);
            using var stashCrop = screenshot.Clone(cropRegion, screenshot.PixelFormat);

            _lastCurrencyResult = null;
            _lastRuneResult = null;
            _lastKalguuranRuneResult = null;
            _lastGenericResult = null;
            _selectedLayoutSlotIndex = null;
            _stashImage?.Dispose();
            _stashImage = new Bitmap(stashCrop);
            _stashPictureBox.Image = _stashImage;
            _stashPictureBox.Invalidate();
            UpdateLayoutEditorControls();

            _statusLabel.Text = "Sending stash crop to OpenAI...";
            var result = await _openAiVisionHelper.AnalyzeStashAsync(
                stashCrop,
                mode.Label,
                actualLayout,
                CancellationToken.None).ConfigureAwait(true);

            ShowAiAnalysisResult(result);
        }
        catch (MissingOpenAiApiKeyException ex)
        {
            _statusLabel.Text = "No OpenAI API key configured.";
            _detailsBox.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Missing OpenAI API Key", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "AI layout helper failed.";
            _detailsBox.Text = ex.ToString();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowAiAnalysisResult(AiStashAnalysisResult result)
    {
        var lines = new List<string>
        {
            "AI stash layout helper",
            string.Empty,
            $"Model: {result.Model}",
            $"Request image: {result.RequestImagePath}",
            $"Raw response: {result.RawResponsePath}",
            $"Output JSON: {result.OutputJsonPath}",
            string.Empty
        };

        if (result.Analysis is null)
        {
            _statusLabel.Text = "AI layout helper returned, but JSON parsing failed.";
            lines.Add("The response was saved, but the app could not parse the JSON.");
            if (!string.IsNullOrWhiteSpace(result.ParseError))
            {
                lines.Add(result.ParseError);
            }

            lines.Add(string.Empty);
            lines.Add(result.OutputJson);
            _detailsBox.Text = string.Join(Environment.NewLine, lines);
            return;
        }

        var occupiedSlots = result.Analysis.Slots.Count(slot => slot.Occupied);
        var namedSlots = result.Analysis.Slots.Count(slot => !string.IsNullOrWhiteSpace(slot.ItemNameGuess));
        _statusLabel.Text = $"AI guess: {result.Analysis.StashTypeGuess} ({occupiedSlots} occupied slots, {namedSlots} named).";

        lines.Add($"Stash guess: {result.Analysis.StashTypeGuess}");
        lines.Add($"Layout confidence: {result.Analysis.LayoutConfidence:0.00}");
        lines.Add($"Slots: {result.Analysis.Slots.Count} total, {occupiedSlots} occupied, {namedSlots} named");
        lines.Add("Generic or low-confidence item names are hidden; raw model output is still saved.");
        lines.Add(string.Empty);

        if (result.Analysis.Notes.Count > 0)
        {
            lines.Add("Notes:");
            lines.AddRange(result.Analysis.Notes.Take(8).Select(note => $"- {note}"));
            lines.Add(string.Empty);
        }

        var topSlots = result.Analysis.Slots
            .Where(slot => slot.Occupied)
            .OrderByDescending(slot => slot.Confidence)
            .Take(12)
            .ToArray();
        if (topSlots.Length > 0)
        {
            lines.Add("Top occupied slot guesses:");
            foreach (var slot in topSlots)
            {
                var name = string.IsNullOrWhiteSpace(slot.ItemNameGuess)
                    ? "(unknown item)"
                    : slot.ItemNameGuess;
                var count = slot.CountGuess is null
                    ? string.Empty
                    : $" x{slot.CountGuess}";
                lines.Add($"{slot.X},{slot.Y},{slot.Width},{slot.Height}  {name}{count}  conf {slot.Confidence:0.00}");
            }
        }

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ShowResult(ScanResult result)
    {
        _lastCurrencyResult = null;
        _lastRuneResult = null;
        _lastKalguuranRuneResult = null;
        _lastGenericResult = null;
        _overlay.ShowResult(result);

        if (result.Choices.Count == 0)
        {
            _statusLabel.Text = result.Notes.Count > 0
                ? "No priced runeshaping rewards found. " + result.Notes[0]
                : "No priced runeshaping rewards found.";
        }
        else
        {
            var best = result.Choices.First();
            var note = result.Notes.Count > 0 ? $" {result.Notes[0]}" : string.Empty;
            _statusLabel.Text = $"Best: {best.Quantity}x {best.ItemName} ({best.Exalts:0.##} ex / {best.Divines:0.####} div).{note}";
        }

        var lines = result.Choices.Select(choice => $"{choice.Color,-6} {choice.DisplayText}")
            .Concat(result.UnpricedRewards.Select(reward => $"N/A    {reward}"))
            .Concat(result.Notes.Count == 0
                ? []
                : new[] { string.Empty, "Notes:" }.Concat(result.Notes.Select(note => $"- {note}")))
            .ToArray();
        _detailsBox.Text = lines.Length == 0
            ? $"OCR text:\r\n{result.RawOcrText.Trim()}"
            : string.Join(Environment.NewLine, lines);
    }

    private void ShowCurrencyResult(CurrencyScanResult result, bool savedView = false)
    {
        _lastRuneResult = null;
        _lastKalguuranRuneResult = null;
        _lastGenericResult = null;
        _lastCurrencyResult = result;
        _selectedLayoutSlotIndex = null;
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();
        UpdateLayoutEditorControls();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Currency total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div ({FormatResolutionProfile(result.ScreenBounds.Size)})";

        var lines = new[]
        {
            "Currency workbench",
            savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
            string.Empty,
            $"Known occupied slots: {result.KnownOccupiedSlots}",
            $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
            string.Empty,
            "Top 5 prices are drawn in the black space beside the stash.",
            string.Empty,
            "Click a boxed slot in the stash image to name or correct it.",
            "Use count override only when OCR keeps reading a stack wrong.",
            "Blank slots are treated as count 0, even if mapped.",
            string.Empty,
            "Blue: mapped/priced slot",
            "Yellow: occupied but unnamed",
            "Gray: empty or visually unsure; skipped for pricing",
            "Blue mapped empty slots can be clicked to edit their saved name",
            "* after a count: manual count override",
            "? after a count: low-confidence local read"
        };

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ShowRuneResult(RuneScanResult result, bool savedView = false)
    {
        _lastCurrencyResult = null;
        _lastKalguuranRuneResult = null;
        _lastGenericResult = null;
        _lastRuneResult = result;
        _selectedLayoutSlotIndex = null;
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();
        UpdateLayoutEditorControls();

        var profitable = result.UpgradeSuggestions.Count(suggestion => suggestion.IsProfitable);
        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div ({FormatResolutionProfile(result.ScreenBounds.Size)}). Profitable upgrades: {profitable}.";

        var lines = new List<string>
            {
                "Augment Runes workbench",
                savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
                string.Empty,
                $"Known occupied slots: {result.KnownOccupiedSlots}",
                $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
                $"Profitable upgrades: {profitable}"
            };

        if (result.UpgradeSuggestions.Count > 0)
        {
            lines.AddRange([
                string.Empty,
                "Upgrade Suggestions"
            ]);
            lines.AddRange(result.UpgradeSuggestions
                .Take(10)
                .Select(suggestion => $"UPGRADE {suggestion.UpgradeCount}x {suggestion.FromItemName} -> {suggestion.ToItemName} ({suggestion.ProfitExalts:+0.##;-0.##;0} ex)"));
        }

        lines.AddRange([
                string.Empty,
                "Click a boxed rune slot to name or correct it.",
                "Use count override only when OCR keeps reading a stack wrong.",
                "Upgrade math: output price - 3x input price.",
                "Eligible upgrades: Lesser -> base, base -> Greater.",
                "Perfect and purple/drop-only runes are priced but not upgraded.",
                string.Empty,
                "Blue: mapped/priced slot",
                "Yellow: occupied but unnamed",
                "Gray: empty or visually unsure; skipped for pricing",
                "Blue mapped empty slots can be clicked to edit their saved name",
                "* after a count: manual count override",
                "? after a count: low-confidence local read"
            ]);

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ShowKalguuranRuneResult(RuneScanResult result, bool savedView = false)
    {
        _lastCurrencyResult = null;
        _lastRuneResult = null;
        _lastGenericResult = null;
        _lastKalguuranRuneResult = result;
        _selectedLayoutSlotIndex = null;
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();
        UpdateLayoutEditorControls();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Kalguuran Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div ({FormatResolutionProfile(result.ScreenBounds.Size)}).";

        var lines = new List<string>
            {
                "Augment Kalguuran Runes workbench",
                savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
                string.Empty,
                $"Known occupied slots: {result.KnownOccupiedSlots}",
                $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
                string.Empty,
                "Click a boxed Kalguuran rune slot to name or correct it.",
                "Icon suggestions are disabled for faster manual correction.",
                "Use count override only when OCR keeps reading a stack wrong.",
                "This tab prices runes only. Essence upgrade logic is intentionally separate.",
                string.Empty,
                "Blue: mapped/priced slot",
                "Yellow: occupied but unnamed",
                "Gray: empty or visually unsure; skipped for pricing",
                "Blue mapped empty slots can be clicked to edit their saved name",
                "* after a count: manual count override",
                "? after a count: low-confidence local read"
            };

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void ShowGenericFixedStashResult(FixedStashScanResult result, bool savedView = false)
    {
        _lastCurrencyResult = null;
        _lastRuneResult = null;
        _lastKalguuranRuneResult = null;
        _lastGenericResult = result;
        _selectedLayoutSlotIndex = null;
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();
        UpdateLayoutEditorControls();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}{result.Profile.Label} total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div ({FormatResolutionProfile(result.ScreenBounds.Size)}).";

        var lines = new List<string>
        {
            $"{result.Profile.Label} workbench",
            savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
            string.Empty,
            $"Known occupied slots: {result.KnownOccupiedSlots}",
            $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
            string.Empty,
            "Click a boxed slot to name or correct it.",
            "Icon suggestions are disabled for faster manual correction.",
            "Use count override only when the local reader keeps reading a stack wrong.",
            "Essence upgrade math is intentionally not included yet.",
            string.Empty,
            "Blue: mapped/priced slot",
            "Yellow: occupied but unnamed",
            "Gray: empty or visually unsure; skipped for pricing",
            "Blue mapped empty slots can be clicked to edit their saved name",
            "* after a count: manual count override",
            "? after a count: low-confidence local read",
            string.Empty,
            "Slot price labels:",
            "Top-right yellow: 1x price",
            "Bottom-right yellow: stack value"
        };

        if (savedView)
        {
            lines.Insert(3, "Saved overlay bounds can be stale after layout/profile changes. Rescan this tab before judging current slot alignment.");
        }

        if (result.Profile == FixedStashScannerProfiles.BreachCatalysts)
        {
            lines.Insert(5, "Wombgift slots are intentionally ignored.");
        }
        else if (result.Profile == FixedStashScannerProfiles.Fragments)
        {
            lines.Insert(5, "Tablets and Trials are intentionally ignored.");
        }

        _detailsBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void StashPictureBox_Paint(object? sender, PaintEventArgs e)
    {
        if (_stashPictureBox.Image is null)
        {
            return;
        }

        var imageRect = GetImageDisplayRectangle(_stashPictureBox);
        if (imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            return;
        }

        var scaleX = imageRect.Width / (float)_stashPictureBox.Image.Width;
        var scaleY = imageRect.Height / (float)_stashPictureBox.Image.Height;

        if (_lastCurrencyResult is not null)
        {
            DrawCurrencySummary(e.Graphics, imageRect);

            foreach (var slot in _lastCurrencyResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, ResolveVisualOverlayBounds(FixedStashScannerProfiles.Currency.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset), slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence, slot.SlotIndex);
            }

            return;
        }

        if (_lastRuneResult is not null)
        {
            DrawRuneUpgradeSummary(e.Graphics, imageRect);
            DrawRuneTopStacksSummary(e.Graphics, imageRect);

            foreach (var slot in _lastRuneResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, ResolveVisualOverlayBounds(FixedStashScannerProfiles.AugmentRunes.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.AugmentRuneOverlayInset), slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence, slot.SlotIndex);
            }
        }

        if (_lastKalguuranRuneResult is not null)
        {
            DrawRuneTopStacksSummary(e.Graphics, imageRect, _lastKalguuranRuneResult, "Top 5 Kalguuran Runes/Prices");

            foreach (var slot in _lastKalguuranRuneResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, ResolveVisualOverlayBounds(FixedStashScannerProfiles.KalguuranRunes.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset), slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence, slot.SlotIndex);
            }
        }

        if (_lastGenericResult is not null)
        {
            DrawGenericTopStacksSummary(e.Graphics, imageRect, _lastGenericResult);

            foreach (var slot in _lastGenericResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, ResolveVisualOverlayBounds(_lastGenericResult.Profile.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset), slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence, slot.SlotIndex);
            }
        }
    }

    private Rectangle ResolveVisualOverlayBounds(
        string profileKey,
        int slotIndex,
        Rectangle cropBounds,
        Rectangle? defaultOverlayCropBounds,
        int fallbackInset)
    {
        if (_slotLayoutOverrides.TryGet(profileKey, slotIndex, out var overrideBounds))
        {
            return GetCurrentCoordinateMapper().ScaleRectFromBase(overrideBounds);
        }

        return defaultOverlayCropBounds ?? FixedStashSlot.Inset(cropBounds, fallbackInset);
    }

    private void DrawSlotOverlay(
        Graphics graphics,
        Rectangle imageRect,
        float scaleX,
        float scaleY,
        Rectangle cropBounds,
        bool occupied,
        string? itemName,
        int? quantity,
        decimal? exalts,
        decimal? divines,
        bool isCustomMapped,
        bool isCountOverridden,
        double countConfidence,
        int slotIndex)
    {
        var forcedEmpty = !occupied && isCountOverridden && quantity == 0;
        var mappedEmpty = !occupied && (itemName is not null || forcedEmpty);
        var color = !occupied
            ? mappedEmpty
                ? Color.FromArgb(185, 90, 180, 255)
                : Color.FromArgb(210, 255, 80, 80)
            : itemName is null
                ? Color.FromArgb(230, 255, 205, 60)
                : Color.FromArgb(230, 90, 180, 255);

        using var pen = new Pen(color, occupied || mappedEmpty ? 3 : 1);
        var rect = new Rectangle(
            imageRect.Left + (int)Math.Round(cropBounds.X * scaleX),
            imageRect.Top + (int)Math.Round(cropBounds.Y * scaleY),
            (int)Math.Round(cropBounds.Width * scaleX),
            (int)Math.Round(cropBounds.Height * scaleY));
        graphics.DrawRectangle(pen, rect);

        if (_editLayoutCheckBox.Checked)
        {
            DrawReadableSlotLabel(graphics, rect, slotIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), Color.White, 8.5f, SlotLabelAnchor.TopLeft);
            if (_selectedLayoutSlotIndex == slotIndex)
            {
                using var selectedPen = new Pen(Color.White, 4);
                graphics.DrawRectangle(selectedPen, rect);
            }
        }

        if (!occupied)
        {
            if (mappedEmpty)
            {
                DrawReadableSlotLabel(graphics, rect, forcedEmpty ? "empty" : "mapped", Color.FromArgb(210, 90, 180, 255), 8.5f);
            }

            return;
        }

        var lowConfidence = !isCountOverridden && countConfidence < 0.58;
        var label = itemName is null
            ? "?"
            : $"x{FormatCompactQuantity(quantity ?? 1)}{(isCountOverridden ? "*" : lowConfidence ? "?" : string.Empty)}";
        DrawReadableSlotLabel(graphics, rect, label, lowConfidence ? Color.FromArgb(255, 220, 72) : Color.White, 10.5f);

        if (itemName is not null && quantity.GetValueOrDefault(1) > 0 && exalts is not null && divines is not null)
        {
            var count = Math.Max(1, quantity ?? 1);
            DrawReadableSlotLabel(
                graphics,
                rect,
                $"1x {FormatCompactPrice(exalts.Value / count, divines.Value / count)}",
                Color.FromArgb(255, 220, 72),
                8.2f,
                SlotLabelAnchor.TopRight);
            DrawReadableSlotLabel(
                graphics,
                rect,
                FormatCompactPrice(exalts.Value, divines.Value),
                Color.FromArgb(255, 220, 72),
                8.2f,
                SlotLabelAnchor.BottomRight);
        }
    }

    private static void DrawReadableSlotLabel(
        Graphics graphics,
        Rectangle slotRect,
        string label,
        Color textColor,
        float fontSize,
        SlotLabelAnchor anchor = SlotLabelAnchor.BottomLeft)
    {
        using var labelFont = new Font("Segoe UI", fontSize, FontStyle.Bold);
        var textSize = graphics.MeasureString(label, labelFont);
        var labelWidth = Math.Min(slotRect.Width - 6, (int)Math.Ceiling(textSize.Width) + 8);
        var labelHeight = Math.Min(slotRect.Height - 6, (int)Math.Ceiling(textSize.Height) + 4);
        var labelX = anchor is SlotLabelAnchor.TopRight or SlotLabelAnchor.BottomRight
            ? slotRect.Right - labelWidth - 4
            : slotRect.Left + 4;
        var labelY = anchor is SlotLabelAnchor.TopLeft or SlotLabelAnchor.TopRight
            ? slotRect.Top + 4
            : slotRect.Bottom - labelHeight - 4;
        var labelRect = new Rectangle(labelX, labelY, Math.Max(1, labelWidth), Math.Max(1, labelHeight));

        using var backgroundBrush = new SolidBrush(Color.FromArgb(185, 0, 0, 0));
        using var textBrush = new SolidBrush(textColor);
        graphics.FillRectangle(backgroundBrush, labelRect);
        graphics.DrawString(label, labelFont, textBrush, labelRect.Left + 4, labelRect.Top + 1);
    }

    private static string FormatCompactPrice(decimal exalts, decimal divines)
    {
        return divines >= 1m
            ? $"{divines:0.##}div"
            : $"{exalts:0.#}ex";
    }

    private static string FormatCompactQuantity(int quantity)
    {
        return quantity >= 1000
            ? $"{quantity / 1000m:0.#}k"
            : quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void DrawCurrencySummary(Graphics graphics, Rectangle imageRect)
    {
        if (_lastCurrencyResult is null)
        {
            return;
        }

        var rightSpace = _stashPictureBox.ClientSize.Width - imageRect.Right;
        if (rightSpace < 280)
        {
            return;
        }

        var left = imageRect.Right + 38;
        var width = Math.Min(520, _stashPictureBox.ClientSize.Width - left - 24);
        if (width < 240)
        {
            return;
        }

        var top = Math.Max(34, imageRect.Top + 60);
        using var titleFont = new Font("Segoe UI", 18f, FontStyle.Bold);
        using var rowFont = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        using var priceFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.Gainsboro);
        using var greenBrush = new SolidBrush(Color.FromArgb(92, 255, 124));
        using var softGreenBrush = new SolidBrush(Color.FromArgb(145, 230, 155));
        using var dimBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.NoClip
        };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.DrawString("Top 5 Items/Prices", titleFont, titleBrush, new RectangleF(left, top, width, 34), format);

        var y = top + 64;
        foreach (var stack in _lastCurrencyResult.TopStacks.Take(5))
        {
            var itemText = $"{stack.ItemName} (x{stack.Quantity})";
            var priceText = $"{stack.Exalts:0.##} ex / {stack.Divines:0.####} div";
            graphics.DrawString(itemText, rowFont, greenBrush, new RectangleF(left, y, width, 24), format);
            graphics.DrawString(priceText, priceFont, softGreenBrush, new RectangleF(left, y + 24, width, 24), format);
            y += 58;
        }

        if (_lastCurrencyResult.TopStacks.Count == 0)
        {
            graphics.DrawString("No priced stacks found.", rowFont, dimBrush, new RectangleF(left, y, width, 28), format);
        }

        var totalText = $"Total: {_lastCurrencyResult.TotalExalts:0.##} ex / {_lastCurrencyResult.TotalDivines:0.####} div";
        graphics.DrawString(totalText, priceFont, dimBrush, new RectangleF(left, y + 18, width, 28), format);
    }

    private void DrawRuneTopStacksSummary(Graphics graphics, Rectangle imageRect, RuneScanResult? result = null, string title = "Top 5 Runes/Prices")
    {
        result ??= _lastRuneResult;
        if (result is null)
        {
            return;
        }

        var rightSpace = _stashPictureBox.ClientSize.Width - imageRect.Right;
        if (rightSpace < 280)
        {
            return;
        }

        var left = imageRect.Right + 38;
        var width = Math.Min(520, _stashPictureBox.ClientSize.Width - left - 24);
        if (width < 240)
        {
            return;
        }

        var top = Math.Max(34, imageRect.Top + 44);
        using var titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        using var rowFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var priceFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.Gainsboro);
        using var greenBrush = new SolidBrush(Color.FromArgb(92, 255, 124));
        using var softGreenBrush = new SolidBrush(Color.FromArgb(145, 230, 155));
        using var dimBrush = new SolidBrush(Color.FromArgb(155, 155, 155));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.NoClip
        };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.DrawString(title, titleFont, titleBrush, new RectangleF(left, top, width, 34), format);

        var y = top + 64;
        foreach (var stack in result.TopStacks.Take(5))
        {
            var itemText = $"{stack.ItemName} (x{stack.Quantity})";
            var priceText = $"{stack.Exalts:0.##} ex / {stack.Divines:0.####} div";
            graphics.DrawString(itemText, rowFont, greenBrush, new RectangleF(left, y, width, 24), format);
            graphics.DrawString(priceText, priceFont, softGreenBrush, new RectangleF(left, y + 24, width, 24), format);
            y += 58;
        }

        if (result.TopStacks.Count == 0)
        {
            graphics.DrawString("No priced rune stacks found.", rowFont, dimBrush, new RectangleF(left, y, width, 28), format);
        }

        var totalText = $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div";
        graphics.DrawString(totalText, priceFont, dimBrush, new RectangleF(left, y + 14, width, 28), format);
    }

    private void DrawGenericTopStacksSummary(Graphics graphics, Rectangle imageRect, FixedStashScanResult result)
    {
        var rightSpace = _stashPictureBox.ClientSize.Width - imageRect.Right;
        if (rightSpace < 280)
        {
            return;
        }

        var left = imageRect.Right + 38;
        var width = Math.Min(520, _stashPictureBox.ClientSize.Width - left - 24);
        if (width < 240)
        {
            return;
        }

        var top = Math.Max(34, imageRect.Top + 44);
        using var titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        using var rowFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var priceFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.Gainsboro);
        using var greenBrush = new SolidBrush(Color.FromArgb(92, 255, 124));
        using var softGreenBrush = new SolidBrush(Color.FromArgb(145, 230, 155));
        using var dimBrush = new SolidBrush(Color.FromArgb(155, 155, 155));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.NoClip
        };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.DrawString($"Top 5 {result.Profile.Label}/Prices", titleFont, titleBrush, new RectangleF(left, top, width, 34), format);

        var y = top + 64;
        foreach (var stack in result.TopStacks.Take(5))
        {
            var itemText = $"{stack.ItemName} (x{stack.Quantity})";
            var priceText = $"{stack.Exalts:0.##} ex / {stack.Divines:0.####} div";
            graphics.DrawString(itemText, rowFont, greenBrush, new RectangleF(left, y, width, 24), format);
            graphics.DrawString(priceText, priceFont, softGreenBrush, new RectangleF(left, y + 24, width, 24), format);
            y += 58;
        }

        if (result.TopStacks.Count == 0)
        {
            graphics.DrawString("No priced stacks found yet.", rowFont, dimBrush, new RectangleF(left, y, width, 28), format);
        }

        var totalText = $"Total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div";
        graphics.DrawString(totalText, priceFont, dimBrush, new RectangleF(left, y + 14, width, 28), format);
    }

    private void DrawRuneUpgradeSummary(Graphics graphics, Rectangle imageRect)
    {
        if (_lastRuneResult is null)
        {
            return;
        }

        if (_lastRuneResult.UpgradeSuggestions.Count == 0)
        {
            return;
        }

        var leftSpace = imageRect.Left;
        if (leftSpace < 300)
        {
            return;
        }

        var left = 24;
        var width = Math.Min(520, imageRect.Left - left - 28);
        if (width < 240)
        {
            return;
        }

        var top = Math.Max(34, imageRect.Top + 44);
        using var titleFont = new Font("Segoe UI", 17f, FontStyle.Bold);
        using var rowFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var priceFont = new Font("Segoe UI", 10f, FontStyle.Regular);
        using var titleBrush = new SolidBrush(Color.Gainsboro);
        using var greenBrush = new SolidBrush(Color.FromArgb(92, 255, 124));
        using var redBrush = new SolidBrush(Color.FromArgb(255, 95, 95));
        using var format = new StringFormat
        {
            Trimming = StringTrimming.EllipsisWord,
            FormatFlags = StringFormatFlags.NoClip
        };

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.DrawString("Rune Upgrades", titleFont, titleBrush, new RectangleF(left, top, width, 34), format);

        var y = top + 54;
        foreach (var suggestion in _lastRuneResult.UpgradeSuggestions.Take(6))
        {
            var brush = suggestion.IsProfitable ? greenBrush : redBrush;
            var action = suggestion.IsProfitable ? "Upgrade" : "Skip";
            var itemText = $"{action}: {suggestion.UpgradeCount}x {suggestion.FromItemName}";
            var priceText = $"{suggestion.ProfitExalts:+0.##;-0.##;0} ex -> {suggestion.ToItemName}";
            graphics.DrawString(itemText, rowFont, brush, new RectangleF(left, y, width, 22), format);
            graphics.DrawString(priceText, priceFont, brush, new RectangleF(left, y + 22, width, 22), format);
            y += 52;
        }

    }

    private async void StashPictureBox_MouseClick(object? sender, MouseEventArgs e)
    {
        if (_stashPictureBox.Image is null)
        {
            return;
        }

        _stashPictureBox.Focus();

        if (!TryTranslatePictureClick(e.Location, _stashPictureBox, out var imagePoint))
        {
            return;
        }

        if (_editLayoutCheckBox.Checked)
        {
            SelectLayoutSlot(imagePoint);
            return;
        }

        if (_lastCurrencyResult is not null)
        {
            await EditCurrencySlotAsync(imagePoint);
            return;
        }

        if (_lastRuneResult is not null)
        {
            await EditRuneSlotAsync(imagePoint);
            return;
        }

        if (_lastKalguuranRuneResult is not null)
        {
            await EditKalguuranRuneSlotAsync(imagePoint);
            return;
        }

        if (_lastGenericResult is not null)
        {
            await EditGenericFixedStashSlotAsync(imagePoint);
        }
    }

    private void SelectLayoutSlot(Point imagePoint)
    {
        var slot = GetCurrentLayoutSlots()
            .Where(candidate => candidate.VisualBounds.Contains(imagePoint))
            .OrderBy(candidate => candidate.VisualBounds.Width * candidate.VisualBounds.Height)
            .FirstOrDefault();
        if (slot is null)
        {
            _selectedLayoutSlotIndex = null;
            _statusLabel.Text = "Layout editor: no slot at that point.";
            UpdateLayoutEditorControls();
            _stashPictureBox.Invalidate();
            return;
        }

        _selectedLayoutSlotIndex = slot.SlotIndex;
        _statusLabel.Text = $"Layout editor: selected {slot.ProfileKey} slot {slot.SlotIndex}. Use arrows to move, Ctrl+arrows to resize, Shift for 10 px.";
        UpdateLayoutEditorControls();
        _stashPictureBox.Invalidate();
    }

    private void StashPictureBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (TryApplyLayoutEditorKey(e.KeyData))
        {
            e.SuppressKeyPress = true;
        }
    }

    private bool TryApplyLayoutEditorKey(Keys keyData)
    {
        if (!_editLayoutCheckBox.Checked || _selectedLayoutSlotIndex is null)
        {
            return false;
        }

        var keyCode = keyData & Keys.KeyCode;
        if (keyCode is not (Keys.Left or Keys.Right or Keys.Up or Keys.Down))
        {
            return false;
        }

        var slot = GetCurrentLayoutSlots().FirstOrDefault(candidate => candidate.SlotIndex == _selectedLayoutSlotIndex.Value);
        if (slot is null)
        {
            return false;
        }

        var step = keyData.HasFlag(Keys.Shift) ? 10 : 1;
        var bounds = slot.VisualBounds;
        if (keyData.HasFlag(Keys.Control))
        {
            bounds = keyCode switch
            {
                Keys.Left => new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width - step), bounds.Height),
                Keys.Right => new Rectangle(bounds.X, bounds.Y, bounds.Width + step, bounds.Height),
                Keys.Up => new Rectangle(bounds.X, bounds.Y, bounds.Width, Math.Max(1, bounds.Height - step)),
                Keys.Down => new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height + step),
                _ => bounds
            };
        }
        else
        {
            bounds = keyCode switch
            {
                Keys.Left => new Rectangle(bounds.X - step, bounds.Y, bounds.Width, bounds.Height),
                Keys.Right => new Rectangle(bounds.X + step, bounds.Y, bounds.Width, bounds.Height),
                Keys.Up => new Rectangle(bounds.X, bounds.Y - step, bounds.Width, bounds.Height),
                Keys.Down => new Rectangle(bounds.X, bounds.Y + step, bounds.Width, bounds.Height),
                _ => bounds
            };
        }

        var canonicalBounds = GetCurrentCoordinateMapper().UnscaleRectToBase(bounds);
        _slotLayoutOverrides.Set(slot.ProfileKey, slot.SlotIndex, canonicalBounds);
        _statusLabel.Text = $"Layout editor: {slot.ProfileKey} slot {slot.SlotIndex} = {bounds.X},{bounds.Y},{bounds.Width},{bounds.Height} displayed; saved as {canonicalBounds.X},{canonicalBounds.Y},{canonicalBounds.Width},{canonicalBounds.Height} base (unsaved).";
        _stashPictureBox.Invalidate();
        return true;
    }

    private void SaveLayoutOverrides()
    {
        try
        {
            _slotLayoutOverrideStore.Save(_slotLayoutOverrides);
            _statusLabel.Text = "Layout overrides saved to slot-layout-overrides.json.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Layout override save failed.";
            _detailsBox.Text = ex.ToString();
        }
    }

    private void ReloadLayoutOverrides()
    {
        _slotLayoutOverrides = _slotLayoutOverrideStore.Load();
        _selectedLayoutSlotIndex = null;
        UpdateLayoutEditorControls();
        _stashPictureBox.Invalidate();
        _statusLabel.Text = "Layout overrides reloaded from slot-layout-overrides.json.";
    }

    private void ResetSelectedLayoutSlot()
    {
        var profileKey = GetCurrentProfileKey();
        if (profileKey is null || _selectedLayoutSlotIndex is null)
        {
            return;
        }

        var slotIndex = _selectedLayoutSlotIndex.Value;
        _slotLayoutOverrides.Remove(profileKey, slotIndex);
        UpdateLayoutEditorControls();
        _stashPictureBox.Invalidate();
        _statusLabel.Text = $"Layout editor: reset {profileKey} slot {slotIndex} to its default visual bounds (unsaved).";
    }

    private void ResetCurrentLayoutTab()
    {
        var profileKey = GetCurrentProfileKey();
        if (profileKey is null)
        {
            return;
        }

        _slotLayoutOverrides.RemoveProfile(profileKey);
        _selectedLayoutSlotIndex = null;
        UpdateLayoutEditorControls();
        _stashPictureBox.Invalidate();
        _statusLabel.Text = $"Layout editor: reset all {profileKey} slot overrides to defaults (unsaved).";
    }

    private void UpdateLayoutEditorControls()
    {
        var hasDisplayedProfile = GetCurrentProfileKey() is not null && _stashPictureBox.Image is not null;
        var enabled = _editLayoutCheckBox.Checked && hasDisplayedProfile;
        _saveLayoutButton.Enabled = enabled;
        _reloadLayoutButton.Enabled = enabled;
        _resetCurrentTabButton.Enabled = enabled;
        _resetSelectedSlotButton.Enabled = enabled && _selectedLayoutSlotIndex is not null;
        _saveLayoutMenuItem.Enabled = enabled;
        _reloadLayoutMenuItem.Enabled = enabled;
        _resetCurrentTabMenuItem.Enabled = enabled;
        _resetSelectedSlotMenuItem.Enabled = enabled && _selectedLayoutSlotIndex is not null;
    }

    private string? GetCurrentProfileKey()
    {
        if (_lastCurrencyResult is not null)
        {
            return FixedStashScannerProfiles.Currency.Key;
        }

        if (_lastRuneResult is not null)
        {
            return FixedStashScannerProfiles.AugmentRunes.Key;
        }

        if (_lastKalguuranRuneResult is not null)
        {
            return FixedStashScannerProfiles.KalguuranRunes.Key;
        }

        return _lastGenericResult?.Profile.Key;
    }

    private StashCoordinateMapper GetCurrentCoordinateMapper()
    {
        var size = _lastCurrencyResult?.ScreenBounds.Size ??
            _lastRuneResult?.ScreenBounds.Size ??
            _lastKalguuranRuneResult?.ScreenBounds.Size ??
            _lastGenericResult?.ScreenBounds.Size;

        return size is { Width: > 0, Height: > 0 } &&
            ScreenshotResolutionProfile.TryDetect(size.Value, out var profile)
            ? new StashCoordinateMapper(profile)
            : StashCoordinateMapper.Base;
    }

    private static string FormatResolutionProfile(Size size)
    {
        return ScreenshotResolutionProfile.TryDetect(size, out var profile)
            ? profile.Label
            : $"{size.Width}x{size.Height} unsupported";
    }

    private IEnumerable<LayoutSlotCandidate> GetCurrentLayoutSlots()
    {
        if (_lastCurrencyResult is not null)
        {
            foreach (var slot in _lastCurrencyResult.Slots)
            {
                yield return new LayoutSlotCandidate(
                    FixedStashScannerProfiles.Currency.Key,
                    slot.SlotIndex,
                    ResolveVisualOverlayBounds(FixedStashScannerProfiles.Currency.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset));
            }

            yield break;
        }

        if (_lastRuneResult is not null)
        {
            foreach (var slot in _lastRuneResult.Slots)
            {
                yield return new LayoutSlotCandidate(
                    FixedStashScannerProfiles.AugmentRunes.Key,
                    slot.SlotIndex,
                    ResolveVisualOverlayBounds(FixedStashScannerProfiles.AugmentRunes.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.AugmentRuneOverlayInset));
            }

            yield break;
        }

        if (_lastKalguuranRuneResult is not null)
        {
            foreach (var slot in _lastKalguuranRuneResult.Slots)
            {
                yield return new LayoutSlotCandidate(
                    FixedStashScannerProfiles.KalguuranRunes.Key,
                    slot.SlotIndex,
                    ResolveVisualOverlayBounds(FixedStashScannerProfiles.KalguuranRunes.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset));
            }

            yield break;
        }

        if (_lastGenericResult is not null)
        {
            foreach (var slot in _lastGenericResult.Slots)
            {
                yield return new LayoutSlotCandidate(
                    _lastGenericResult.Profile.Key,
                    slot.SlotIndex,
                    ResolveVisualOverlayBounds(_lastGenericResult.Profile.Key, slot.SlotIndex, slot.CropBounds, slot.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset));
            }
        }
    }

    private async Task EditCurrencySlotAsync(Point imagePoint)
    {
        if (_lastCurrencyResult is null)
        {
            return;
        }

        var slot = _lastCurrencyResult.Slots
            .Where(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.Currency.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Contains(imagePoint))
            .OrderBy(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.Currency.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Width *
                ResolveVisualOverlayBounds(FixedStashScannerProfiles.Currency.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Height)
            .FirstOrDefault();
        if (slot is null)
        {
            return;
        }

        var iconSuggestions = await GetIconSuggestionsAsync(
            _lastCurrencyResult.StashCropPath,
            slot.CropBounds,
            new IconMatchContext(
                FixedStashScannerProfiles.Currency.Key,
                FixedStashScannerProfiles.Currency.IconCategories),
            CancellationToken.None);
        var countPreview = TryCreateCountCropPreview(
            _lastCurrencyResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.Currency.Key,
            slot.SlotIndex,
            slot.Quantity,
            slot.CountMethod);

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _currencyMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions,
            countPreview);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _currencyScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        ApplyManualCorrectionToDisplayedCurrencySlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        var iconTemplateStatus = TrySaveIconTemplateFromMapping(
            _lastCurrencyResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.Currency.Key,
            slot.SlotIndex,
            dialog.ItemName,
            null);
        var countStatus = dialog.CountOverride is null
            ? "using OCR count"
            : $"count override x{dialog.CountOverride}";
        var trainingStatus = TrySaveDigitTrainingFromOverride(
            _lastCurrencyResult.StashCropPath,
            slot.CropBounds,
            dialog.CountOverride,
            slot.Quantity,
            "currency",
            slot.SlotIndex);
        _statusLabel.Text = $"Saved slot {slot.SlotIndex} as {dialog.ItemName} ({countStatus}{trainingStatus}{iconTemplateStatus}). Scan currency again to reprice.";
    }

    private async Task EditRuneSlotAsync(Point imagePoint)
    {
        if (_lastRuneResult is null)
        {
            return;
        }

        var slot = _lastRuneResult.Slots
            .Where(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.AugmentRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.AugmentRuneOverlayInset).Contains(imagePoint))
            .OrderBy(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.AugmentRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.AugmentRuneOverlayInset).Width *
                ResolveVisualOverlayBounds(FixedStashScannerProfiles.AugmentRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.AugmentRuneOverlayInset).Height)
            .FirstOrDefault();
        if (slot is null)
        {
            return;
        }

        var iconSuggestions = await GetIconSuggestionsAsync(
            _lastRuneResult.StashCropPath,
            slot.CropBounds,
            new IconMatchContext(
                FixedStashScannerProfiles.AugmentRunes.Key,
                FixedStashScannerProfiles.AugmentRunes.IconCategories),
            CancellationToken.None);
        var countPreview = TryCreateCountCropPreview(
            _lastRuneResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.AugmentRunes.Key,
            slot.SlotIndex,
            slot.Quantity,
            slot.CountMethod);

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _runeMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions,
            countPreview);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _runeScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        ApplyManualCorrectionToDisplayedRuneSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        var iconTemplateStatus = TrySaveIconTemplateFromMapping(
            _lastRuneResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.AugmentRunes.Key,
            slot.SlotIndex,
            dialog.ItemName,
            null);
        var countStatus = dialog.CountOverride is null
            ? "using OCR count"
            : $"count override x{dialog.CountOverride}";
        var trainingStatus = TrySaveDigitTrainingFromOverride(
            _lastRuneResult.StashCropPath,
            slot.CropBounds,
            dialog.CountOverride,
            slot.Quantity,
            "runes",
            slot.SlotIndex);
        _statusLabel.Text = $"Saved rune slot {slot.SlotIndex} as {dialog.ItemName} ({countStatus}{trainingStatus}{iconTemplateStatus}). Scan Aug Runes again to reprice.";
    }

    private async Task EditKalguuranRuneSlotAsync(Point imagePoint)
    {
        if (_lastKalguuranRuneResult is null)
        {
            return;
        }

        var slot = _lastKalguuranRuneResult.Slots
            .Where(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.KalguuranRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset).Contains(imagePoint))
            .OrderBy(candidate => ResolveVisualOverlayBounds(FixedStashScannerProfiles.KalguuranRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset).Width *
                ResolveVisualOverlayBounds(FixedStashScannerProfiles.KalguuranRunes.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.KalguuranRuneOverlayInset).Height)
            .FirstOrDefault();
        if (slot is null)
        {
            return;
        }

        var iconSuggestions = await GetIconSuggestionsAsync(
            _lastKalguuranRuneResult.StashCropPath,
            slot.CropBounds,
            new IconMatchContext(
                FixedStashScannerProfiles.KalguuranRunes.Key,
                FixedStashScannerProfiles.KalguuranRunes.IconCategories),
            CancellationToken.None);
        var countPreview = TryCreateCountCropPreview(
            _lastKalguuranRuneResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.KalguuranRunes.Key,
            slot.SlotIndex,
            slot.Quantity,
            slot.CountMethod);

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _kalguuranRuneMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions,
            countPreview);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _kalguuranRuneScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        ApplyManualCorrectionToDisplayedKalguuranRuneSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        var iconTemplateStatus = TrySaveIconTemplateFromMapping(
            _lastKalguuranRuneResult.StashCropPath,
            slot.CropBounds,
            FixedStashScannerProfiles.KalguuranRunes.Key,
            slot.SlotIndex,
            dialog.ItemName,
            null);
        var countStatus = dialog.CountOverride is null
            ? "using OCR count"
            : $"count override x{dialog.CountOverride}";
        var trainingStatus = TrySaveDigitTrainingFromOverride(
            _lastKalguuranRuneResult.StashCropPath,
            slot.CropBounds,
            dialog.CountOverride,
            slot.Quantity,
            "kalguuran-runes",
            slot.SlotIndex);
        _statusLabel.Text = $"Saved Kalguuran rune slot {slot.SlotIndex} as {dialog.ItemName} ({countStatus}{trainingStatus}{iconTemplateStatus}). Scan Kalguuran Runes again to reprice.";
    }

    private async Task EditGenericFixedStashSlotAsync(Point imagePoint)
    {
        if (_lastGenericResult is null)
        {
            return;
        }

        var slot = _lastGenericResult.Slots
            .Where(candidate => ResolveVisualOverlayBounds(_lastGenericResult.Profile.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Contains(imagePoint))
            .OrderBy(candidate => ResolveVisualOverlayBounds(_lastGenericResult.Profile.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Width *
                ResolveVisualOverlayBounds(_lastGenericResult.Profile.Key, candidate.SlotIndex, candidate.CropBounds, candidate.OverlayCropBounds, FixedStashScannerProfiles.DefaultStaticOverlayInset).Height)
            .FirstOrDefault();
        if (slot is null)
        {
            return;
        }

        var isEssence = EssenceStaticIdentity.IsEssenceProfile(_lastGenericResult.Profile);
        var iconSuggestions = isEssence
            ? Array.Empty<PoeNinjaIconMatch>()
            : await GetIconSuggestionsAsync(
                _lastGenericResult.StashCropPath,
                slot.CropBounds,
                new IconMatchContext(
                    _lastGenericResult.Profile.Key,
                    _lastGenericResult.Profile.IconCategories,
                    _lastGenericResult.Profile.Slots[slot.SlotIndex].Section),
                CancellationToken.None);
        var countPreview = TryCreateCountCropPreview(
            _lastGenericResult.StashCropPath,
            slot.CropBounds,
            _lastGenericResult.Profile.Key,
            slot.SlotIndex,
            slot.Quantity,
            slot.CountMethod);

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(slot.SlotIndex),
            iconSuggestions,
            countPreview);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var mappedSlotCount = _genericScanners[_lastGenericResult.Profile.Key].SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        ApplyManualCorrectionToDisplayedGenericSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
        var iconTemplateStatus = isEssence
            ? string.Empty
            : TrySaveIconTemplateFromMapping(
                _lastGenericResult.StashCropPath,
                slot.CropBounds,
                _lastGenericResult.Profile.Key,
                slot.SlotIndex,
                dialog.ItemName,
                _lastGenericResult.Profile.Slots[slot.SlotIndex].Section);
        var countStatus = dialog.CountOverride is null
            ? "using local count"
            : $"count override x{dialog.CountOverride}";
        var staticIdentityStatus = isEssence && mappedSlotCount > 1
            ? $", filled Essence group ({mappedSlotCount} tier names)"
            : string.Empty;
        var trainingStatus = TrySaveDigitTrainingFromOverride(
            _lastGenericResult.StashCropPath,
            slot.CropBounds,
            dialog.CountOverride,
            slot.Quantity,
            _lastGenericResult.Profile.CountMode,
            slot.SlotIndex);
        _statusLabel.Text = $"Saved {_lastGenericResult.Profile.Label} slot {slot.SlotIndex} as {dialog.ItemName} ({countStatus}{staticIdentityStatus}{trainingStatus}{(isEssence ? string.Empty : iconTemplateStatus)}). Scan this tab again to reprice.";
    }

    private void ApplyManualCorrectionToDisplayedCurrencySlot(int slotIndex, string itemName, int? countOverride)
    {
        if (_lastCurrencyResult is null)
        {
            return;
        }

        var slots = _lastCurrencyResult.Slots.ToArray();
        var index = Array.FindIndex(slots, slot => slot.SlotIndex == slotIndex);
        if (index < 0)
        {
            return;
        }

        slots[index] = ApplyManualCorrection(slots[index], slotIndex, itemName, countOverride, _currencyMappingStore);
        _lastCurrencyResult = _lastCurrencyResult with { Slots = slots };
        _stashPictureBox.Invalidate();
    }

    private void ApplyManualCorrectionToDisplayedRuneSlot(int slotIndex, string itemName, int? countOverride)
    {
        if (_lastRuneResult is null)
        {
            return;
        }

        var slots = _lastRuneResult.Slots.ToArray();
        var index = Array.FindIndex(slots, slot => slot.SlotIndex == slotIndex);
        if (index < 0)
        {
            return;
        }

        slots[index] = ApplyManualCorrection(slots[index], slotIndex, itemName, countOverride, _runeMappingStore);
        _lastRuneResult = _lastRuneResult with { Slots = slots };
        _stashPictureBox.Invalidate();
    }

    private void ApplyManualCorrectionToDisplayedKalguuranRuneSlot(int slotIndex, string itemName, int? countOverride)
    {
        if (_lastKalguuranRuneResult is null)
        {
            return;
        }

        var slots = _lastKalguuranRuneResult.Slots.ToArray();
        var index = Array.FindIndex(slots, slot => slot.SlotIndex == slotIndex);
        if (index < 0)
        {
            return;
        }

        slots[index] = ApplyManualCorrection(slots[index], slotIndex, itemName, countOverride, _kalguuranRuneMappingStore);
        _lastKalguuranRuneResult = _lastKalguuranRuneResult with { Slots = slots };
        _stashPictureBox.Invalidate();
    }

    private void ApplyManualCorrectionToDisplayedGenericSlot(int slotIndex, string itemName, int? countOverride)
    {
        if (_lastGenericResult is null || !_genericMappingStores.TryGetValue(_lastGenericResult.Profile.Key, out var mappingStore))
        {
            return;
        }

        var slots = _lastGenericResult.Slots.ToArray();
        var index = Array.FindIndex(slots, slot => slot.SlotIndex == slotIndex);
        if (index < 0)
        {
            return;
        }

        slots[index] = ApplyManualCorrection(slots[index], slotIndex, itemName, countOverride, mappingStore);
        _lastGenericResult = _lastGenericResult with { Slots = slots };
        _stashPictureBox.Invalidate();
    }

    private static CurrencySlotDetection ApplyManualCorrection(
        CurrencySlotDetection slot,
        int slotIndex,
        string itemName,
        int? countOverride,
        CurrencyMappingStore mappingStore)
    {
        return slot with
        {
            ItemName = NormalizeManualItemName(itemName),
            Quantity = countOverride ?? slot.Quantity,
            Exalts = null,
            Divines = null,
            IsCustomMapped = mappingStore.IsCustomMapped(slotIndex),
            IsCountOverridden = mappingStore.IsCountOverridden(slotIndex),
            CountConfidence = countOverride is null ? slot.CountConfidence : 1,
            CountMethod = ResolveManualCorrectionCountMethod(slot.CountMethod, countOverride)
        };
    }

    private static RuneSlotDetection ApplyManualCorrection(
        RuneSlotDetection slot,
        int slotIndex,
        string itemName,
        int? countOverride,
        CurrencyMappingStore mappingStore)
    {
        return slot with
        {
            ItemName = NormalizeManualItemName(itemName),
            Quantity = countOverride ?? slot.Quantity,
            Exalts = null,
            Divines = null,
            IsCustomMapped = mappingStore.IsCustomMapped(slotIndex),
            IsCountOverridden = mappingStore.IsCountOverridden(slotIndex),
            CountConfidence = countOverride is null ? slot.CountConfidence : 1,
            CountMethod = ResolveManualCorrectionCountMethod(slot.CountMethod, countOverride)
        };
    }

    private static FixedStashSlotDetection ApplyManualCorrection(
        FixedStashSlotDetection slot,
        int slotIndex,
        string itemName,
        int? countOverride,
        CurrencyMappingStore mappingStore)
    {
        return slot with
        {
            ItemName = NormalizeManualItemName(itemName),
            Quantity = countOverride ?? slot.Quantity,
            Exalts = null,
            Divines = null,
            IsCustomMapped = mappingStore.IsCustomMapped(slotIndex),
            IsCountOverridden = mappingStore.IsCountOverridden(slotIndex),
            CountConfidence = countOverride is null ? slot.CountConfidence : 1,
            CountMethod = ResolveManualCorrectionCountMethod(slot.CountMethod, countOverride)
        };
    }

    private static string? NormalizeManualItemName(string itemName)
    {
        return string.IsNullOrWhiteSpace(itemName)
            ? null
            : itemName.Trim();
    }

    private static string ResolveManualCorrectionCountMethod(string existingMethod, int? countOverride)
    {
        if (countOverride is not null)
        {
            return "manual-count-override";
        }

        return existingMethod.Equals("manual-count-override", StringComparison.OrdinalIgnoreCase)
            ? "unknown"
            : existingMethod;
    }

    private static string TrySaveDigitTrainingFromOverride(
        string stashCropPath,
        Rectangle cropBounds,
        int? countOverride,
        int? originalGuessedCount,
        string mode,
        int slotIndex)
    {
        if (countOverride is null || !File.Exists(stashCropPath))
        {
            return string.Empty;
        }

        var result = CountTrainingHelpers.TrySaveFromManualCorrection(
            stashCropPath,
            cropBounds,
            countOverride,
            originalGuessedCount,
            mode,
            slotIndex,
            AppPaths.DebugDirectory);
        return result.Length == 0 ? string.Empty : ", " + result.TrimStart();
    }

    private static CountCropSaveResult? TryCreateCountCropPreview(
        string stashCropPath,
        Rectangle cropBounds,
        string mode,
        int slotIndex,
        int? guessedCount,
        string? countMethod)
    {
        try
        {
            var preview = CountCropTrainingStore.TrySavePreviewCrop(
                stashCropPath,
                cropBounds,
                mode,
                slotIndex,
                guessedCount,
                countMethod);
            return preview.Saved ? preview : null;
        }
        catch
        {
            return null;
        }
    }

    private string TrySaveIconTemplateFromMapping(
        string stashCropPath,
        Rectangle cropBounds,
        string tabKey,
        int slotIndex,
        string itemName,
        string? slotSection)
    {
        return string.Empty;
    }

    private Task<IReadOnlyList<PoeNinjaIconMatch>> GetIconSuggestionsAsync(
        string stashCropPath,
        Rectangle cropBounds,
        string iconType,
        CancellationToken cancellationToken)
    {
        return GetIconSuggestionsAsync(
            stashCropPath,
            cropBounds,
            new IconMatchContext(
                "Unknown",
                new HashSet<string>([iconType], StringComparer.OrdinalIgnoreCase)),
            cancellationToken);
    }

    private Task<IReadOnlyList<PoeNinjaIconMatch>> GetIconSuggestionsAsync(
        string stashCropPath,
        Rectangle cropBounds,
        IconMatchContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<PoeNinjaIconMatch>>(Array.Empty<PoeNinjaIconMatch>());
    }

    private static Rectangle GetImageDisplayRectangle(PictureBox pictureBox)
    {
        if (pictureBox.Image is null)
        {
            return Rectangle.Empty;
        }

        var image = pictureBox.Image;
        var imageRatio = image.Width / (float)image.Height;
        var boxRatio = pictureBox.ClientSize.Width / (float)pictureBox.ClientSize.Height;

        int width;
        int height;
        if (boxRatio > imageRatio)
        {
            height = pictureBox.ClientSize.Height;
            width = (int)Math.Round(height * imageRatio);
        }
        else
        {
            width = pictureBox.ClientSize.Width;
            height = (int)Math.Round(width / imageRatio);
        }

        return new Rectangle(
            (pictureBox.ClientSize.Width - width) / 2,
            (pictureBox.ClientSize.Height - height) / 2,
            width,
            height);
    }

    private static bool TryTranslatePictureClick(Point click, PictureBox pictureBox, out Point imagePoint)
    {
        imagePoint = Point.Empty;
        if (pictureBox.Image is null)
        {
            return false;
        }

        var rect = GetImageDisplayRectangle(pictureBox);
        if (!rect.Contains(click))
        {
            return false;
        }

        var x = (int)Math.Round((click.X - rect.Left) * pictureBox.Image.Width / (float)rect.Width);
        var y = (int)Math.Round((click.Y - rect.Top) * pictureBox.Image.Height / (float)rect.Height);
        imagePoint = new Point(x, y);
        return true;
    }

    private static Image LoadImageWithoutFileLock(string path)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(path));
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private static Rectangle ClampRectangle(Rectangle rectangle, Size imageSize)
    {
        var x = Math.Clamp(rectangle.X, 0, Math.Max(0, imageSize.Width - 1));
        var y = Math.Clamp(rectangle.Y, 0, Math.Max(0, imageSize.Height - 1));
        var width = Math.Min(rectangle.Width, imageSize.Width - x);
        var height = Math.Min(rectangle.Height, imageSize.Height - y);
        return new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            bitmap.Save(tempPath, ImageFormat.Png);
            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _scanInProgress = busy;
        _runeshapingButton.Enabled = !busy;
        _modeComboBox.Enabled = !busy;
        _insideFolderCheckBox.Enabled = !busy;
        _scanButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _testButton.Enabled = !busy;
        _captureTabButton.Enabled = !busy;
        _aiAnalyzeButton.Enabled = !busy;
        _refreshIconsButton.Enabled = !busy;
        _copySummaryButton.Enabled = !busy;
        _reviewCountCropsButton.Enabled = !busy;
        _aiReadCountsButton.Enabled = !busy;
        _openScreenshotMenuItem.Enabled = !busy;
        _scanRuneshapingMenuItem.Enabled = !busy;
        _scanCurrentStashMenuItem.Enabled = !busy;
        _aiReadCountsMenuItem.Enabled = !busy;
        _recalculateValuesMenuItem.Enabled = !busy;
        _clearCurrentScanMenuItem.Enabled = !busy;
        UseWaitCursor = busy;
        if (status is not null)
        {
            _statusLabel.Text = status;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private sealed record LayoutSlotCandidate(string ProfileKey, int SlotIndex, Rectangle VisualBounds);

    private sealed record AiCountCurrentScanContext(
        string ProfileKey,
        string ProfileLabel,
        string StashCropPath,
        IReadOnlyList<AiCountSlotSource> Slots);

    private sealed record AiCountApplySummary(
        int AppliedCount,
        int NoCountVisibleCount,
        int UnclearCount,
        int UnknownTileIds,
        int MissingTileIds,
        int InvalidOkCounts,
        int SkippedManualOverrides,
        int LockedManualTileCount,
        int ExcludedOccupiedSlots,
        bool ValuesRecalculated,
        string? RecalculationErrorPath,
        decimal? RecalculatedTotalExalts,
        decimal? RecalculatedTotalDivines);

    private sealed record ScanModeOption(string Label, string Key, ScanModeKind Kind, FixedStashScannerProfile? Profile = null)
    {
        public override string ToString() => Label;
    }

    private enum ScanModeKind
    {
        CurrencyStash,
        AugmentRunes,
        KalguuranRunes,
        GenericFixedStash,
        NotImplemented
    }

    private enum SlotLabelAnchor
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight
    }
}
