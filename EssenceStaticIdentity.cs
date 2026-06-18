using System.Text.RegularExpressions;

namespace Poe2PriceChecker;

internal static class EssenceStaticIdentity
{
    public static readonly IReadOnlyList<string> TierColumns = ["Lesser", "Normal", "Greater", "Perfect"];

    public static bool IsEssenceProfile(FixedStashScannerProfile profile)
    {
        return profile.Key.Equals(FixedStashScannerProfiles.Essence.Key, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string?> ResolveSlotNames(IReadOnlyList<FixedStashSlot> slots, CurrencyMappingStore mappingStore)
    {
        var names = new string?[slots.Count];
        var groups = slots
            .Select((slot, index) => new { Slot = slot, Index = index })
            .Where(entry => entry.Slot.StaticIdentity?.GroupIndex is not null)
            .GroupBy(entry => entry.Slot.StaticIdentity!.GroupIndex!.Value)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            var family = InferFamily(group.Select(entry => (entry.Index, entry.Slot)), mappingStore);
            foreach (var entry in group)
            {
                var identity = entry.Slot.StaticIdentity;
                if (identity is null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(family) &&
                    identity.Column is >= 0 and < 4 &&
                    !string.IsNullOrWhiteSpace(identity.EssenceTier))
                {
                    names[entry.Index] = BuildName(family, identity.Column.Value);
                    continue;
                }

                names[entry.Index] = identity.CanonicalItemName ?? entry.Slot.ItemName;
            }
        }

        return names;
    }

    public static int ApplyGroupMapping(
        IReadOnlyList<FixedStashSlot> slots,
        CurrencyMappingStore mappingStore,
        int slotIndex,
        string itemName)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count ||
            !TryParseName(itemName, out _, out var family))
        {
            return 0;
        }

        var groupIndex = slots[slotIndex].StaticIdentity?.GroupIndex;
        if (groupIndex is null)
        {
            return 0;
        }

        var updated = 0;
        for (var index = 0; index < slots.Count; index++)
        {
            var identity = slots[index].StaticIdentity;
            if (identity?.GroupIndex != groupIndex ||
                identity.Column is null ||
                identity.Column.Value < 0 ||
                identity.Column.Value >= 4 ||
                string.IsNullOrWhiteSpace(identity.EssenceTier))
            {
                continue;
            }

            mappingStore.SetName(index, BuildName(family, identity.Column.Value));
            updated++;
        }

        return updated;
    }

    public static IReadOnlyList<string> BuildCompletenessReport(
        FixedStashScannerProfile profile,
        CurrencyMappingStore mappingStore,
        LiveMarketPrices prices)
    {
        var resolvedNames = ResolveSlotNames(profile.Slots, mappingStore);
        var lines = new List<string>
        {
            "Essence Static Profile Completeness",
            $"Profile: {profile.Label}",
            $"Slots: {profile.Slots.Count}",
            "Rule: column 0 Lesser, column 1 Normal, column 2 Greater, column 3 Perfect.",
            string.Empty
        };

        var groups = profile.Slots
            .Select((slot, index) => new { Slot = slot, Index = index })
            .Where(entry => entry.Slot.StaticIdentity?.GroupIndex is not null)
            .GroupBy(entry => entry.Slot.StaticIdentity!.GroupIndex!.Value)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            var inferredFamily = InferFamily(group.Select(entry => (entry.Index, entry.Slot)), mappingStore);
            var observations = BuildFamilyObservations(group.Select(entry => (entry.Index, entry.Slot)), mappingStore).ToArray();
            var conflicts = observations
                .Select(observation => observation.Family)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Skip(1)
                .Any();

            lines.Add($"Group {group.Key}: inferred family = {inferredFamily ?? "(missing)"}{(conflicts ? " CONFLICT" : string.Empty)}");
            foreach (var entry in group.OrderBy(entry => entry.Slot.StaticIdentity?.Column ?? 0))
            {
                var identity = entry.Slot.StaticIdentity!;
                var expectedName = resolvedNames[entry.Index];
                var hasTier = !string.IsNullOrWhiteSpace(identity.EssenceTier);
                var missing = string.IsNullOrWhiteSpace(expectedName);
                var priceOk = !missing && prices.TryGetValue(expectedName!, 1) is not null;
                var customName = mappingStore.GetName(entry.Index, null);

                lines.Add(
                    $"  slot {entry.Index,2} row {identity.Row?.ToString() ?? "?"} col {identity.Column?.ToString() ?? "?"} " +
                    $"section {entry.Slot.Section ?? ""} tier {(hasTier ? identity.EssenceTier : "(unknown)")} " +
                    $"name {(expectedName ?? "(missing)")} missing={missing} price={priceOk}" +
                    (string.IsNullOrWhiteSpace(customName) ? string.Empty : $" mapped=\"{customName}\""));
            }

            lines.Add(string.Empty);
        }

        return lines;
    }

    public static string BuildName(string family, int column)
    {
        var cleanFamily = family.Trim();
        return column switch
        {
            0 => $"Lesser Essence of {cleanFamily}",
            1 => $"Essence of {cleanFamily}",
            2 => $"Greater Essence of {cleanFamily}",
            3 => $"Perfect Essence of {cleanFamily}",
            _ => $"Essence of {cleanFamily}"
        };
    }

    public static bool TryParseName(string itemName, out string tier, out string family)
    {
        tier = string.Empty;
        family = string.Empty;
        var match = Regex.Match(
            itemName.Trim(),
            @"^(?:(Lesser|Greater|Perfect)\s+)?Essence\s+of\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        tier = match.Groups[1].Success
            ? NormalizeTier(match.Groups[1].Value)
            : "Normal";
        family = match.Groups[2].Value.Trim();
        return family.Length > 0;
    }

    private static string? InferFamily(IEnumerable<(int Index, FixedStashSlot Slot)> group, CurrencyMappingStore mappingStore)
    {
        var observations = BuildFamilyObservations(group, mappingStore)
            .GroupBy(observation => observation.Family, StringComparer.OrdinalIgnoreCase)
            .Select(familyGroup => new
            {
                Family = familyGroup.First().Family,
                Count = familyGroup.Count(),
                FirstColumn = familyGroup.Min(observation => observation.Column ?? int.MaxValue)
            })
            .OrderByDescending(observation => observation.Count)
            .ThenBy(observation => observation.FirstColumn)
            .FirstOrDefault();

        return observations?.Family;
    }

    private static IEnumerable<FamilyObservation> BuildFamilyObservations(
        IEnumerable<(int Index, FixedStashSlot Slot)> group,
        CurrencyMappingStore mappingStore)
    {
        foreach (var (index, slot) in group)
        {
            var mapped = mappingStore.GetName(index, null);
            if (!string.IsNullOrWhiteSpace(mapped) &&
                TryParseName(mapped, out _, out var mappedFamily))
            {
                yield return new FamilyObservation(mappedFamily, slot.StaticIdentity?.Column);
            }

            var identity = slot.StaticIdentity;
            if (!string.IsNullOrWhiteSpace(identity?.EssenceFamily))
            {
                yield return new FamilyObservation(identity.EssenceFamily, identity.Column);
            }

            var defaultName = identity?.CanonicalItemName ?? slot.ItemName;
            if (!string.IsNullOrWhiteSpace(defaultName) &&
                TryParseName(defaultName, out _, out var defaultFamily))
            {
                yield return new FamilyObservation(defaultFamily, identity?.Column);
            }
        }
    }

    private static string NormalizeTier(string tier)
    {
        return tier.Trim().ToUpperInvariant() switch
        {
            "LESSER" => "Lesser",
            "GREATER" => "Greater",
            "PERFECT" => "Perfect",
            _ => "Normal"
        };
    }

    private sealed record FamilyObservation(string Family, int? Column);
}
