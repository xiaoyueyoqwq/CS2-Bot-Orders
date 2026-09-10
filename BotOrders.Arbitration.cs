namespace BotOrders;

internal readonly record struct SiteBallot(ulong SteamId, Bombsite Site);

internal enum BombKnowledge : byte
{
    None = 0,
    Planted,
    DroppedEntity,
    PingedEntity,
}

internal readonly record struct BombFact(BombKnowledge Kind, Bombsite Site)
{
    public static BombFact None => new(BombKnowledge.None, Bombsite.Unknown);
}

internal enum ResolutionAction : byte
{
    NoInject = 0,
    AllGo,
    KeepPrevious,
    HuntDropped,
    LeaveSite,
}

internal readonly record struct Resolution(
    int Rank,
    OrderKind Kind,
    Bombsite Primary,
    ResolutionAction Action,
    bool AssignAllBots,
    string Reason);

/// <summary>
/// Pure claim/fact resolver. No CSS types. Lower rank number wins at the
/// plugin layer; this function returns the single winning resolution.
/// </summary>
internal static class OrderArbitration
{
    internal static Resolution Resolve(
        IReadOnlyList<SiteBallot> liveBallots,
        BombFact bomb,
        bool teamIsTerrorist,
        ulong? humanC4HolderSteamId,
        Bombsite previousPrimary,
        bool hadAssignment,
        Bombsite clearSite)
    {
        if (bomb.Kind == BombKnowledge.Planted && bomb.Site != Bombsite.Unknown)
        {
            return AllGo(
                OrderRanks.BombFact,
                OrderKind.PlantedAt,
                bomb.Site,
                "bomb-planted");
        }

        if (bomb.Kind == BombKnowledge.Planted)
        {
            return new Resolution(
                OrderRanks.BombFact,
                OrderKind.PlantedAt,
                Bombsite.Unknown,
                ResolutionAction.HuntDropped,
                AssignAllBots: true,
                Reason: "bomb-planted-hunt");
        }

        if (bomb.Kind == BombKnowledge.PingedEntity && bomb.Site != Bombsite.Unknown)
        {
            return AllGo(
                OrderRanks.BombFact,
                OrderKind.PingBomb,
                bomb.Site,
                "ping-bomb-entity");
        }

        if (bomb.Kind == BombKnowledge.PingedEntity)
        {
            return new Resolution(
                OrderRanks.BombFact,
                OrderKind.PingBomb,
                Bombsite.Unknown,
                ResolutionAction.HuntDropped,
                AssignAllBots: true,
                Reason: "ping-bomb-hunt");
        }

        if (bomb.Kind == BombKnowledge.DroppedEntity && !teamIsTerrorist)
        {
            return new Resolution(
                OrderRanks.BombFact,
                OrderKind.DroppedBomb,
                Bombsite.Unknown,
                ResolutionAction.HuntDropped,
                AssignAllBots: true,
                Reason: "ct-dropped-hunt");
        }

        if (clearSite != Bombsite.Unknown
            && bomb.Kind != BombKnowledge.Planted
            && bomb.Site != clearSite)
        {
            return new Resolution(
                OrderRanks.SiteClear,
                OrderKind.SiteClear,
                OrderCommands.Other(clearSite),
                ResolutionAction.LeaveSite,
                AssignAllBots: true,
                Reason: "verified-clear");
        }

        int votesA = 0;
        int votesB = 0;
        Bombsite? c4HolderSite = null;
        foreach (var ballot in liveBallots)
        {
            if (ballot.Site == Bombsite.A)
                votesA++;
            else if (ballot.Site == Bombsite.B)
                votesB++;
            else
                continue;

            if (humanC4HolderSteamId is ulong holder && ballot.SteamId == holder)
                c4HolderSite = ballot.Site;
        }

        if (votesA == 0 && votesB == 0)
            return None("no-ballots");

        if (votesB == 0)
            return AllGo(OrderRanks.SiteClaim, OrderKind.GoSite, Bombsite.A, "uncontested-A");
        if (votesA == 0)
            return AllGo(OrderRanks.SiteClaim, OrderKind.GoSite, Bombsite.B, "uncontested-B");

        if (votesA != votesB)
        {
            Bombsite majority = votesA > votesB ? Bombsite.A : Bombsite.B;
            return AllGo(
                OrderRanks.SiteClaim,
                OrderKind.GoSite,
                majority,
                votesA > votesB ? "majority-A" : "majority-B");
        }

        if (teamIsTerrorist && c4HolderSite is Bombsite c4Site && c4Site != Bombsite.Unknown)
        {
            return AllGo(
                OrderRanks.SiteClaim,
                OrderKind.GoSite,
                c4Site,
                "tie-c4-holder");
        }

        if (hadAssignment && previousPrimary is Bombsite.A or Bombsite.B)
        {
            return new Resolution(
                OrderRanks.SiteClaim,
                OrderKind.GoSite,
                previousPrimary,
                ResolutionAction.KeepPrevious,
                AssignAllBots: true,
                Reason: "tie-keep-previous");
        }

        return None("tie-no-inject");
    }

    private static Resolution AllGo(int rank, OrderKind kind, Bombsite site, string reason)
    {
        return new Resolution(
            rank,
            kind,
            site,
            ResolutionAction.AllGo,
            AssignAllBots: true,
            Reason: reason);
    }

    private static Resolution None(string reason)
    {
        return new Resolution(
            OrderRanks.NoInject,
            OrderKind.None,
            Bombsite.Unknown,
            ResolutionAction.NoInject,
            AssignAllBots: false,
            Reason: reason);
    }

    /// <summary>
    /// Settlement table from the 2026-09-10 plan. Returns failing case names.
    /// </summary>
    internal static IReadOnlyList<string> SelfCheck()
    {
        var fail = new List<string>();

        void Case(
            string name,
            Resolution got,
            ResolutionAction action,
            Bombsite site,
            int rank,
            bool assignAll)
        {
            if (got.Action != action
                || got.Primary != site
                || got.Rank != rank
                || got.AssignAllBots != assignAll)
            {
                fail.Add(
                    $"{name}: got action={got.Action} primary={got.Primary} rank={got.Rank} assign={got.AssignAllBots} reason={got.Reason}");
            }
        }

        var none = Array.Empty<SiteBallot>();
        var oneA = new[] { new SiteBallot(1, Bombsite.A) };
        var twoA = new[] { new SiteBallot(1, Bombsite.A), new SiteBallot(2, Bombsite.A) };
        var oneEach = new[] { new SiteBallot(1, Bombsite.A), new SiteBallot(2, Bombsite.B) };
        var twoAoneB = new[]
        {
            new SiteBallot(1, Bombsite.A),
            new SiteBallot(2, Bombsite.A),
            new SiteBallot(3, Bombsite.B),
        };

        Case(
            "planted-B",
            Resolve(none, new BombFact(BombKnowledge.Planted, Bombsite.B), true, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.B, OrderRanks.BombFact, true);

        Case(
            "one-human-A",
            Resolve(oneA, BombFact.None, true, 1, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "two-agree-A",
            Resolve(twoA, BombFact.None, true, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "tie-T-c4-holder-A",
            Resolve(oneEach, BombFact.None, true, 1, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "tie-CT-no-previous",
            Resolve(oneEach, BombFact.None, false, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.NoInject, Bombsite.Unknown, OrderRanks.NoInject, false);

        Case(
            "tie-CT-keep-A",
            Resolve(oneEach, BombFact.None, false, null, Bombsite.A, true, Bombsite.Unknown),
            ResolutionAction.KeepPrevious, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "majority-2A-1B",
            Resolve(twoAoneB, BombFact.None, false, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "T-dropped-no-rally-falls-to-uncontested",
            Resolve(oneA, new BombFact(BombKnowledge.DroppedEntity, Bombsite.Unknown), true, 1, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.A, OrderRanks.SiteClaim, true);

        Case(
            "T-dropped-no-ballots",
            Resolve(none, new BombFact(BombKnowledge.DroppedEntity, Bombsite.Unknown), true, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.NoInject, Bombsite.Unknown, OrderRanks.NoInject, false);

        Case(
            "CT-dropped-hunt",
            Resolve(oneEach, new BombFact(BombKnowledge.DroppedEntity, Bombsite.Unknown), false, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.HuntDropped, Bombsite.Unknown, OrderRanks.BombFact, true);

        Case(
            "ping-bomb-B",
            Resolve(oneEach, new BombFact(BombKnowledge.PingedEntity, Bombsite.B), false, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.AllGo, Bombsite.B, OrderRanks.BombFact, true);

        Case(
            "ping-bomb-no-site-hunts",
            Resolve(oneA, new BombFact(BombKnowledge.PingedEntity, Bombsite.Unknown), true, 1, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.HuntDropped, Bombsite.Unknown, OrderRanks.BombFact, true);

        Case(
            "planted-no-site-hunts",
            Resolve(none, new BombFact(BombKnowledge.Planted, Bombsite.Unknown), false, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.HuntDropped, Bombsite.Unknown, OrderRanks.BombFact, true);

        Case(
            "no-ballots",
            Resolve(none, BombFact.None, true, null, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.NoInject, Bombsite.Unknown, OrderRanks.NoInject, false);

        Case(
            "tie-T-c4-holder-silent",
            Resolve(oneEach, BombFact.None, true, 99, Bombsite.Unknown, false, Bombsite.Unknown),
            ResolutionAction.NoInject, Bombsite.Unknown, OrderRanks.NoInject, false);

        Case(
            "verified-clear-B",
            Resolve(none, BombFact.None, false, null, Bombsite.Unknown, false, Bombsite.B),
            ResolutionAction.LeaveSite, Bombsite.A, OrderRanks.SiteClear, true);

        return fail;
    }
}
