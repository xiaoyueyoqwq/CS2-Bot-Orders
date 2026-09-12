using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal sealed class RoundTeamState
{
    public CsTeam Team { get; }
    public Dictionary<ulong, SiteBallot> Ballots { get; } = new();
    public Dictionary<ulong, float> LastClaimTime { get; } = new();
    public BombFact Bomb { get; set; } = BombFact.None;
    public Bombsite ClearSite { get; set; } = Bombsite.Unknown;
    public Resolution Current { get; private set; } = new(
        OrderRanks.NoInject,
        OrderKind.None,
        Bombsite.Unknown,
        ResolutionAction.NoInject,
        AssignAllBots: false,
        Reason: "init");
    public RosterAssignment? Roster { get; private set; }
    public HashSet<int> PrimarySlots { get; } = new();
    public HashSet<int> GuardSlots { get; } = new();
    public WorldPoint? DroppedOrigin { get; set; }
    public bool DeathDropped { get; set; }
    public int? FetchSlot { get; set; }

    public int Rank => Current.Rank;
    public OrderKind Kind => Current.Kind;
    public Bombsite PrimarySite => Current.Primary;
    public Bombsite OtherSite => OrderCommands.Other(Current.Primary);
    public string Source => Current.Reason;
    public bool HadAssignment { get; private set; }

    public RoundTeamState(CsTeam team)
    {
        Team = team;
    }

    public bool TryClaim(ulong steamId, Bombsite site, float now, out string reject)
    {
        reject = "";
        if (steamId == 0 || site is not (Bombsite.A or Bombsite.B))
        {
            reject = "invalid-claim";
            return false;
        }

        if (LastClaimTime.TryGetValue(steamId, out float last)
            && now - last < OrderCommands.ClaimCooldownSeconds)
        {
            if (Ballots.TryGetValue(steamId, out var existing) && existing.Site != site)
            {
                reject = "cooldown";
                return false;
            }
        }

        Ballots[steamId] = new SiteBallot(steamId, site);
        LastClaimTime[steamId] = now;
        return true;
    }

    public bool ExpireClaim(ulong steamId)
    {
        LastClaimTime.Remove(steamId);
        return Ballots.Remove(steamId);
    }

    public void ApplyResolution(Resolution resolution, RosterAssignment? roster)
    {
        Current = resolution;
        if (resolution.AssignAllBots)
            HadAssignment = true;
        else if (resolution.Action == ResolutionAction.NoInject)
            HadAssignment = false;

        ApplyRoster(roster ?? OrderRoster.None(0));
    }

    public void ApplyRoster(RosterAssignment assignment)
    {
        Roster = assignment;
        PrimarySlots.Clear();
        GuardSlots.Clear();
        foreach (int slot in assignment.PrimarySlots)
            PrimarySlots.Add(slot);
        foreach (int slot in assignment.GuardSlots)
            GuardSlots.Add(slot);
    }

    public IReadOnlyList<SiteBallot> LiveBallots()
    {
        return Ballots.Values.Where(b => b.Site is Bombsite.A or Bombsite.B).ToList();
    }

    public void Wipe()
    {
        Ballots.Clear();
        LastClaimTime.Clear();
        Bomb = BombFact.None;
        ClearSite = Bombsite.Unknown;
        HadAssignment = false;
        Current = new Resolution(
            OrderRanks.NoInject,
            OrderKind.None,
            Bombsite.Unknown,
            ResolutionAction.NoInject,
            AssignAllBots: false,
            Reason: "wipe");
        Roster = null;
        PrimarySlots.Clear();
        GuardSlots.Clear();
        DroppedOrigin = null;
        DeathDropped = false;
        FetchSlot = null;
    }
}
