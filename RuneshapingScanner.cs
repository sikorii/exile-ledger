using System.Drawing.Imaging;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tesseract;

namespace Poe2PriceChecker;

internal sealed class RuneshapingScanner
{
    private static readonly Rectangle Crop3840x2160 = new(407, 300, 680, 1080);
    private static readonly Regex RewardLine = new(
        @"(?<qty>\d+)\s*[xX]\s+(?<name>[A-Za-z][A-Za-z0-9'\u2019 -]{2,})",
        RegexOptions.Compiled);
    private static readonly Regex QuantityLineStart = new(
        @"^\s*\d+\s*[xX]\b",
        RegexOptions.Compiled);
    private static readonly Regex QuantityLineWithTrailingFragment = new(
        @"^(?<prefix>\s*\d+\s*[xX]\s+.*?)(?<fragment>[A-Za-z]{1,5})$",
        RegexOptions.Compiled);
    private static readonly Regex LeadingWordFragment = new(
        @"^(?<fragment>[A-Za-z]{1,6})(?<rest>(?:'s|'S|er'?s|ER'?S)?(?:\s+.+)?)$",
        RegexOptions.Compiled);
    private static readonly JsonSerializerOptions VocabularyJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Lazy<RuneshapingRewardVocabulary> RewardVocabulary = new(LoadRuneshapingRewardVocabulary);
    private static readonly IReadOnlyDictionary<string, string> RuneshapingPriceLookupAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Ancient Rune of Horde"] = "Ancient Rune of the Horde",
            ["Rune of Blossom"] = "Rune of the Blossom",
            ["Rune of Prism"] = "Rune of the Prism",
            ["Saqawal's Rune of Sky"] = "Saqawals Rune Of The Sky"
        };

    private readonly string _debugDirectory;
    private PoeNinjaPrices? _cachedPrices;
    private DateTimeOffset _lastPriceRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _lastMergedScanUtc = DateTimeOffset.MinValue;
    private readonly Dictionary<string, RawReward> _mergedRewards = new(StringComparer.OrdinalIgnoreCase);

    public RuneshapingScanner(string debugDirectory)
    {
        _debugDirectory = debugDirectory;
        Directory.CreateDirectory(_debugDirectory);
    }

    public async Task RefreshPricesAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        _cachedPrices = await PoeNinjaPrices.FetchAsync(cancellationToken, forceRefresh).ConfigureAwait(false);
        _lastPriceRefresh = DateTimeOffset.UtcNow;
    }

    public async Task<ScanResult> ScanScreenAsync(CancellationToken cancellationToken)
    {
        var screen = SelectTargetScreen();
        using var screenshot = CaptureScreen(screen.Bounds);
        return await ScanBitmapAsync(screenshot, screen.Bounds, mergeWithRecentRuneshapingScans: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScanResult> ScanFileAsync(string screenshotPath, CancellationToken cancellationToken)
    {
        using var bitmap = new Bitmap(screenshotPath);
        return await ScanBitmapAsync(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), mergeWithRecentRuneshapingScans: false, cancellationToken).ConfigureAwait(false);
    }

    public void ClearMergedRuneshapingRewards()
    {
        _mergedRewards.Clear();
        _lastMergedScanUtc = DateTimeOffset.MinValue;
    }

    private async Task<ScanResult> ScanBitmapAsync(
        Bitmap screenshot,
        Rectangle screenBounds,
        bool mergeWithRecentRuneshapingScans,
        CancellationToken cancellationToken)
    {
        var cropRegion = ResolveCropRegion(screenshot.Size);
        using var crop = screenshot.Clone(cropRegion, screenshot.PixelFormat);
        var panelLooksScrollable = LooksLikeScrollableRewardPanel(crop);
        SaveBitmap(crop, Path.Combine(_debugDirectory, "runeshaping-crop.png"));

        using var processed = PrepareForOcr(crop);
        var processedPath = Path.Combine(_debugDirectory, "runeshaping-ocr.png");
        SaveBitmap(processed, processedPath);

        var tessData = await EnsureTessDataAsync(Path.Combine(AppContext.BaseDirectory, "tessdata"), cancellationToken).ConfigureAwait(false);
        var rawText = RunOcr(processedPath, tessData);
        File.WriteAllText(Path.Combine(_debugDirectory, "runeshaping-ocr.txt"), rawText);
        var rewards = ParseRewards(rawText, out var rewardParseCandidates);

        if (_cachedPrices is null || DateTimeOffset.UtcNow - _lastPriceRefresh > TimeSpan.FromMinutes(30))
        {
            await RefreshPricesAsync(cancellationToken).ConfigureAwait(false);
        }

        var prices = _cachedPrices ?? throw new InvalidOperationException("Runeshaping price cache was not initialized.");
        var notes = new List<string>();
        var vocabulary = RewardVocabulary.Value;
        var matchedRewards = MatchRewards(rewards, vocabulary);
        var canonicalRewards = matchedRewards
            .Select(match => match.Reward)
            .ToArray();
        var rewardsForPricing = mergeWithRecentRuneshapingScans
            ? MergeRewardsForCurrentEncounter(canonicalRewards, notes)
            : canonicalRewards;

        if (panelLooksScrollable || canonicalRewards.Length >= 8)
        {
            notes.Add("Reward list may be scrollable. Scroll the panel and press F8 again to merge hidden rows.");
        }

        var priced = new List<RewardChoice>();
        var unpriced = new List<string>();
        foreach (var reward in rewardsForPricing)
        {
            var priceLookupName = ResolveRuneshapingPriceLookupName(reward.ItemName);
            var value = prices.TryGetValue(priceLookupName, reward.Quantity);
            if (value is null)
            {
                unpriced.Add($"{reward.Quantity}x {reward.ItemName}");
                continue;
            }

            priced.Add(new RewardChoice(
                reward.Quantity,
                reward.ItemName,
                value.Exalts,
                value.Divines,
                ChoiceColor.Red));
        }

        var colored = AssignColors(priced);
        File.WriteAllLines(
            Path.Combine(_debugDirectory, "runeshaping-debug.txt"),
            new[] { "Raw OCR:", rawText, string.Empty, "Parsed rewards:" }
                .Concat(rewards.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Reward parse candidates:" })
                .Concat(rewardParseCandidates.Count == 0
                    ? ["(none)"]
                    : rewardParseCandidates.Select(FormatParseCandidateDebugLine))
                .Concat(new[] { string.Empty, "Canonical match results:" })
                .Concat(matchedRewards.Select(match => FormatMatchDebugLine(match, prices)))
                .Concat(new[] { string.Empty, "Runeshaping vocabulary:" })
                .Concat([$"{vocabulary.CanonicalNames.Count} canonical rewards loaded from {vocabulary.SourcePath}"])
                .Concat(string.IsNullOrWhiteSpace(vocabulary.LoadError) ? [] : [$"load warning: {vocabulary.LoadError}"])
                .Concat(new[] { string.Empty, "Merged rewards:" })
                .Concat(rewardsForPricing.Select(reward => $"{reward.Quantity}x {reward.ItemName}"))
                .Concat(new[] { string.Empty, "Notes:" })
                .Concat(notes.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Unpriced:" })
                .Concat(unpriced.DefaultIfEmpty("(none)"))
                .Concat(new[] { string.Empty, "Unpriced diagnostics:" })
                .Concat(unpriced.Count == 0
                    ? ["(none)"]
                    : unpriced.Select(rewardText =>
                    {
                        var match = Regex.Match(rewardText, @"^\d+x\s+(?<name>.+)$");
                        var name = match.Success ? match.Groups["name"].Value : rewardText;
                        return prices.DiagnoseMissing(ResolveRuneshapingPriceLookupName(name)).ToDebugString();
                    })));
        return new ScanResult(colored, unpriced, notes, rawText, cropRegion, screenBounds);
    }

    private IReadOnlyList<RawReward> MergeRewardsForCurrentEncounter(IReadOnlyList<RawReward> visibleRewards, List<string> notes)
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastMergedScanUtc > TimeSpan.FromMinutes(2))
        {
            _mergedRewards.Clear();
        }

        _lastMergedScanUtc = now;

        foreach (var reward in visibleRewards)
        {
            _mergedRewards.TryAdd($"{reward.Quantity}x {reward.ItemName}", reward);
        }

        if (_mergedRewards.Count > visibleRewards.Count)
        {
            notes.Add($"Merged {_mergedRewards.Count} rewards from this Runeshaping panel. Close the overlay x to reset.");
        }

        return _mergedRewards.Values
            .OrderByDescending(reward => reward.Quantity)
            .ThenBy(reward => reward.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<RewardChoice> AssignColors(List<RewardChoice> choices)
    {
        if (choices.Count == 0)
        {
            return choices;
        }

        var sorted = choices
            .OrderByDescending(choice => choice.Exalts)
            .ThenBy(choice => choice.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var best = sorted[0].Exalts;
        if (best <= 0)
        {
            return sorted;
        }

        return sorted
            .Select(choice =>
            {
                var color =
                    choice.Exalts >= best * 0.99m ? ChoiceColor.Green :
                    choice.Exalts >= best * 0.25m ? ChoiceColor.Yellow :
                    ChoiceColor.Red;

                return choice with { Color = color };
            })
            .ToArray();
    }

    private static IReadOnlyList<RawReward> ParseRewards(string rawText, out IReadOnlyList<RewardParseCandidate> parseCandidates)
    {
        parseCandidates = BuildRewardParseCandidates(rawText);
        var rewards = new List<RawReward>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var gluedParseCandidateKeys = parseCandidates
            .Where(candidate => candidate.Source.Equals("stitched-glued", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.StitchKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in parseCandidates)
        {
            if (candidate.Source.Equals("stitched-spaced", StringComparison.OrdinalIgnoreCase) &&
                gluedParseCandidateKeys.Contains(candidate.StitchKey))
            {
                continue;
            }

            foreach (Match match in RewardLine.Matches(candidate.Text))
            {
                if (!int.TryParse(match.Groups["qty"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) ||
                    quantity <= 0)
                {
                    continue;
                }

                var itemName = NormalizeItemName(match.Groups["name"].Value);
                if (itemName.Length == 0 || itemName.Length > 80)
                {
                    continue;
                }

                var key = $"{quantity}x {itemName}";
                if (seen.Add(key))
                {
                    rewards.Add(new RawReward(quantity, itemName));
                }
            }
        }

        return rewards;
    }

    private static IReadOnlyList<RewardParseCandidate> BuildRewardParseCandidates(string rawText)
    {
        var sourceLines = rawText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        var candidates = new List<RewardParseCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < sourceLines.Length; index++)
        {
            var line = sourceLines[index];
            AddParseCandidate("original", line, candidates, seen);

            if (!QuantityLineStart.IsMatch(line) || RewardLine.IsMatch(line))
            {
                continue;
            }

            var combined = line;
            var joinedLines = 0;
            for (var nextIndex = index + 1; nextIndex < sourceLines.Length && joinedLines < 2; nextIndex++)
            {
                var next = sourceLines[nextIndex];
                if (QuantityLineStart.IsMatch(next) || next.Length > 40)
                {
                    break;
                }

                var candidate = combined + " " + next;
                if (candidate.Length > 90)
                {
                    break;
                }

                combined = candidate;
                joinedLines++;
                var stitchKey = $"{index}:{nextIndex}";
                AddParseCandidate("stitched-spaced", combined, candidates, seen, stitchKey);
                if (TryBuildGluedWordCandidate(line, next, out var gluedCandidate))
                {
                    AddParseCandidate("stitched-glued", gluedCandidate, candidates, seen, stitchKey);
                }

                if (RewardLine.IsMatch(combined))
                {
                    break;
                }
            }
        }

        return candidates;
    }

    private static bool TryBuildGluedWordCandidate(string firstLine, string nextLine, out string candidate)
    {
        candidate = string.Empty;

        var firstMatch = QuantityLineWithTrailingFragment.Match(firstLine);
        if (!firstMatch.Success)
        {
            return false;
        }

        var nextMatch = LeadingWordFragment.Match(nextLine);
        if (!nextMatch.Success)
        {
            return false;
        }

        var prefix = firstMatch.Groups["prefix"].Value;
        var firstFragment = firstMatch.Groups["fragment"].Value;
        var nextFragment = nextMatch.Groups["fragment"].Value;
        var nextRest = nextMatch.Groups["rest"].Value;
        candidate = prefix + firstFragment + nextFragment + nextRest;
        return candidate.Length <= 90;
    }

    private static void AddParseCandidate(
        string source,
        string candidate,
        List<RewardParseCandidate> candidates,
        HashSet<string> seen,
        string stitchKey = "")
    {
        if (seen.Add(candidate))
        {
            candidates.Add(new RewardParseCandidate(source, candidate, stitchKey));
        }
    }

    private static string FormatParseCandidateDebugLine(RewardParseCandidate candidate)
    {
        return $"{candidate.Source}: {candidate.Text}";
    }

    private static string NormalizeItemName(string value)
    {
        var asciiValue = value.Replace("\u2019", "'");
        var cleaned = Regex.Replace(asciiValue, @"\s+", " ").Trim(' ', '-', '\'');
        var titled = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
        return Regex.Replace(titled, @"\b(Of|The|And)\b", m => m.Value.ToLowerInvariant());
    }

    private static IReadOnlyList<RuneshapingRewardMatch> MatchRewards(
        IReadOnlyList<RawReward> rewards,
        RuneshapingRewardVocabulary vocabulary)
    {
        var matches = new List<RuneshapingRewardMatch>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reward in rewards)
        {
            var match = MatchRewardName(reward, vocabulary);
            var key = match.MergeKey;
            if (seen.Add(key))
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    private static RuneshapingRewardMatch MatchRewardName(
        RawReward reward,
        RuneshapingRewardVocabulary vocabulary)
    {
        var itemName = reward.ItemName;
        if (TryFindVocabularyName(itemName, vocabulary, out var known))
        {
            return RuneshapingRewardMatch.Exact(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var repaired = ApplyExplicitRuneshapingRepairs(itemName);
        if (!repaired.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(repaired, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var withoutLeadingJunk = Regex.Replace(repaired, @"^[A-Z]\s+(?=[A-Z])", string.Empty);
        if (!withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(withoutLeadingJunk, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var withoutTrailingJunk = Regex.Replace(repaired, @"\s+[A-Z]$", string.Empty);
        if (!withoutTrailingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(withoutTrailingJunk, vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        if (!repaired.EndsWith(" Rune", StringComparison.OrdinalIgnoreCase) &&
            TryFindVocabularyName(repaired + " Rune", vocabulary, out known))
        {
            return RuneshapingRewardMatch.Alias(reward, known, PoeNinjaPrices.Normalize(itemName));
        }

        var matchingName = !withoutLeadingJunk.Equals(repaired, StringComparison.OrdinalIgnoreCase)
            ? withoutLeadingJunk
            : repaired;

        var nearMatch = FindNearVocabularyName(matchingName, vocabulary);
        if (nearMatch.Accepted)
        {
            return RuneshapingRewardMatch.Near(
                reward,
                nearMatch.KnownName,
                PoeNinjaPrices.Normalize(itemName),
                nearMatch.Distance,
                nearMatch.SecondBestDistance,
                nearMatch.Candidates,
                nearMatch.RejectionReason);
        }

        if (TryFindAnchorVocabularyName(matchingName, vocabulary, out known, out var anchor))
        {
            return RuneshapingRewardMatch.AnchorMatch(
                reward,
                known,
                anchor,
                PoeNinjaPrices.Normalize(itemName),
                nearMatch.Candidates,
                nearMatch.RejectionReason);
        }

        return RuneshapingRewardMatch.Unmatched(
            reward,
            matchingName,
            PoeNinjaPrices.Normalize(itemName),
            nearMatch.Candidates,
            nearMatch.RejectionReason);
    }

    private static string ApplyExplicitRuneshapingRepairs(string itemName)
    {
        var repaired = Regex.Replace(itemName, @"\bJeweller\s+S\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\bJewellers\s+Orb\b", "Jeweller's Orb", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\b(Blacksmith|Arcanist|Armourer|Jeweller)\s+S\s+", "$1's ", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"^Warding Rune of Ical$", "Warding Rune of Protection", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\bof\s+[A-Z]\s+(?=[A-Z])", "of ", RegexOptions.IgnoreCase);
        repaired = Regex.Replace(
            repaired,
            @"^Uncut (?<kind>Skill|Spirit|Support) Gem Level (?<level>\d{1,2})$",
            "Uncut ${kind} Gem (Level ${level})",
            RegexOptions.IgnoreCase);
        repaired = Regex.Replace(repaired, @"\s+", " ").Trim();
        return repaired;
    }

    private static bool LooksLikeScrollableRewardPanel(Bitmap crop)
    {
        var stripLeft = Math.Max(0, crop.Width - 34);
        var stripRight = Math.Max(stripLeft + 1, crop.Width - 5);
        var top = Math.Min(crop.Height - 1, 120);
        var bottom = Math.Max(top + 1, crop.Height - 60);
        var rowsWithTrack = 0;

        for (var y = top; y < bottom; y++)
        {
            var darkPixels = 0;
            var neutralPixels = 0;
            for (var x = stripLeft; x < stripRight; x++)
            {
                var color = crop.GetPixel(x, y);
                var max = Math.Max(color.R, Math.Max(color.G, color.B));
                var min = Math.Min(color.R, Math.Min(color.G, color.B));
                var luminance = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);

                if (luminance < 80)
                {
                    darkPixels++;
                }

                if (max - min < 48 && luminance is > 65 and < 190)
                {
                    neutralPixels++;
                }
            }

            if (darkPixels >= 3 && neutralPixels >= 5)
            {
                rowsWithTrack++;
            }
        }

        return rowsWithTrack > (bottom - top) * 0.22;
    }

    private static bool TryFindVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string knownName)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (vocabulary.NamesByNormalizedKey.TryGetValue(normalized, out var match))
        {
            knownName = match;
            return true;
        }

        knownName = string.Empty;
        return false;
    }

    private static NearKnownNameMatch FindNearVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary)
    {
        var normalized = PoeNinjaPrices.Normalize(itemName);
        if (normalized.Length < 8)
        {
            return NearKnownNameMatch.Rejected("input too short", []);
        }

        var inputTokens = NormalizedTokens(itemName);
        var candidates = vocabulary.MatchCandidates
            .Where(candidate => candidate.NormalizedName.Length >= 8)
            .Where(candidate => HasStrongTokenAnchor(inputTokens, candidate.Tokens))
            .Select(candidate => new NearCandidate(
                candidate.DisplayName,
                EditDistance(normalized, candidate.NormalizedName)))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        var candidateSummaries = candidates
            .Select(candidate => $"{candidate.Name}:{candidate.Distance}")
            .ToArray();
        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return NearKnownNameMatch.Rejected("no anchored candidates", candidateSummaries);
        }

        var maxDistance = normalized.Length <= 12 ? 1 : 2;
        if (best.Distance > maxDistance)
        {
            return NearKnownNameMatch.Rejected($"best distance {best.Distance} > {maxDistance}", candidateSummaries);
        }

        var secondBest = candidates.Skip(1).FirstOrDefault();
        if (secondBest is not null && secondBest.Distance - best.Distance < 2)
        {
            return NearKnownNameMatch.Rejected(
                $"ambiguous best {best.Distance}, second {secondBest.Distance}",
                candidateSummaries);
        }

        return NearKnownNameMatch.AcceptedMatch(best.Name, best.Distance, secondBest?.Distance, candidateSummaries);
    }

    private static IReadOnlyList<KnownNameCandidate> BuildKnownNameCandidates(
        IReadOnlyList<RuneshapingVocabularyItem> entries)
    {
        return entries
            .Select(entry => entry.CanonicalName.Trim())
            .Where(name => name.Length > 0)
            .Select(displayName => new KnownNameCandidate(
                displayName,
                PoeNinjaPrices.Normalize(displayName),
                NormalizedTokens(displayName)))
            .GroupBy(candidate => candidate.NormalizedName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(candidate => candidate.DisplayName.Contains('-', StringComparison.Ordinal))
                .ThenBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();
    }

    private static bool HasStrongTokenAnchor(IReadOnlyList<string> inputTokens, IReadOnlyList<string> candidateTokens)
    {
        return inputTokens
            .Where(token => token.Length >= 4)
            .Any(token => candidateTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryFindAnchorVocabularyName(
        string itemName,
        RuneshapingRewardVocabulary vocabulary,
        out string knownName,
        out string anchor)
    {
        knownName = string.Empty;
        anchor = string.Empty;

        var inputFirstToken = NormalizedTokens(itemName).FirstOrDefault();
        if (inputFirstToken is null)
        {
            return false;
        }

        var anchorFamilies = new[]
        {
            new AnchorFamily("Blacksmith", ["BLACKSMITH", "BLACKSMITHS"]),
            new AnchorFamily("Arcanist", ["ARCANIST", "ARCANISTS"]),
            new AnchorFamily("Armourer", ["ARMOURER", "ARMOURERS"])
        };

        foreach (var family in anchorFamilies)
        {
            if (!family.Tokens.Contains(inputFirstToken, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidates = vocabulary.CanonicalNames
                .Where(candidate =>
                {
                    var candidateFirstToken = NormalizedTokens(candidate).FirstOrDefault();
                    return candidateFirstToken is not null &&
                        family.Tokens.Contains(candidateFirstToken, StringComparer.OrdinalIgnoreCase);
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 1)
            {
                knownName = candidates[0];
                anchor = family.Label;
                return true;
            }
        }

        return false;
    }

    private static RuneshapingRewardVocabulary LoadRuneshapingRewardVocabulary()
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, "Data", "runeshaping_reward_vocabulary.json");
        if (!File.Exists(sourcePath))
        {
            return RuneshapingRewardVocabulary.Empty(sourcePath, "vocabulary file not found");
        }

        try
        {
            using var stream = File.OpenRead(sourcePath);
            var entries = JsonSerializer.Deserialize<List<RuneshapingVocabularyItem>>(stream, VocabularyJsonOptions) ?? [];
            var canonicalEntries = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.CanonicalName))
                .ToArray();
            var namesByNormalizedKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in canonicalEntries)
            {
                AddVocabularyLookup(namesByNormalizedKey, entry.CanonicalName, entry.CanonicalName);

                foreach (var alias in entry.Aliases ?? [])
                {
                    AddVocabularyLookup(namesByNormalizedKey, alias, entry.CanonicalName);
                }

                if (!string.IsNullOrWhiteSpace(entry.NormalizedKey))
                {
                    namesByNormalizedKey.TryAdd(
                        NormalizeVocabularyKey(entry.NormalizedKey),
                        entry.CanonicalName);
                }
            }

            var canonicalNames = namesByNormalizedKey.Values
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return new RuneshapingRewardVocabulary(
                canonicalNames,
                namesByNormalizedKey,
                BuildKnownNameCandidates(canonicalEntries),
                sourcePath,
                string.Empty);
        }
        catch (Exception ex)
        {
            return RuneshapingRewardVocabulary.Empty(sourcePath, ex.Message);
        }
    }

    private static void AddVocabularyLookup(
        Dictionary<string, string> namesByNormalizedKey,
        string sourceName,
        string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return;
        }

        namesByNormalizedKey.TryAdd(PoeNinjaPrices.Normalize(sourceName), canonicalName);
    }

    private static string NormalizeVocabularyKey(string normalizedKey)
    {
        return PoeNinjaPrices.Normalize(Regex.Replace(normalizedKey, @"(?<!^)([A-Z])", " $1"));
    }

    private static string[] NormalizedTokens(string itemName)
    {
        return PoeNinjaPrices.Normalize(itemName)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string FormatMatchDebugLine(RuneshapingRewardMatch match, PoeNinjaPrices prices)
    {
        var priceLookupName = ResolveRuneshapingPriceLookupName(match.Reward.ItemName);
        var price = prices.TryGetValue(priceLookupName, match.Reward.Quantity);
        var canonical = match.Matched
            ? match.Reward.ItemName
            : "(unmatched)";
        var score = match.Distance is null
            ? string.Empty
            : $" distance={match.Distance}";
        var secondBest = match.SecondBestDistance is null
            ? string.Empty
            : $" secondBest={match.SecondBestDistance}";
        var anchor = string.IsNullOrWhiteSpace(match.AnchorLabel)
            ? string.Empty
            : $" anchor={match.AnchorLabel}";
        var exact = match.Method.Equals("exact", StringComparison.OrdinalIgnoreCase)
            ? $" exact={match.Reward.ItemName}"
            : " exact=(none)";
        var nearMatchCandidates = match.NearMatchCandidates ?? [];
        var nearCandidates = nearMatchCandidates.Count == 0
            ? " nearCandidates=(none)"
            : $" nearCandidates={string.Join("; ", nearMatchCandidates)}";
        var nearRejection = string.IsNullOrWhiteSpace(match.NearMatchRejection)
            ? string.Empty
            : $" nearRejected='{match.NearMatchRejection}'";
        var priceLookup = priceLookupName.Equals(match.Reward.ItemName, StringComparison.OrdinalIgnoreCase)
            ? " priceLookup=(canonical)"
            : $" priceLookup='{priceLookupName}'";
        var priceStatus = price is null ? "missing" : "ok";
        return $"{match.ParsedReward.Quantity}x parsed='{match.ParsedReward.ItemName}' normalized='{match.NormalizedParsedName}' canonical='{canonical}' method={match.Method}{exact}{anchor}{score}{secondBest}{nearCandidates}{nearRejection} mergeKey='{match.MergeKey}'{priceLookup} price={priceStatus}";
    }

    private static string ResolveRuneshapingPriceLookupName(string canonicalName)
    {
        return RuneshapingPriceLookupAliases.TryGetValue(canonicalName, out var alias)
            ? alias
            : canonicalName;
    }

    private static int EditDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];

        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var cost = left[i - 1] == right[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[right.Length];
    }

    private sealed record RuneshapingRewardMatch(
        RawReward ParsedReward,
        RawReward Reward,
        string Method,
        string? AnchorLabel = null,
        int? Distance = null,
        int? SecondBestDistance = null,
        string NormalizedParsedName = "",
        IReadOnlyList<string>? NearMatchCandidates = null,
        string NearMatchRejection = "")
    {
        public bool Matched => !Method.Equals("unmatched", StringComparison.OrdinalIgnoreCase);

        public string MergeKey => $"{Reward.Quantity}x {Reward.ItemName}";

        public static RuneshapingRewardMatch Exact(RawReward reward, string canonicalName, string normalizedParsedName)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "exact",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: []);
        }

        public static RuneshapingRewardMatch Alias(RawReward reward, string canonicalName, string normalizedParsedName)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "alias",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: []);
        }

        public static RuneshapingRewardMatch Near(
            RawReward reward,
            string canonicalName,
            string normalizedParsedName,
            int distance,
            int? secondBestDistance,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "near",
                Distance: distance,
                SecondBestDistance: secondBestDistance,
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }

        public static RuneshapingRewardMatch AnchorMatch(
            RawReward reward,
            string canonicalName,
            string anchor,
            string normalizedParsedName,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = canonicalName },
                "anchor",
                anchor,
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }

        public static RuneshapingRewardMatch Unmatched(
            RawReward reward,
            string repairedName,
            string normalizedParsedName,
            IReadOnlyList<string> nearMatchCandidates,
            string nearMatchRejection)
        {
            return new RuneshapingRewardMatch(
                reward,
                reward with { ItemName = repairedName },
                "unmatched",
                NormalizedParsedName: normalizedParsedName,
                NearMatchCandidates: nearMatchCandidates,
                NearMatchRejection: nearMatchRejection);
        }
    }

    private sealed record AnchorFamily(string Label, string[] Tokens);

    private sealed record RewardParseCandidate(string Source, string Text, string StitchKey = "");

    private sealed record RuneshapingVocabularyItem(
        string CanonicalName,
        string NormalizedKey,
        string[]? Aliases);

    private sealed record RuneshapingRewardVocabulary(
        IReadOnlyList<string> CanonicalNames,
        IReadOnlyDictionary<string, string> NamesByNormalizedKey,
        IReadOnlyList<KnownNameCandidate> MatchCandidates,
        string SourcePath,
        string LoadError)
    {
        public static RuneshapingRewardVocabulary Empty(string sourcePath, string loadError)
        {
            return new RuneshapingRewardVocabulary(
                [],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                [],
                sourcePath,
                loadError);
        }
    }

    private sealed record KnownNameCandidate(string DisplayName, string NormalizedName, IReadOnlyList<string> Tokens);

    private sealed record NearCandidate(string Name, int Distance);

    private sealed record NearKnownNameMatch(
        bool Accepted,
        string KnownName,
        int Distance,
        int? SecondBestDistance,
        IReadOnlyList<string> Candidates,
        string RejectionReason)
    {
        public static NearKnownNameMatch AcceptedMatch(
            string knownName,
            int distance,
            int? secondBestDistance,
            IReadOnlyList<string> candidates)
        {
            return new NearKnownNameMatch(true, knownName, distance, secondBestDistance, candidates, string.Empty);
        }

        public static NearKnownNameMatch Rejected(string reason, IReadOnlyList<string> candidates)
        {
            return new NearKnownNameMatch(false, string.Empty, 0, null, candidates, reason);
        }
    }

    private static string RunOcr(string imagePath, string tessDataDirectory)
    {
        using var engine = new TesseractEngine(tessDataDirectory, "eng", EngineMode.Default);
        engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789' -x");
        engine.DefaultPageSegMode = PageSegMode.SparseText;

        using var pix = Pix.LoadFromFile(imagePath);
        using var page = engine.Process(pix);
        return page.GetText();
    }

    private static Bitmap PrepareForOcr(Bitmap input)
    {
        var output = new Bitmap(input.Width * 2, input.Height * 2, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.White);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(input, 0, 0, output.Width, output.Height);

        for (var y = 0; y < output.Height; y++)
        {
            for (var x = 0; x < output.Width; x++)
            {
                var color = output.GetPixel(x, y);
                var luminance = (int)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                var neutralInk = Math.Abs(color.R - color.G) < 55 && Math.Abs(color.G - color.B) < 55;
                var likelyText = luminance < 120 && neutralInk;
                output.SetPixel(x, y, likelyText ? Color.Black : Color.White);
            }
        }

        return output;
    }

    private static Rectangle ResolveCropRegion(Size screenshotSize)
    {
        if (screenshotSize.Width == 3840 && screenshotSize.Height == 2160)
        {
            return Crop3840x2160;
        }

        var scaleX = screenshotSize.Width / 3840d;
        var scaleY = screenshotSize.Height / 2160d;
        return new Rectangle(
            (int)Math.Round(Crop3840x2160.X * scaleX),
            (int)Math.Round(Crop3840x2160.Y * scaleY),
            (int)Math.Round(Crop3840x2160.Width * scaleX),
            (int)Math.Round(Crop3840x2160.Height * scaleY));
    }

    private static Screen SelectTargetScreen()
    {
        if (TryFindPathOfExileScreen(out var poeScreen))
        {
            return poeScreen;
        }

        return Screen.AllScreens
            .OrderByDescending(screen => screen.Bounds.Width == 3840 && screen.Bounds.Height == 2160)
            .ThenByDescending(screen => screen.Bounds.Width * screen.Bounds.Height)
            .FirstOrDefault() ?? Screen.PrimaryScreen!;
    }

    private static bool TryFindPathOfExileScreen(out Screen screen)
    {
        screen = Screen.PrimaryScreen!;
        var matches = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            GetWindowThreadProcessId(handle, out var processId);
            var processName = TryGetProcessName(processId);

            if (LooksLikePathOfExile(title, processName))
            {
                matches.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var handle in matches)
        {
            if (!GetWindowRect(handle, out var rect))
            {
                continue;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 1000 || bounds.Height < 700)
            {
                continue;
            }

            screen = Screen.FromRectangle(bounds);
            return true;
        }

        return false;
    }

    private static bool LooksLikePathOfExile(string title, string processName)
    {
        return title.Contains("Path of Exile", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void SaveBitmap(Bitmap bitmap, string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static async Task<string> EnsureTessDataAsync(string tessDataDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tessDataDirectory);
        var trainedDataPath = Path.Combine(tessDataDirectory, "eng.traineddata");
        if (File.Exists(trainedDataPath))
        {
            return tessDataDirectory;
        }

        using var client = new HttpClient();
        var bytes = await client.GetByteArrayAsync(
            "https://github.com/tesseract-ocr/tessdata_fast/raw/main/eng.traineddata",
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllBytesAsync(trainedDataPath, bytes, cancellationToken).ConfigureAwait(false);
        return tessDataDirectory;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
