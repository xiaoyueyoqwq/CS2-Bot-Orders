using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotOrders;

[MinimumApiVersion(304)]
public sealed class BotOrdersPlugin : BasePlugin
{
    public override string ModuleName => "BotOrders";
    public override string ModuleVersion => "0.1.0";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleDescription =>
        "Bots follow human site orders (listen-only in 0.1.0)";

    private const float ProgressInterval = 0.5f;
    private const float EmptyDwellSeconds = 3f;

    private readonly Dictionary<CsTeam, RoundTeamState> _teams = new()
    {
        [CsTeam.Terrorist] = new RoundTeamState(CsTeam.Terrorist),
        [CsTeam.CounterTerrorist] = new RoundTeamState(CsTeam.CounterTerrorist),
    };

    private float _roundStartTime;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _progressTimer;

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventPlayerRadio>(OnPlayerRadio);
        RegisterEventHandler<EventPlayerPing>(OnPlayerPing);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
        RegisterEventHandler<EventBombDropped>(OnBombDropped);
        RegisterEventHandler<EventBombPickup>(OnBombPickup);

        AddCommand("css_botorders", "Print BotOrders round state", OnDumpCommand);

        _progressTimer = AddTimer(ProgressInterval, OnProgressTick, TimerFlags.REPEAT);
        Logger.LogInformation("BotOrders {Version} loaded (listen-only, no GameState inject).", ModuleVersion);
    }

    public override void Unload(bool hotReload)
    {
        _progressTimer?.Kill();
        _progressTimer = null;
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        WipeAll("round_start");
        _roundStartTime = Server.CurrentTime;
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        WipeAll("round_end");
        return HookResult.Continue;
    }

    private HookResult OnPlayerRadio(EventPlayerRadio @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsHumanCommander(player) || player == null)
            return HookResult.Continue;

        string place = PlaceOf(player);
        bool mapped = OrderCommands.TryMapRadioSlot(@event.Slot, out var meaning);
        OrderDump.Radio(Logger, player, @event.Slot, place, mapped);

        if (!mapped)
            return HookResult.Continue;

        var order = OrderCommands.FromRadioMeaning(meaning, InOpeningWindow());
        if (order == null)
            return HookResult.Continue;

        var resolved = order.Value;
        if (resolved.Site == Bombsite.Unknown
            && resolved.Kind is OrderKind.SiteClear or OrderKind.PingBomb or OrderKind.PlantedAt)
        {
            var fromPlace = OrderCommands.ParseSite(place);
            if (fromPlace != Bombsite.Unknown)
                resolved = resolved with { Site = fromPlace };
        }

        Accept(player.Team, resolved);
        return HookResult.Continue;
    }

    private HookResult OnPlayerPing(EventPlayerPing @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsHumanCommander(player) || player == null)
            return HookResult.Continue;

        int type = 0;
        bool urgent = @event.Urgent;
        string place = "";
        string entityName = "";
        CPlayerPing? ping = null;

        try
        {
            ping = Utilities.GetEntityFromIndex<CPlayerPing>(@event.Entityid);
            if (ping != null && ping.IsValid)
            {
                type = ping.Type;
                urgent = ping.Urgent;
                place = ping.PlaceName ?? "";
                var pinged = ping.PingedEntity.Value;
                if (pinged != null && pinged.IsValid)
                    entityName = pinged.DesignerName;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[BotOrders] CPlayerPing read failed entityid={Id}", @event.Entityid);
        }

        if (string.IsNullOrEmpty(place))
            place = PlaceOf(player);

        OrderDump.Ping(Logger, player, type, urgent, place, entityName, @event.X, @event.Y, @event.Z);

        ParsedOrder? order = null;
        if (OrderCommands.IsBombEntity(entityName))
        {
            order = new ParsedOrder(
                OrderRanks.PlantedOrBombPing,
                OrderKind.PingBomb,
                OrderCommands.ParseSite(place),
                "ping:bomb");
        }
        else
        {
            var site = OrderCommands.ParseSite(place);
            if (site != Bombsite.Unknown)
                order = OrderCommands.RankGoSite(site, InOpeningWindow(), "ping:site");
        }

        if (order != null)
            Accept(player.Team, order.Value);

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (!IsLiveCombatant(attacker) || !IsLiveCombatant(victim) || attacker == null || victim == null)
            return HookResult.Continue;
        if (attacker.Team == victim.Team)
            return HookResult.Continue;

        MarkContact(attacker, victim);
        MarkContact(victim, attacker);
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        var site = OrderCommands.FromPlantedSiteIndex(@event.Site);
        var planter = @event.Userid;

        Logger.LogInformation(
            "[BotOrders] bomb_planted site={Site} planter={Name}",
            site,
            planter?.PlayerName ?? "?");

        if (site == Bombsite.Unknown)
            return HookResult.Continue;

        var order = new ParsedOrder(
            OrderRanks.PlantedOrBombPing,
            OrderKind.PlantedAt,
            site,
            "event:bomb_planted");

        Accept(CsTeam.Terrorist, order);
        Accept(CsTeam.CounterTerrorist, order);
        return HookResult.Continue;
    }

    private HookResult OnBombDropped(EventBombDropped @event, GameEventInfo info)
    {
        Logger.LogInformation(
            "[BotOrders] bomb_dropped userid={Name}",
            @event.Userid?.PlayerName ?? "?");
        return HookResult.Continue;
    }

    private HookResult OnBombPickup(EventBombPickup @event, GameEventInfo info)
    {
        Logger.LogInformation(
            "[BotOrders] bomb_pickup userid={Name}",
            @event.Userid?.PlayerName ?? "?");
        return HookResult.Continue;
    }

    private void OnProgressTick()
    {
        foreach (var state in _teams.Values)
            TickFakeEmpty(state);
    }

    private void TickFakeEmpty(RoundTeamState state)
    {
        if (!state.SplitActive)
            return;

        bool changed = false;
        foreach (int slot in state.FakeSlots.ToArray())
        {
            if (state.ContactSlots.Contains(slot))
            {
                state.FakeArrivedAt.Remove(slot);
                continue;
            }

            var bot = Utilities.GetPlayers().FirstOrDefault(p => p.Slot == slot);
            if (!IsLiveCombatant(bot) || bot == null)
                continue;

            var placeSite = OrderCommands.ParseSite(PlaceOf(bot));
            if (placeSite != state.OtherSite)
            {
                state.FakeArrivedAt.Remove(slot);
                continue;
            }

            if (!state.FakeArrivedAt.ContainsKey(slot))
                state.FakeArrivedAt[slot] = Server.CurrentTime;

            if (Server.CurrentTime - state.FakeArrivedAt[slot] >= EmptyDwellSeconds)
                changed = true;
        }

        if (!changed)
            return;

        bool allArrivedEmpty = state.FakeSlots.Count > 0
            && state.FakeSlots.All(slot =>
                !state.ContactSlots.Contains(slot)
                && state.FakeArrivedAt.TryGetValue(slot, out float arrived)
                && Server.CurrentTime - arrived >= EmptyDwellSeconds);

        if (!allArrivedEmpty || state.FakeEmpty)
            return;

        state.FakeEmpty = true;
        Logger.LogInformation(
            "[BotOrders] fake-empty team={Team} other={Other} → join {Primary}",
            state.Team,
            state.OtherSite,
            state.PrimarySite);
        NotifyInject(state);
    }

    private void MarkContact(CCSPlayerController player, CCSPlayerController enemy)
    {
        if (!_teams.TryGetValue(player.Team, out var state))
            return;
        if (!state.SplitActive || !state.FakeSlots.Contains(player.Slot))
            return;
        if (!state.ContactSlots.Add(player.Slot))
            return;

        Logger.LogInformation(
            "[BotOrders] contact-stay team={Team} bot={Name} slot={Slot} vs={Enemy} at={Place}",
            state.Team,
            player.PlayerName,
            player.Slot,
            enemy.PlayerName,
            PlaceOf(player));
        NotifyInject(state);
    }

    private void Accept(CsTeam team, ParsedOrder order)
    {
        if (team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return;
        if (!_teams.TryGetValue(team, out var state))
            return;

        if (!state.TryAccept(order))
        {
            OrderDump.Rejected(Logger, team, order, state.Rank);
            return;
        }

        var living = LivingOn(team);
        if (state.Rank == OrderRanks.OpeningSplit)
            state.ApplyRoster(OrderRoster.Assign(living, team));
        else
        {
            int[] slots = living.Select(p => p.Slot).ToArray();
            state.ApplyRoster(new RosterAssignment(slots, Array.Empty<int>(), slots.Length, 0));
        }

        OrderDump.Accepted(Logger, state);
        NotifyInject(state);
    }

    private void NotifyInject(RoundTeamState state)
    {
        var bots = LivingOn(state.Team).Where(p => p.IsBot);
        OrderInject.Apply(Logger, state, bots);
    }

    private void WipeAll(string reason)
    {
        foreach (var state in _teams.Values)
            state.Wipe();
        OrderDump.Wipe(Logger, reason);
    }

    private bool InOpeningWindow() =>
        OrderCommands.IsInOpeningWindow(_roundStartTime, Server.CurrentTime);

    private void OnDumpCommand(CCSPlayerController? player, CommandInfo info)
    {
        info.ReplyToCommand(
            $"BotOrders {ModuleVersion} window={(InOpeningWindow() ? "open" : "closed")} elapsed={Server.CurrentTime - _roundStartTime:0.0}s radioSlots={OrderCommands.RadioSlots.Count}");
        foreach (var state in _teams.Values)
        {
            info.ReplyToCommand(
                $"  {state.Team} rank={state.Rank} kind={state.Kind} primary={state.PrimarySite} other={state.OtherSite} fakeEmpty={state.FakeEmpty} src={state.Source} primary=[{string.Join(',', state.PrimarySlots)}] fake=[{string.Join(',', state.FakeSlots)}] contact=[{string.Join(',', state.ContactSlots)}]");
        }
    }

    private static IReadOnlyList<CCSPlayerController> LivingOn(CsTeam team)
    {
        return Utilities.GetPlayers()
            .Where(p => IsLiveCombatant(p) && p.Team == team)
            .ToList();
    }

    private static bool IsHumanCommander(CCSPlayerController? player)
    {
        return player is { IsValid: true, IsBot: false, IsHLTV: false };
    }

    private static bool IsLiveCombatant(CCSPlayerController? player)
    {
        return player is { IsValid: true, IsHLTV: false, PawnIsAlive: true }
               && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist
               && player.PlayerPawn.Value is { IsValid: true };
    }

    private static string PlaceOf(CCSPlayerController player)
    {
        return player.PlayerPawn.Value?.LastPlaceName ?? "";
    }
}
