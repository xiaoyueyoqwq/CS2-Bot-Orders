namespace BotOrders;

/// <summary>
/// Lower number wins. Gaps of 10 are for Phase 2 inserts (Follow, NeedBackup, Mid, …).
/// </summary>
internal static class OrderRanks
{
    public const int RoundWipe = 0;
    public const int FollowMe = 10;
    public const int BombFact = 20;
    public const int SiteClear = 30;
    public const int SiteClaim = 40;
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
    public const float OpeningCollectSeconds = 15f;
    public const float TacticalSilenceSeconds = 5f;
    public const float ClaimCooldownSeconds = 0f;
    public const float MustRunSeconds = 8f;
    public const float MinDestAbs = 1f;
    public const float HuntRetryFirstSeconds = 0.15f;
    public const float HuntRetrySecondSeconds = 0.5f;
    public const int PingTypeLocation = 0;
    public const int PingTypeWarning = 13;
    public const float AttentionLookSeconds = 5f;
    public const float EnemyLookSeconds = 8f;
    public const float AlertNearbyUnits = 1500f;
    public const float LookAtAngleTolerance = 20f;

    internal static bool IsUsableDest(float x, float y)
    {
        return MathF.Abs(x) + MathF.Abs(y) >= MinDestAbs;
    }

    /// <summary>
    /// Radio Slot → meaning. Empty until a live dump fills it.
    /// Unknown slots are logged, never guessed.
    /// </summary>
    internal static readonly Dictionary<int, RadioMeaning> RadioSlots = new();

    /// <summary>
    /// Chatwheel token → meaning. Keys are CW.* names and #Chatwheel_*
    /// aliases after stripping the prefix. This is the client command
    /// string, not a guessed EventPlayerRadio slot.
    /// </summary>
    internal static readonly Dictionary<string, RadioMeaning> ChatwheelTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["GoA"] = RadioMeaning.GoA,
            ["aplan"] = RadioMeaning.GoA,
            ["GoB"] = RadioMeaning.GoB,
            ["bplan"] = RadioMeaning.GoB,
            ["GoToLocMid"] = RadioMeaning.IgnoredPhase2OrNever,
            ["midplan"] = RadioMeaning.IgnoredPhase2OrNever,
            ["BombsiteClear"] = RadioMeaning.BombsiteClear,
            ["SectorClear"] = RadioMeaning.SectorClear,
            ["PlantedAtA"] = RadioMeaning.PlantedAtA,
            ["plantedata"] = RadioMeaning.PlantedAtA,
            ["PlantedAtB"] = RadioMeaning.PlantedAtB,
            ["plantedatb"] = RadioMeaning.PlantedAtB,
            ["PlantingAtA"] = RadioMeaning.PlantingAtA,
            ["plantingata"] = RadioMeaning.PlantingAtA,
            ["PlantingAtB"] = RadioMeaning.PlantingAtB,
            ["plantingatb"] = RadioMeaning.PlantingAtB,
            ["wePlanted"] = RadioMeaning.WePlanted,
            ["planted"] = RadioMeaning.WePlanted,
            ["BombAt"] = RadioMeaning.BombAt,
            ["DroppedBomb"] = RadioMeaning.DroppedBomb,
            ["PickedUpC4"] = RadioMeaning.PickedUpC4,
            ["bombpickedup"] = RadioMeaning.PickedUpC4,
            ["WeHaveTheBomb"] = RadioMeaning.PickedUpC4,
            ["ihavethebomb"] = RadioMeaning.PickedUpC4,
        };

    internal static float Elapsed(float roundStartTime, float now)
    {
        return now - roundStartTime;
    }

    /// <summary>
    /// Collect lasts until freeze ends. OpeningCollectSeconds is only a
    /// cap if EventRoundFreezeEnd never arrives.
    /// </summary>
    internal static bool IsInOpeningCollect(float roundStartTime, float now, bool freezeEnded)
    {
        if (freezeEnded)
            return false;
        return Elapsed(roundStartTime, now) <= OpeningCollectSeconds;
    }

    /// <summary>
    /// Cheap-order silence is 5s after freeze end, not after round_start.
    /// </summary>
    internal static bool IsInTacticalSilence(float freezeEndTime, float now, bool freezeEnded)
    {
        if (!freezeEnded)
            return false;
        float since = now - freezeEndTime;
        return since >= 0f && since <= TacticalSilenceSeconds;
    }

    internal static ParsedOrder RankGoSite(Bombsite site, string source)
    {
        return new ParsedOrder(OrderRanks.SiteClaim, OrderKind.GoSite, site, source);
    }

    internal static bool TryMapRadioSlot(int slot, out RadioMeaning meaning)
    {
        return RadioSlots.TryGetValue(slot, out meaning);
    }

    internal static bool TryMapChatwheel(string? rawToken, out RadioMeaning meaning)
    {
        meaning = RadioMeaning.Unknown;
        string token = NormalizeChatwheelToken(rawToken);
        if (token.Length == 0)
            return false;
        return ChatwheelTokens.TryGetValue(token, out meaning);
    }

    internal static string NormalizeChatwheelToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        string token = raw.Trim().Trim('"');
        const string cw = "CW.";
        if (token.StartsWith(cw, StringComparison.OrdinalIgnoreCase))
            token = token[cw.Length..];
        const string hash = "#Chatwheel_";
        if (token.StartsWith(hash, StringComparison.OrdinalIgnoreCase))
            token = token[hash.Length..];
        return token;
    }

    internal static ParsedOrder? FromRadioMeaning(RadioMeaning meaning, string sourcePrefix)
    {
        return meaning switch
        {
            RadioMeaning.GoA => RankGoSite(Bombsite.A, sourcePrefix + ":GoA"),
            RadioMeaning.GoB => RankGoSite(Bombsite.B, sourcePrefix + ":GoB"),
            RadioMeaning.BombsiteClear => new ParsedOrder(OrderRanks.SiteClear, OrderKind.SiteClear, Bombsite.Unknown, sourcePrefix + ":BombsiteClear"),
            RadioMeaning.SectorClear => new ParsedOrder(OrderRanks.SiteClear, OrderKind.SiteClear, Bombsite.Unknown, sourcePrefix + ":SectorClear"),
            RadioMeaning.PlantedAtA => null,
            RadioMeaning.PlantedAtB => null,
            RadioMeaning.PlantingAtA => null,
            RadioMeaning.PlantingAtB => null,
            RadioMeaning.WePlanted => null,
            RadioMeaning.BombAt => null,
            RadioMeaning.DroppedBomb => null,
            RadioMeaning.PickedUpC4 => null,
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
    AllGoSite,
    RetakeSite,
    HuntDropped,
    LeaveSite,
}
