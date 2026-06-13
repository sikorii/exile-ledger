namespace Poe2PriceChecker;

internal static class ScanValueRecalculator
{
    public static CurrencyScanResult Recalculate(CurrencyScanResult result, PoeNinjaPrices prices)
    {
        var stacks = new List<CurrencyStack>();
        var knownOccupied = 0;
        var unknownOccupied = 0;
        var slots = result.Slots
            .Select(slot =>
            {
                if (!slot.Occupied)
                {
                    return slot with { Exalts = null, Divines = null };
                }

                if (string.IsNullOrWhiteSpace(slot.ItemName))
                {
                    unknownOccupied++;
                    return slot with { Exalts = null, Divines = null };
                }

                knownOccupied++;
                var quantity = NormalizeQuantity(slot.Quantity);
                var value = prices.TryGetValue(slot.ItemName, quantity);
                if (value is null)
                {
                    unknownOccupied++;
                    return slot with { Quantity = quantity, Exalts = null, Divines = null };
                }

                stacks.Add(new CurrencyStack(slot.ItemName, quantity, value.Exalts, value.Divines));
                return slot with { Quantity = quantity, Exalts = value.Exalts, Divines = value.Divines };
            })
            .ToArray();

        return result with
        {
            TopStacks = stacks
                .OrderByDescending(stack => stack.Exalts)
                .Take(10)
                .ToArray(),
            TotalExalts = stacks.Sum(stack => stack.Exalts),
            TotalDivines = stacks.Sum(stack => stack.Divines),
            KnownOccupiedSlots = knownOccupied,
            UnknownOccupiedSlots = unknownOccupied,
            Slots = slots
        };
    }

    public static RuneScanResult Recalculate(
        RuneScanResult result,
        PoeNinjaPrices prices,
        Func<IReadOnlyList<RuneStack>, PoeNinjaPrices, IReadOnlyList<RuneUpgradeSuggestion>> buildUpgradeSuggestions)
    {
        var stacks = new List<RuneStack>();
        var knownOccupied = 0;
        var unknownOccupied = 0;
        var slots = result.Slots
            .Select(slot =>
            {
                if (!slot.Occupied)
                {
                    return slot with { Exalts = null, Divines = null };
                }

                if (string.IsNullOrWhiteSpace(slot.ItemName))
                {
                    unknownOccupied++;
                    return slot with { Exalts = null, Divines = null };
                }

                knownOccupied++;
                var quantity = NormalizeQuantity(slot.Quantity);
                var value = prices.TryGetValue(slot.ItemName, quantity);
                if (value is null)
                {
                    unknownOccupied++;
                    return slot with { Quantity = quantity, Exalts = null, Divines = null };
                }

                stacks.Add(new RuneStack(slot.ItemName, quantity, value.Exalts, value.Divines));
                return slot with { Quantity = quantity, Exalts = value.Exalts, Divines = value.Divines };
            })
            .ToArray();

        return result with
        {
            TopStacks = stacks
                .OrderByDescending(stack => stack.Exalts)
                .Take(10)
                .ToArray(),
            UpgradeSuggestions = buildUpgradeSuggestions(stacks, prices),
            TotalExalts = stacks.Sum(stack => stack.Exalts),
            TotalDivines = stacks.Sum(stack => stack.Divines),
            KnownOccupiedSlots = knownOccupied,
            UnknownOccupiedSlots = unknownOccupied,
            Slots = slots
        };
    }

    public static FixedStashScanResult Recalculate(FixedStashScanResult result, PoeNinjaPrices prices)
    {
        var stacks = new List<FixedStashStack>();
        var knownOccupied = 0;
        var unknownOccupied = 0;
        var slots = result.Slots
            .Select(slot =>
            {
                if (!slot.Occupied)
                {
                    return slot with { Exalts = null, Divines = null };
                }

                if (string.IsNullOrWhiteSpace(slot.ItemName))
                {
                    unknownOccupied++;
                    return slot with { Exalts = null, Divines = null };
                }

                knownOccupied++;
                var quantity = NormalizeQuantity(slot.Quantity);
                var value = prices.TryGetValue(slot.ItemName, quantity);
                if (value is null)
                {
                    unknownOccupied++;
                    return slot with { Quantity = quantity, Exalts = null, Divines = null };
                }

                stacks.Add(new FixedStashStack(slot.ItemName, quantity, value.Exalts, value.Divines));
                return slot with { Quantity = quantity, Exalts = value.Exalts, Divines = value.Divines };
            })
            .ToArray();

        return result with
        {
            TopStacks = stacks
                .OrderByDescending(stack => stack.Exalts)
                .Take(10)
                .ToArray(),
            TotalExalts = stacks.Sum(stack => stack.Exalts),
            TotalDivines = stacks.Sum(stack => stack.Divines),
            KnownOccupiedSlots = knownOccupied,
            UnknownOccupiedSlots = unknownOccupied,
            Slots = slots
        };
    }

    private static int NormalizeQuantity(int? quantity)
    {
        return quantity is > 0 ? quantity.Value : 1;
    }
}
