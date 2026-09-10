using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal sealed class RoundTeamState
{
    public CsTeam Team { get; }
    public int Rank { get; private set; } = OrderRanks.NoInject;
    public OrderKind Kind { get; private set; } = OrderKind.None;
    public Bombsite PrimarySite { get; private set; } = Bombsite.Unknown;
    public Bombsite OtherSite { get; private set; } = Bombsite.Unknown;
    public string Source { get; private set; } = "";
    public RosterAssignment? Roster { get; private set; }
    public HashSet<int> ContactSlots { get; } = new();
    public HashSet<int> FakeSlots { get; } = new();
    public HashSet<int> PrimarySlots { get; } = new();
    public Dictionary<int, float> FakeArrivedAt { get; } = new();
    public bool FakeEmpty { get; set; }

    public RoundTeamState(CsTeam team)
    {
        Team = team;
    }

    public bool TryAccept(ParsedOrder incoming)
    {
        if (incoming.Kind == OrderKind.None)
            return false;
        if (incoming.Site == Bombsite.Unknown
            && incoming.Kind is OrderKind.GoSite or OrderKind.PlantedAt or OrderKind.SiteClear)
            return false;
        if (!OrderCommands.Beats(incoming.Rank, Rank))
            return false;

        bool siteChanged = incoming.Site != Bombsite.Unknown && incoming.Site != PrimarySite;
        Rank = incoming.Rank;
        Kind = incoming.Kind;
        Source = incoming.Source;

        if (incoming.Kind is OrderKind.GoSite or OrderKind.PlantedAt)
        {
            PrimarySite = incoming.Site;
            OtherSite = OrderCommands.Other(incoming.Site);
        }
        else if (incoming.Kind == OrderKind.SiteClear)
        {
            PrimarySite = OrderCommands.Other(incoming.Site);
            OtherSite = incoming.Site;
        }
        else if (incoming.Kind == OrderKind.PingBomb && incoming.Site != Bombsite.Unknown)
        {
            PrimarySite = incoming.Site;
            OtherSite = OrderCommands.Other(incoming.Site);
        }

        if (incoming.Rank <= OrderRanks.SiteClear || siteChanged)
        {
            ContactSlots.Clear();
            FakeArrivedAt.Clear();
            FakeEmpty = false;
        }

        if (incoming.Rank <= OrderRanks.SiteClear)
            FakeSlots.Clear();

        return true;
    }

    public void ApplyRoster(RosterAssignment assignment)
    {
        Roster = assignment;
        PrimarySlots.Clear();
        FakeSlots.Clear();
        foreach (int slot in assignment.PrimarySlots)
            PrimarySlots.Add(slot);
        foreach (int slot in assignment.FakeSlots)
            FakeSlots.Add(slot);
    }

    public void Wipe()
    {
        Rank = OrderRanks.NoInject;
        Kind = OrderKind.None;
        PrimarySite = Bombsite.Unknown;
        OtherSite = Bombsite.Unknown;
        Source = "";
        Roster = null;
        ContactSlots.Clear();
        FakeSlots.Clear();
        PrimarySlots.Clear();
        FakeArrivedAt.Clear();
        FakeEmpty = false;
    }

    public bool SplitActive =>
        Rank == OrderRanks.OpeningSplit && FakeSlots.Count > 0 && !FakeEmpty;
}
