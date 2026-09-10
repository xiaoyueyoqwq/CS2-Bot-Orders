namespace BotOrders;

/// <summary>
/// Lower number wins. Gaps of 10 are for Phase 2 inserts (Follow, NeedBackup, Mid, …).
/// </summary>
internal static class OrderRanks
{
    public const int RoundWipe = 0;
    public const int ExplicitSiteChange = 10;
    public const int PlantedOrBombPing = 20;
    public const int SiteClear = 30;
    public const int OpeningSplit = 40;
    public const int ContactStay = 50;
    public const int NoInject = 60;
}

internal enum Bombsite : byte
{
    Unknown = 0,
    A = 1,
    B = 2,
}

internal enum OrderKind : byte
{
    None = 0,
    GoSite,
    PlantedAt,
    PingBomb,
    SiteClear,
    DroppedBomb,
    PickedUpC4,
}

internal readonly record struct ParsedOrder(
    int Rank,
    OrderKind Kind,
    Bombsite Site,
    string Source);

internal static class OrderCommands
{
    public const float OpeningWindowSeconds = 10f;

    /// <summary>
    /// Radio Slot → meaning. Empty until a live dump fills it.
    /// Unknown slots are logged, never guessed.
    /// </summary>
    internal static readonly Dictionary<int, RadioMeaning> RadioSlots = new();

    internal static bool IsInOpeningWindow(float roundStartTime, float now)
    {
        return now - roundStartTime <= OpeningWindowSeconds;
    }

    internal static ParsedOrder RankGoSite(Bombsite site, bool inOpeningWindow, string source)
    {
        int rank = inOpeningWindow ? OrderRanks.OpeningSplit : OrderRanks.ExplicitSiteChange;
        return new ParsedOrder(rank, OrderKind.GoSite, site, source);
    }

    internal static bool TryMapRadioSlot(int slot, out RadioMeaning meaning)
    {
        return RadioSlots.TryGetValue(slot, out meaning);
    }

    internal static ParsedOrder? FromRadioMeaning(RadioMeaning meaning, bool inOpeningWindow)
    {
        return meaning switch
        {
            RadioMeaning.GoA => RankGoSite(Bombsite.A, inOpeningWindow, "radio:GoA"),
            RadioMeaning.GoB => RankGoSite(Bombsite.B, inOpeningWindow, "radio:GoB"),
            RadioMeaning.PlantedAtA => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PlantedAt, Bombsite.A, "radio:PlantedAtA"),
            RadioMeaning.PlantedAtB => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PlantedAt, Bombsite.B, "radio:PlantedAtB"),
            RadioMeaning.PlantingAtA => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PlantedAt, Bombsite.A, "radio:PlantingAtA"),
            RadioMeaning.PlantingAtB => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PlantedAt, Bombsite.B, "radio:PlantingAtB"),
            RadioMeaning.WePlanted => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PlantedAt, Bombsite.Unknown, "radio:wePlanted"),
            RadioMeaning.BombAt => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PingBomb, Bombsite.Unknown, "radio:BombAt"),
            RadioMeaning.BombsiteClear => new ParsedOrder(OrderRanks.SiteClear, OrderKind.SiteClear, Bombsite.Unknown, "radio:BombsiteClear"),
            RadioMeaning.SectorClear => new ParsedOrder(OrderRanks.SiteClear, OrderKind.SiteClear, Bombsite.Unknown, "radio:SectorClear"),
            RadioMeaning.DroppedBomb => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.DroppedBomb, Bombsite.Unknown, "radio:DroppedBomb"),
            RadioMeaning.PickedUpC4 => new ParsedOrder(OrderRanks.PlantedOrBombPing, OrderKind.PickedUpC4, Bombsite.Unknown, "radio:PickedUpC4"),
            RadioMeaning.IgnoredPhase2OrNever => null,
            _ => null,
        };
    }

    internal static Bombsite ParseSite(string? place)
    {
        if (string.IsNullOrWhiteSpace(place))
            return Bombsite.Unknown;

        string compact = place.Replace(" ", "", StringComparison.OrdinalIgnoreCase);

        if (compact.Contains("BombsiteA", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("BombA", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("SiteA", StringComparison.OrdinalIgnoreCase))
            return Bombsite.A;

        if (compact.Contains("BombsiteB", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("BombB", StringComparison.OrdinalIgnoreCase)
            || compact.Contains("SiteB", StringComparison.OrdinalIgnoreCase))
            return Bombsite.B;

        return Bombsite.Unknown;
    }

    internal static Bombsite Other(Bombsite site)
    {
        return site switch
        {
            Bombsite.A => Bombsite.B,
            Bombsite.B => Bombsite.A,
            _ => Bombsite.Unknown,
        };
    }

    internal static Bombsite FromPlantedSiteIndex(int site)
    {
        return site switch
        {
            0 => Bombsite.A,
            1 => Bombsite.B,
            _ => Bombsite.Unknown,
        };
    }

    internal static bool IsBombEntity(string? designerName)
    {
        if (string.IsNullOrEmpty(designerName))
            return false;

        return designerName.Equals("planted_c4", StringComparison.OrdinalIgnoreCase)
               || designerName.Equals("weapon_c4", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool Beats(int incomingRank, int currentRank)
    {
        return incomingRank <= currentRank;
    }
}

internal enum RadioMeaning : byte
{
    Unknown = 0,
    GoA,
    GoB,
    PlantedAtA,
    PlantedAtB,
    PlantingAtA,
    PlantingAtB,
    WePlanted,
    BombAt,
    BombsiteClear,
    SectorClear,
    DroppedBomb,
    PickedUpC4,
    IgnoredPhase2OrNever,
}

internal enum InjectKind : byte
{
    None = 0,
    RushSite,
    FakeRushSite,
    StackSite,
    CheckSite,
    AllGoSite,
    RetakeSite,
    StayFight,
    LeaveSite,
}
