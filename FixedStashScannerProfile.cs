namespace Poe2PriceChecker;

internal sealed record FixedStashSlot(
    Rectangle Bounds,
    string? ItemName = null,
    Rectangle? OverlayBounds = null,
    string? Section = null,
    FixedStashSlotIdentity? StaticIdentity = null)
{
    public Rectangle GetOverlayBounds(int defaultInset = 0)
    {
        return OverlayBounds ?? Inset(Bounds, defaultInset);
    }

    public static Rectangle Inset(Rectangle rectangle, int inset)
    {
        if (inset <= 0)
        {
            return rectangle;
        }

        var width = Math.Max(1, rectangle.Width - inset * 2);
        var height = Math.Max(1, rectangle.Height - inset * 2);
        return new Rectangle(rectangle.X + inset, rectangle.Y + inset, width, height);
    }
}

internal sealed record FixedStashSlotIdentity(
    string? CanonicalItemName,
    string? EssenceFamily,
    string? EssenceTier,
    string? PoeNinjaLookupKey,
    int? GroupIndex,
    int? Row,
    int? Column);

internal sealed record FixedStashScannerProfile(
    string Key,
    string Label,
    string MappingFileName,
    string CountOverrideFileName,
    string IconCategory,
    string CountMode,
    bool IsRuneLike,
    bool HasUpgradeSummary,
    IReadOnlyList<FixedStashSlot> Slots,
    IReadOnlySet<string> PriceCategories,
    IReadOnlySet<string> IconCategories,
    bool DefaultInsideFolder = true)
{
    public FixedTabLayoutDescriptor Layout => FixedTabLayoutDescriptor.FromSlots(Key, Slots);
}

internal static class FixedStashScannerProfiles
{
    public const int DefaultStaticOverlayInset = 3;
    public const int AugmentRuneOverlayInset = 1;
    public const int KalguuranRuneOverlayInset = 3;

    public static readonly FixedStashScannerProfile Currency = new(
        "CurrencyStash",
        "Currency Stash",
        "currency-mappings.json",
        "currency-count-overrides.json",
        "Currency",
        "currency",
        IsRuneLike: false,
        HasUpgradeSummary: false,
        Slots: CurrencySlotMap.Slots.Select(slot => SlotWithOverlay(slot.Bounds, slot.ItemName)).ToArray(),
        PriceCategories: Set("Currency"),
        IconCategories: Set("Currency"),
        DefaultInsideFolder: false);

    public static readonly FixedStashScannerProfile AugmentRunes = new(
        "AugmentRunes",
        "Augment: Runes",
        "rune-mappings.json",
        "rune-count-overrides.json",
        "Runes",
        "runes",
        IsRuneLike: true,
        HasUpgradeSummary: true,
        Slots: RuneSlotMap.Slots.Select(slot => SlotWithOverlay(slot.Bounds, slot.ItemName, AugmentRuneOverlayInset)).ToArray(),
        PriceCategories: Set("Runes"),
        IconCategories: Set("Runes"));

    public static readonly FixedStashScannerProfile KalguuranRunes = new(
        "AugmentKalguuranRunes",
        "Augment: Kalguuran Runes",
        "kalguuran-rune-mappings.json",
        "kalguuran-rune-count-overrides.json",
        "Runes",
        "kalguuran-runes",
        IsRuneLike: true,
        HasUpgradeSummary: false,
        Slots: KalguuranRuneSlotMap.Slots.Select(slot => SlotWithOverlay(slot.Bounds, slot.ItemName, KalguuranRuneOverlayInset)).ToArray(),
        PriceCategories: Set("Runes"),
        IconCategories: Set("Runes"));

    public static readonly FixedStashScannerProfile Abyss = Generic(
        "Abyss",
        "Abyss Stash",
        "abyss",
        "Abyss",
        FixedStashSlotMaps.Abyss);

    public static readonly FixedStashScannerProfile Delirium = Generic(
        "Delirium",
        "Delirium Stash",
        "delirium",
        "Delirium",
        FixedStashSlotMaps.Delirium);

    public static readonly FixedStashScannerProfile Expedition = new(
        "Expedition",
        "Expedition Stash",
        "expedition-mappings.json",
        "expedition-count-overrides.json",
        "Expedition",
        "expedition",
        IsRuneLike: false,
        HasUpgradeSummary: false,
        Slots: NormalizeOverlaySlots(FixedStashSlotMaps.Expedition),
        PriceCategories: Set("Expedition", "Verisium"),
        IconCategories: Set("Expedition", "Verisium"));

    public static readonly FixedStashScannerProfile Ritual = Generic(
        "Ritual",
        "Ritual Stash",
        "ritual",
        "Ritual",
        FixedStashSlotMaps.Ritual);

    public static readonly FixedStashScannerProfile BreachCatalysts = Generic(
        "BreachCatalysts",
        "Breach Catalysts",
        "breach-catalysts",
        "Breach",
        FixedStashSlotMaps.BreachCatalysts);

    public static readonly FixedStashScannerProfile Fragments = Generic(
        "Fragments",
        "Fragments",
        "fragments",
        "Fragments",
        FixedStashSlotMaps.Fragments);

    public static readonly FixedStashScannerProfile SoulCores = Generic(
        "AugmentSoulCores",
        "Augment: Soul Cores",
        "soul-cores",
        "SoulCores",
        FixedStashSlotMaps.SoulCores,
        isRuneLike: true);

    public static readonly FixedStashScannerProfile Idols = Generic(
        "AugmentIdols",
        "Augment: Idols",
        "idols",
        "Idols",
        FixedStashSlotMaps.Idols,
        isRuneLike: true);

    public static readonly FixedStashScannerProfile AncientAugments = Generic(
        "AugmentAncientAugments",
        "Augment: Ancient Augments",
        "ancient-augments",
        "Verisium",
        FixedStashSlotMaps.AncientAugments,
        isRuneLike: true);

    public static readonly FixedStashScannerProfile Essence = Generic(
        "Essence",
        "Essence Stash",
        "essence",
        "Essences",
        FixedStashSlotMaps.Essence,
        isRuneLike: true);

    public static readonly IReadOnlyList<FixedStashScannerProfile> BuiltIn =
    [
        Currency,
        AugmentRunes,
        KalguuranRunes,
        Abyss,
        Delirium,
        Expedition,
        Ritual,
        BreachCatalysts,
        Fragments,
        SoulCores,
        Idols,
        AncientAugments,
        Essence
    ];

    public static string ConfigPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "config", fileName);
    }

    private static FixedStashScannerProfile Generic(
        string key,
        string label,
        string filePrefix,
        string category,
        IReadOnlyList<FixedStashSlot> slots,
        bool isRuneLike = false)
    {
        return new FixedStashScannerProfile(
            key,
            label,
            $"{filePrefix}-mappings.json",
            $"{filePrefix}-count-overrides.json",
            category,
            filePrefix,
            isRuneLike,
            HasUpgradeSummary: false,
            NormalizeOverlaySlots(slots),
            PriceCategories: Set(category),
            IconCategories: Set(category));
    }

    private static FixedStashSlot SlotWithOverlay(Rectangle bounds, string? itemName = null, int overlayInset = DefaultStaticOverlayInset)
    {
        return new FixedStashSlot(bounds, itemName, FixedStashSlot.Inset(bounds, overlayInset));
    }

    private static IReadOnlyList<FixedStashSlot> NormalizeOverlaySlots(IReadOnlyList<FixedStashSlot> slots)
    {
        return slots
            .Select(slot => slot.OverlayBounds is null
                ? slot with { OverlayBounds = FixedStashSlot.Inset(slot.Bounds, DefaultStaticOverlayInset) }
                : slot)
            .ToArray();
    }

    private static IReadOnlySet<string> Set(params string[] values)
    {
        return new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
    }
}

internal static class FixedStashSlotMaps
{
    public static readonly IReadOnlyList<FixedStashSlot> Abyss =
    [
        Slot(603, 430), Slot(603, 635),
        Slot(470, 765), Slot(603, 765), Slot(736, 765),
        Slot(337, 895), Slot(470, 895), Slot(603, 895), Slot(736, 895),
        Slot(470, 1025), Slot(603, 1025), Slot(736, 1025),
        Slot(603, 1160),
        .. Row(140, 1358, 8, 132)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> Delirium =
    [
        Slot(529, 431, 122), Slot(690, 429, 125), Slot(606, 564, 126),
        .. Row(160, 712, 3, 135, 126),
        Slot(783, 714, 122), Slot(918, 714, 122), Slot(1052, 712, 125),
        Slot(608, 789, 121),
        Slot(229, 849, 123), Slot(364, 849, 123), Slot(849, 847, 126), Slot(984, 847, 126),
        Slot(608, 924, 121),
        .. Row(163, 1019, 3),
        Slot(783, 1018, 122), Slot(918, 1018, 122), Slot(1052, 1016, 125),
        Slot(608, 1059, 121),
        Slot(229, 1153, 123), Slot(364, 1153, 123), Slot(849, 1151, 126), Slot(984, 1151, 126),
        Slot(188, 1326, 125), Slot(327, 1326, 125), Slot(466, 1326, 125),
        Slot(740, 1326, 125), Slot(879, 1326, 125), Slot(1018, 1326, 125)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> Expedition =
    [
        .. Row(270, 421, 6, 134, 120),
        Slot(428, 613, 241, 235), Slot(672, 613, 241, 235),
        Slot(540, 868), Slot(675, 868),
        Slot(401, 1000), Slot(535, 1000), Slot(674, 1000), Slot(809, 1000),
        .. Row(210, 1150, 7, 135),
        .. Row(270, 1285, 6, 135),
        .. Row(402, 1432, 4, 135, 126)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> Ritual =
    [
        Slot(405, 370), Slot(555, 370, 220, 110), Slot(805, 370),
        Slot(405, 565), Slot(685, 565), Slot(815, 565),
        Slot(205, 740), Slot(335, 740), Slot(465, 740), Slot(630, 740), Slot(760, 740), Slot(890, 740),
        Slot(270, 870), Slot(400, 870), Slot(630, 870), Slot(760, 870), Slot(890, 870), Slot(1020, 870),
        Slot(245, 1045), Slot(375, 1045), Slot(535, 1045), Slot(665, 1045), Slot(835, 1045), Slot(965, 1045),
        Slot(245, 1220), Slot(375, 1220),
        .. Row(215, 1415, 3),
        .. Row(650, 1415, 4)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> BreachCatalysts =
    [
        Slot(537, 519, 126, 126), Slot(672, 519, 126, 126),
        Slot(552, 654, 231, 232),
        .. Row(270, 938, 6),
        .. Row(270, 1073, 7),
        .. Row(270, 1248, 6),
        .. Row(270, 1383, 7)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> Fragments =
    [
        // Fragment tab only; tablets and trials sections are intentionally omitted.
        Slot(490, 535), Slot(610, 535), Slot(730, 535),
        Slot(165, 715), Slot(295, 715), Slot(610, 715), Slot(935, 715),
        Slot(165, 850, 220, 220), Slot(610, 850, 220, 220), Slot(935, 850, 220, 220),
        Slot(110, 1150), Slot(240, 1150, 220, 120), Slot(485, 1150), Slot(665, 1150), Slot(795, 1150), Slot(925, 1150), Slot(1085, 1150),
        Slot(490, 1355), Slot(610, 1355), Slot(730, 1355)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> SoulCores =
    [
        .. Row(140, 700, 8),
        .. Row(205, 835, 7),
        .. Row(140, 1064, 8),
        .. Row(205, 1199, 7)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> Idols =
    [
        .. Row(140, 510, 8),
        .. Row(270, 674, 6),
        .. Row(202, 904, 7),
        .. Row(200, 1130, 7),
        .. Row(475, 1385, 3)
    ];

    public static readonly IReadOnlyList<FixedStashSlot> AncientAugments =
    [
        .. Row(399, 491, 4),
        .. Row(405, 720, 4),
        Slot(605, 950),
        .. Row(405, 1199, 4),
        .. Row(405, 1430, 4)
    ];

    public static readonly FixedTabLayoutDescriptor EssenceLayout = BuildEssenceLayout();

    public static readonly IReadOnlyList<FixedStashSlot> Essence = EssenceLayout.ToFixedSlots();

    private static FixedTabLayoutDescriptor BuildEssenceLayout()
    {
        var slots = new List<StashSlotRect>();
        var sideRowY = new[] { 354, 476, 597, 719, 840, 962, 1083, 1205, 1326, 1448 };
        var groupIndex = 0;

        for (var row = 0; row < sideRowY.Length - 1; row++)
        {
            AddEssenceTierGroup(slots, groupIndex++, row, $"left-row-{row}", 83, sideRowY[row]);
            AddEssenceTierGroup(slots, groupIndex++, row, $"right-row-{row}", 804, sideRowY[row]);
        }

        AddEssenceTierGroup(slots, groupIndex++, 9, "left-row-9", 83, sideRowY[9]);
        AddEssencePartialGroup(slots, groupIndex++, 7, "center-bottom-0", 561, 1208);
        AddEssencePartialGroup(slots, groupIndex++, 8, "center-bottom-1", 561, 1329);
        AddEssencePartialGroup(slots, groupIndex++, 9, "center-bottom-2", 561, 1451);

        return new FixedTabLayoutDescriptor("Essence", slots, DefaultOverlayInset: 3);
    }

    private static void AddEssenceTierGroup(List<StashSlotRect> slots, int groupIndex, int row, string section, int x, int y)
    {
        for (var column = 0; column < EssenceStaticIdentity.TierColumns.Count; column++)
        {
            var bounds = new Rectangle(x + column * 111, y, 115, 116);
            var tier = EssenceStaticIdentity.TierColumns[column];
            slots.Add(new StashSlotRect(
                slots.Count,
                bounds,
                FixedStashSlot.Inset(bounds, 3),
                Section: section,
                StaticIdentity: new FixedStashSlotIdentity(
                    CanonicalItemName: null,
                    EssenceFamily: null,
                    EssenceTier: tier,
                    PoeNinjaLookupKey: null,
                    GroupIndex: groupIndex,
                    Row: row,
                    Column: column)));
        }
    }

    private static void AddEssencePartialGroup(List<StashSlotRect> slots, int groupIndex, int row, string section, int x, int y)
    {
        for (var column = 0; column < 2; column++)
        {
            var bounds = new Rectangle(x + column * 108, y, 106, 106);
            slots.Add(new StashSlotRect(
                slots.Count,
                bounds,
                FixedStashSlot.Inset(bounds, 3),
                Section: section,
                StaticIdentity: new FixedStashSlotIdentity(
                    CanonicalItemName: null,
                    EssenceFamily: null,
                    EssenceTier: null,
                    PoeNinjaLookupKey: null,
                    GroupIndex: groupIndex,
                    Row: row,
                    Column: column)));
        }
    }

    private static FixedStashSlot Slot(int x, int y, int size = 120)
    {
        return new FixedStashSlot(new Rectangle(x, y, size, size));
    }

    private static FixedStashSlot Slot(int x, int y, int width, int height)
    {
        return new FixedStashSlot(new Rectangle(x, y, width, height));
    }

    private static FixedStashSlot[] Row(int x, int y, int count, int step = 135, int size = 120)
    {
        return Enumerable.Range(0, count)
            .Select(index => Slot(x + index * step, y, size))
            .ToArray();
    }
}
