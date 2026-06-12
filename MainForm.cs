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
    private readonly LatestStashScanStore _latestScanStore;
    private readonly OpenAiVisionHelper _openAiVisionHelper;
    private readonly PoeNinjaIconCache _iconCache;
    private readonly LocalIconTemplateStore _iconTemplateStore;
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
    private readonly Label _statusLabel = new();
    private readonly Label _totalStashValueLabel = new();
    private readonly TextBox _detailsBox = new();
    private readonly PictureBox _stashPictureBox = new();

    private bool _scanInProgress;
    private readonly Dictionary<string, CurrencyScanResult> _savedCurrencyResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RuneScanResult> _savedRuneResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FixedStashScanResult> _savedGenericResults = new(StringComparer.OrdinalIgnoreCase);
    private CurrencyScanResult? _lastCurrencyResult;
    private RuneScanResult? _lastRuneResult;
    private RuneScanResult? _lastKalguuranRuneResult;
    private FixedStashScanResult? _lastGenericResult;
    private Image? _stashImage;
    private PoeNinjaIconMatcher? _iconMatcher;

    public MainForm()
    {
        _scanner = new RuneshapingScanner(Path.Combine(AppContext.BaseDirectory, "debug"));
        _currencyMappingStore = new CurrencyMappingStore(FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.Currency.MappingFileName));
        _currencyScanner = new CurrencyScanner(Path.Combine(AppContext.BaseDirectory, "debug"), _currencyMappingStore);
        _runeMappingStore = new CurrencyMappingStore(
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.AugmentRunes.MappingFileName),
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.AugmentRunes.CountOverrideFileName));
        _layoutSettingsStore = new StashLayoutSettingsStore(Path.Combine(AppContext.BaseDirectory, "config", "stash-layout-settings.json"));
        _latestScanStore = new LatestStashScanStore(Path.Combine(AppContext.BaseDirectory, "config", "latest-stash-scans.json"));
        _runeScanner = new AugmentRuneScanner(Path.Combine(AppContext.BaseDirectory, "debug"), _runeMappingStore);
        _kalguuranRuneMappingStore = new CurrencyMappingStore(
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.KalguuranRunes.MappingFileName),
            FixedStashScannerProfiles.ConfigPath(FixedStashScannerProfiles.KalguuranRunes.CountOverrideFileName));
        _kalguuranRuneScanner = new KalguuranRuneScanner(Path.Combine(AppContext.BaseDirectory, "debug"), _kalguuranRuneMappingStore);
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
                    Path.Combine(AppContext.BaseDirectory, "debug"),
                    _genericMappingStores[mode.Key],
                    mode.Profile!),
                StringComparer.OrdinalIgnoreCase);
        _openAiVisionHelper = new OpenAiVisionHelper(Path.Combine(AppContext.BaseDirectory, "debug", "ai-stash-analysis"));
        _iconCache = PoeNinjaIconCache.CreateDefault();
        _iconTemplateStore = LocalIconTemplateStore.CreateDefault();
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

        var title = new Label
        {
            Text = "POE2 Price Checker",
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Location = new Point(18, 14),
            AutoSize = true
        };

        _runeshapingButton.Text = "Runeshaping";
        _runeshapingButton.Location = new Point(22, 62);
        _runeshapingButton.Size = new Size(120, 34);
        _runeshapingButton.Click += async (_, _) => await ScanLiveAsync();

        _modeComboBox.Location = new Point(154, 63);
        _modeComboBox.Size = new Size(205, 28);
        _modeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeComboBox.Items.AddRange(ScanModes.Cast<object>().ToArray());
        _modeComboBox.SelectedIndex = 0;
        _modeComboBox.SelectedIndexChanged += (_, _) =>
        {
            LoadSelectedModeFolderSetting();
            ShowSavedScanForSelectedMode();
        };

        _insideFolderCheckBox.Text = "Folder";
        _insideFolderCheckBox.Location = new Point(372, 61);
        _insideFolderCheckBox.AutoSize = true;
        _insideFolderCheckBox.Padding = new Padding(0, 5, 0, 5);
        _insideFolderCheckBox.CheckedChanged += (_, _) => SaveSelectedModeFolderSetting();

        _scanButton.Text = "Scan Stash";
        _scanButton.Location = new Point(466, 62);
        _scanButton.Size = new Size(100, 34);
        _scanButton.Click += async (_, _) => await ScanSelectedStashModeAsync();

        _refreshButton.Text = "Refresh Prices";
        _refreshButton.Location = new Point(578, 62);
        _refreshButton.Size = new Size(130, 34);
        _refreshButton.Click += async (_, _) => await RefreshPricesAsync();

        _testButton.Text = "Test";
        _testButton.Location = new Point(720, 62);
        _testButton.Size = new Size(86, 34);
        _testButton.Click += async (_, _) => await ScanTestScreenshotAsync();

        _captureTabButton.Text = "Capture Stash";
        _captureTabButton.Location = new Point(818, 62);
        _captureTabButton.Size = new Size(128, 34);
        _captureTabButton.Click += (_, _) => CaptureStashTabReference();

        _aiAnalyzeButton.Text = "AI Layout";
        _aiAnalyzeButton.Location = new Point(958, 62);
        _aiAnalyzeButton.Size = new Size(112, 34);
        _aiAnalyzeButton.Visible = false;
        _aiAnalyzeButton.Click += async (_, _) => await AnalyzeSelectedStashWithAiAsync();

        _refreshIconsButton.Text = "Icons";
        _refreshIconsButton.Location = new Point(958, 62);
        _refreshIconsButton.Size = new Size(72, 34);
        _refreshIconsButton.Click += async (_, _) => await RefreshIconCacheAsync();

        _copySummaryButton.Text = "Copy";
        _copySummaryButton.Location = new Point(1042, 62);
        _copySummaryButton.Size = new Size(74, 34);
        _copySummaryButton.Click += (_, _) => CopyCurrentSummary();

        _statusLabel.Text = "Runeshaping is separate. Choose a stash mode, then Scan Stash. Hotkeys: F8 Runeshaping, F7 selected stash.";
        _statusLabel.Location = new Point(22, 112);
        _statusLabel.Size = new Size(1200, 24);

        _totalStashValueLabel.Text = "All scanned: 0 tabs | 0ex / 0div";
        _totalStashValueLabel.Location = new Point(500, 14);
        _totalStashValueLabel.Size = new Size(740, 34);
        _totalStashValueLabel.TextAlign = ContentAlignment.MiddleRight;
        _totalStashValueLabel.Font = new Font("Segoe UI", 12.5f, FontStyle.Bold);
        _totalStashValueLabel.ForeColor = Color.FromArgb(92, 255, 124);
        _totalStashValueLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _totalStashValueLabel.AutoEllipsis = true;

        _stashPictureBox.Location = new Point(22, 146);
        _stashPictureBox.Size = new Size(780, 650);
        _stashPictureBox.BorderStyle = BorderStyle.FixedSingle;
        _stashPictureBox.BackColor = Color.FromArgb(24, 24, 24);
        _stashPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        _stashPictureBox.Paint += StashPictureBox_Paint;
        _stashPictureBox.MouseClick += StashPictureBox_MouseClick;

        _detailsBox.Location = new Point(820, 146);
        _detailsBox.Size = new Size(420, 650);
        _detailsBox.Multiline = true;
        _detailsBox.ReadOnly = true;
        _detailsBox.ScrollBars = ScrollBars.Vertical;
        _detailsBox.WordWrap = true;
        _detailsBox.Font = new Font("Segoe UI", 10f);
        _detailsBox.BackColor = Color.FromArgb(18, 18, 18);
        _detailsBox.ForeColor = Color.Gainsboro;
        _detailsBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        _stashPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        Controls.AddRange([title, _runeshapingButton, _modeComboBox, _insideFolderCheckBox, _scanButton, _refreshButton, _testButton, _captureTabButton, _refreshIconsButton, _copySummaryButton, _statusLabel, _totalStashValueLabel, _stashPictureBox, _detailsBox]);
        LoadSelectedModeFolderSetting();
        UpdateAllScannedTabsTotal();
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
            _iconMatcher = null;
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
        _stashImage?.Dispose();
        _stashImage = null;
        _stashPictureBox.Image = null;
        _stashPictureBox.Invalidate();
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
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "debug"));
            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, "debug", "latest-stash-scans-save-error.txt"),
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

        SetBusy(true, "Scanning active 4K screen...");
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
            var cropRegion = ClampRectangle(GetSelectedStashLayout().DisplayCropRegion, screenshot.Size);
            using var stashCrop = screenshot.Clone(cropRegion, screenshot.PixelFormat);

            var captureDirectory = Path.Combine(AppContext.BaseDirectory, "debug", "stash-tab-captures");
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
            _stashImage?.Dispose();
            _stashImage = LoadImageWithoutFileLock(cropPath);
            _stashPictureBox.Image = _stashImage;
            _stashPictureBox.Invalidate();

            _statusLabel.Text = "Stash tab reference captured.";
            _detailsBox.Text = string.Join(Environment.NewLine, [
                "Stash tab capture",
                string.Empty,
                "Use this when you want me to build the next fixed-layout tab.",
                "Open the tab in PoE, click Capture Stash Tab, then tell me which tab it is.",
                string.Empty,
                $"Screen: {screen.Bounds.Width}x{screen.Bounds.Height} at {screen.Bounds.Left},{screen.Bounds.Top}",
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
            var cropRegion = ClampRectangle(layout.DisplayCropRegion, screenshot.Size);
            using var stashCrop = screenshot.Clone(cropRegion, screenshot.PixelFormat);

            _lastCurrencyResult = null;
            _lastRuneResult = null;
            _lastKalguuranRuneResult = null;
            _lastGenericResult = null;
            _stashImage?.Dispose();
            _stashImage = new Bitmap(stashCrop);
            _stashPictureBox.Image = _stashImage;
            _stashPictureBox.Invalidate();

            _statusLabel.Text = "Sending stash crop to OpenAI...";
            var result = await _openAiVisionHelper.AnalyzeStashAsync(
                stashCrop,
                mode.Label,
                layout,
                CancellationToken.None).ConfigureAwait(true);

            ShowAiAnalysisResult(result);
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
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Currency total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div";

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
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();

        var profitable = result.UpgradeSuggestions.Count(suggestion => suggestion.IsProfitable);
        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div. Profitable upgrades: {profitable}.";

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
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}Kalguuran Runes total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div.";

        var lines = new List<string>
            {
                "Augment Kalguuran Runes workbench",
                savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
                string.Empty,
                $"Known occupied slots: {result.KnownOccupiedSlots}",
                $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
                string.Empty,
                "Click a boxed Kalguuran rune slot to name or correct it.",
                "Use icon suggestions as mapping hints; item names are not auto-applied.",
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
        _stashImage?.Dispose();
        _stashImage = File.Exists(result.StashCropPath)
            ? LoadImageWithoutFileLock(result.StashCropPath)
            : null;
        _stashPictureBox.Image = _stashImage;
        _stashPictureBox.Invalidate();

        _statusLabel.Text = $"{(savedView ? "Saved " : string.Empty)}{result.Profile.Label} total: {result.TotalExalts:0.##} ex / {result.TotalDivines:0.####} div.";

        var lines = new List<string>
        {
            $"{result.Profile.Label} workbench",
            savedView ? "Showing latest saved scan for this mode." : "Fresh scan saved for this mode.",
            string.Empty,
            $"Known occupied slots: {result.KnownOccupiedSlots}",
            $"Unknown occupied slots: {result.UnknownOccupiedSlots}",
            string.Empty,
            "Click a boxed slot to name or correct it.",
            "Use icon suggestions as mapping hints; item names are not auto-applied.",
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
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, slot.OverlayCropBounds ?? slot.CropBounds, slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence);
            }

            return;
        }

        if (_lastRuneResult is not null)
        {
            DrawRuneUpgradeSummary(e.Graphics, imageRect);
            DrawRuneTopStacksSummary(e.Graphics, imageRect);

            foreach (var slot in _lastRuneResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, slot.OverlayCropBounds ?? slot.CropBounds, slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence);
            }
        }

        if (_lastKalguuranRuneResult is not null)
        {
            DrawRuneTopStacksSummary(e.Graphics, imageRect, _lastKalguuranRuneResult, "Top 5 Kalguuran Runes/Prices");

            foreach (var slot in _lastKalguuranRuneResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, slot.OverlayCropBounds ?? slot.CropBounds, slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence);
            }
        }

        if (_lastGenericResult is not null)
        {
            DrawGenericTopStacksSummary(e.Graphics, imageRect, _lastGenericResult);

            foreach (var slot in _lastGenericResult.Slots)
            {
                DrawSlotOverlay(e.Graphics, imageRect, scaleX, scaleY, slot.OverlayCropBounds ?? slot.CropBounds, slot.Occupied, slot.ItemName, slot.Quantity, slot.Exalts, slot.Divines, slot.IsCustomMapped, slot.IsCountOverridden, slot.CountConfidence);
            }
        }
    }

    private static void DrawSlotOverlay(
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
        double countConfidence)
    {
        var forcedEmpty = !occupied && isCountOverridden && quantity == 0;
        var mappedEmpty = !occupied && (itemName is not null || forcedEmpty);
        var color = !occupied
            ? mappedEmpty
                ? Color.FromArgb(185, 90, 180, 255)
                : Color.FromArgb(90, 120, 120, 120)
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

        if (!TryTranslatePictureClick(e.Location, _stashPictureBox, out var imagePoint))
        {
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

    private async Task EditCurrencySlotAsync(Point imagePoint)
    {
        if (_lastCurrencyResult is null)
        {
            return;
        }

        var slot = _lastCurrencyResult.Slots
            .Where(candidate => candidate.CropBounds.Contains(imagePoint))
            .OrderBy(candidate => candidate.CropBounds.Width * candidate.CropBounds.Height)
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

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _currencyMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _currencyScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
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
            .Where(candidate => candidate.CropBounds.Contains(imagePoint))
            .OrderBy(candidate => candidate.CropBounds.Width * candidate.CropBounds.Height)
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

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _runeMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _runeScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
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
            .Where(candidate => candidate.CropBounds.Contains(imagePoint))
            .OrderBy(candidate => candidate.CropBounds.Width * candidate.CropBounds.Height)
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

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _kalguuranRuneMappingStore.GetCountOverride(slot.SlotIndex),
            iconSuggestions);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _kalguuranRuneScanner.SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
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
            .Where(candidate => candidate.CropBounds.Contains(imagePoint))
            .OrderBy(candidate => candidate.CropBounds.Width * candidate.CropBounds.Height)
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

        using var dialog = new SlotMappingDialog(
            slot.ItemName ?? string.Empty,
            slot.Quantity,
            _genericScanners[_lastGenericResult.Profile.Key].GetCountOverride(slot.SlotIndex),
            iconSuggestions);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var mappedSlotCount = _genericScanners[_lastGenericResult.Profile.Key].SetSlot(slot.SlotIndex, dialog.ItemName, dialog.CountOverride);
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
            _lastGenericResult.Profile.CountMode,
            slot.SlotIndex);
        _statusLabel.Text = $"Saved {_lastGenericResult.Profile.Label} slot {slot.SlotIndex} as {dialog.ItemName} ({countStatus}{staticIdentityStatus}{trainingStatus}{(isEssence ? string.Empty : iconTemplateStatus)}). Scan this tab again to reprice.";
    }

    private static string TrySaveDigitTrainingFromOverride(
        string stashCropPath,
        Rectangle cropBounds,
        int? countOverride,
        string mode,
        int slotIndex)
    {
        if (countOverride is null || !File.Exists(stashCropPath))
        {
            return string.Empty;
        }

        var result = CountTrainingHelpers.TrySaveFromOverride(
            stashCropPath,
            cropBounds,
            countOverride,
            mode,
            slotIndex,
            Path.Combine(AppContext.BaseDirectory, "debug"));
        return result.Length == 0 ? string.Empty : ", " + result.TrimStart();
    }

    private string TrySaveIconTemplateFromMapping(
        string stashCropPath,
        Rectangle cropBounds,
        string tabKey,
        int slotIndex,
        string itemName,
        string? slotSection)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return string.Empty;
        }

        try
        {
            var templatePath = _iconTemplateStore.SaveTemplate(
                stashCropPath,
                cropBounds,
                tabKey,
                slotIndex,
                itemName,
                slotSection);
            if (templatePath.Length == 0)
            {
                return string.Empty;
            }

            _iconMatcher = null;
            return ", icon template saved";
        }
        catch
        {
            return ", icon template save failed";
        }
    }

    private async Task<IReadOnlyList<PoeNinjaIconMatch>> GetIconSuggestionsAsync(
        string stashCropPath,
        Rectangle cropBounds,
        string iconType,
        CancellationToken cancellationToken)
    {
        return await GetIconSuggestionsAsync(
            stashCropPath,
            cropBounds,
            new IconMatchContext(
                "Unknown",
                new HashSet<string>([iconType], StringComparer.OrdinalIgnoreCase)),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task<IReadOnlyList<PoeNinjaIconMatch>> GetIconSuggestionsAsync(
        string stashCropPath,
        Rectangle cropBounds,
        IconMatchContext context,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(stashCropPath))
        {
            return [];
        }

        var previousStatus = _statusLabel.Text;
        UseWaitCursor = true;
        _statusLabel.Text = "Building icon suggestions...";

        try
        {
            if (_iconMatcher is null)
            {
                var index = await _iconCache.LoadOrBuildAsync(cancellationToken).ConfigureAwait(true);
                _iconMatcher = PoeNinjaIconMatcher.FromIndex(index, _iconTemplateStore);
            }

            using var stashCrop = CurrencyScanner.LoadBitmapWithoutFileLock(stashCropPath);
            var safeBounds = ClampRectangle(cropBounds, stashCrop.Size);
            var suggestions = _iconMatcher.MatchSlot(stashCrop, safeBounds, maxResults: 5, context)
                .Where(match => match.Confidence >= 0.42)
                .ToArray();

            _statusLabel.Text = suggestions.Length == 0
                ? "No icon suggestions found for this slot."
                : $"Icon suggestions ready for {suggestions[0].ItemName}.";
            return suggestions;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Icon suggestions unavailable.";
            _detailsBox.Text = ex.ToString();
            return [];
        }
        finally
        {
            UseWaitCursor = false;
            if (_statusLabel.Text == "Building icon suggestions...")
            {
                _statusLabel.Text = previousStatus;
            }
        }
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
