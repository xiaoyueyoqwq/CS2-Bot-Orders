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
    public override string ModuleVersion => "0.1.7";
    public override string ModuleAuthor => "xiaoyueyoqwq";
    public override string ModuleDescription =>
        "Bots follow human site orders (human radio beats hide/follow)";

    private readonly Dictionary<CsTeam, RoundTeamState> _teams = new()
    {
        [CsTeam.Terrorist] = new RoundTeamState(CsTeam.Terrorist),
        [CsTeam.CounterTerrorist] = new RoundTeamState(CsTeam.CounterTerrorist),
    };

    private readonly HashSet<string> _injectSkipLogged = new();
    private float _roundStartTime;
    private float _freezeEndTime;
    private bool _openingFlushed;
    private bool _freezeEnded;
    private bool _roundLive;
    private string _freezeEndSource = "";
    private CounterStrikeSharp.API.Modules.Timers.Timer? _progress;

    public override void Load(bool hotReload)
    {
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRoundFreezeEnd);
        RegisterEventHandler<EventPlayerRadio>(OnPlayerRadio);
        RegisterEventHandler<EventPlayerPing>(OnPlayerPing);
        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventBombPlanted>(OnBombPlanted);
        RegisterEventHandler<EventBombDropped>(OnBombDropped);
        RegisterEventHandler<EventBombPickup>(OnBombPickup);
        RegisterEventHandler<EventBotTakeover>(OnBotTakeover);

        if (hotReload)
        {
            _openingFlushed = true;
            _freezeEnded = true;
            _roundLive = true;
        }

        AddCommandListener("playerchatwheel", OnPlayerChatwheel, HookMode.Pre);
        AddCommandListener("chatwheel_ping", OnChatwheelPing, HookMode.Pre);

        AddCommand("css_botorders", "Print BotOrders round state", OnDumpCommand);

        _progress = AddTimer(1.0f, OnProgress, TimerFlags.REPEAT);

        var failures = OrderArbitration.SelfCheck();
        if (failures.Count == 0)
            Logger.LogInformation("BotOrders {Version} loaded. arbitration self-check ok.", ModuleVersion);
        else
        {
            foreach (string fail in failures)
                Logger.LogError("[BotOrders] arbitration self-check fail {Fail}", fail);
        }
    }

    public override void Unload(bool hotReload)
    {
        _progress?.Kill();
        _progress = null;
        RemoveCommandListener("playerchatwheel", OnPlayerChatwheel, HookMode.Pre);
        RemoveCommandListener("chatwheel_ping", OnChatwheelPing, HookMode.Pre);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        WipeAll("round_start");
        _roundStartTime = Server.CurrentTime;
        _freezeEndTime = 0f;
        _openingFlushed = false;
        _freezeEnded = false;
        _freezeEndSource = "";
        _roundLive = true;
        _injectSkipLogged.Clear();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        WipeAll("round_end");
        _roundLive = false;
        return HookResult.Continue;
    }

    private HookResult OnRoundFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_roundLive)
            return HookResult.Continue;
        MarkFreezeEnded("event");
        FlushOpeningIfDue();
        return HookResult.Continue;
    }

    private HookResult OnPlayerRadio(EventPlayerRadio @event, GameEventInfo info)
    {
        var player = @event.Userid;
        string place = player is { IsValid: true } ? PlaceOf(player) : "";
        bool mapped = OrderCommands.TryMapRadioSlot(@event.Slot, out var meaning);
        OrderDump.Radio(Logger, player, @event.Slot, place, mapped);

        if (!IsHumanCommander(player) || player == null)
            return HookResult.Continue;

        if (!mapped)
            return HookResult.Continue;

        var order = OrderCommands.FromRadioMeaning(meaning, "radio");
        if (order == null)
        {
            OrderDump.VerbalIgnored(Logger, player, meaning);
            return HookResult.Continue;
        }

        HandleCheapOrder(player, order.Value, place);
        return HookResult.Continue;
    }

    private HookResult OnPlayerChatwheel(CCSPlayerController? player, CommandInfo info)
    {
        string cmd = info.ArgCount > 0 ? info.GetArg(0) : "playerchatwheel";
        string token = info.ArgCount >= 2 ? info.GetArg(1) : "";
        string args = info.ArgString ?? "";
        bool mapped = OrderCommands.TryMapChatwheel(token, out var meaning);
        OrderDump.Chatwheel(Logger, player, cmd, token, args, mapped);

        if (!IsHumanCommander(player) || player == null)
            return HookResult.Continue;

        if (!mapped)
            return HookResult.Continue;

        var order = OrderCommands.FromRadioMeaning(meaning, "wheel");
        if (order == null)
        {
            OrderDump.VerbalIgnored(Logger, player, meaning);
            return HookResult.Continue;
        }

        HandleCheapOrder(player, order.Value, PlaceOf(player));
        return HookResult.Continue;
    }

    private HookResult OnChatwheelPing(CCSPlayerController? player, CommandInfo info)
    {
        string cmd = info.ArgCount > 0 ? info.GetArg(0) : "chatwheel_ping";
        string token = info.ArgCount >= 2 ? info.GetArg(1) : "";
        string args = info.ArgString ?? "";
        bool mapped = OrderCommands.TryMapChatwheel(token, out _);
        OrderDump.Chatwheel(Logger, player, cmd, token, args, mapped);
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

        try
        {
            var ping = Utilities.GetEntityFromIndex<CPlayerPing>(@event.Entityid);
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

        if (OrderCommands.IsBombEntity(entityName))
        {
            var site = OrderCommands.ParseSite(place);
            SetBombFact(new BombFact(BombKnowledge.PingedEntity, site), "ping:bomb-entity", player.Team);
            return HookResult.Continue;
        }

        if (!_roundLive || !OrderCommands.IsUsableDest(@event.X, @event.Y))
            return HookResult.Continue;

        bool enemy = type == OrderCommands.PingTypeWarning || urgent;
        var dest = new Vector(@event.X, @event.Y, @event.Z);
        var result = OrderAlert.Apply(
            LivingOn(player.Team).Where(p => p.IsBot),
            dest,
            enemy,
            enemy ? OrderCommands.EnemyLookSeconds : OrderCommands.AttentionLookSeconds);
        OrderDump.Alert(
            Logger,
            enemy ? "enemy" : "attention",
            player.Team,
            place,
            dest,
            result);
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

        string place = PlaceOf(victim);
        OrderDump.Casualty(Logger, victim, attacker, place, OrderCommands.ParseSite(place));
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        if (victim == null || !victim.IsValid)
            return HookResult.Continue;

        string place = PlaceOf(victim);
        OrderDump.Casualty(Logger, victim, attacker, place, OrderCommands.ParseSite(place));
        return HookResult.Continue;
    }

    private HookResult OnBotTakeover(EventBotTakeover @event, GameEventInfo info)
    {
        OrderDump.Takeover(Logger, @event.Userid, @event.Botid);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid)
            return HookResult.Continue;

        ExpireAndRecompute(player, "disconnect");
        return HookResult.Continue;
    }

    private HookResult OnBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        var planter = @event.Userid;
        string place = planter is { IsValid: true } ? PlaceOf(planter) : "";
        var site = OrderCommands.FromPlantedSiteIndex(@event.Site);
        if (site == Bombsite.Unknown)
            site = OrderSites.SiteFromPlantedC4();
        if (site == Bombsite.Unknown)
            site = OrderCommands.ParseSite(place);

        Logger.LogInformation(
            "[BotOrders] bomb_planted rawSite={Raw} site={Site} planter={Name} place={Place}",
            @event.Site,
            site,
            planter?.PlayerName ?? "?",
            place);

        SetBombFact(new BombFact(BombKnowledge.Planted, site), "event:bomb_planted", null);
        return HookResult.Continue;
    }

    private HookResult OnBombDropped(EventBombDropped @event, GameEventInfo info)
    {
        Logger.LogInformation(
            "[BotOrders] bomb_dropped userid={Name}",
            @event.Userid?.PlayerName ?? "?");

        if (!_roundLive)
            return HookResult.Continue;

        WorldPoint? dropPoint = WorldPoint.From(@event.Userid?.PlayerPawn.Value?.AbsOrigin);
        foreach (var state in _teams.Values)
            state.DroppedOrigin = dropPoint;

        SetBombFact(
            new BombFact(BombKnowledge.DroppedEntity, Bombsite.Unknown),
            "event:bomb_dropped",
            null);
        ScheduleHuntRetry(OrderCommands.HuntRetryFirstSeconds);
        ScheduleHuntRetry(OrderCommands.HuntRetrySecondSeconds);
        return HookResult.Continue;
    }

    private HookResult OnBombPickup(EventBombPickup @event, GameEventInfo info)
    {
        Logger.LogInformation(
            "[BotOrders] bomb_pickup userid={Name}",
            @event.Userid?.PlayerName ?? "?");

        if (!_roundLive)
            return HookResult.Continue;

        foreach (var state in _teams.Values)
        {
            bool clearedDropped = false;
            if (state.Bomb.Kind == BombKnowledge.DroppedEntity)
            {
                state.Bomb = BombFact.None;
                state.DroppedOrigin = null;
                OrderDump.BombFactLog(Logger, "event:bomb_pickup-clear-dropped", state.Bomb, state.Team);
                clearedDropped = true;
            }

            if (clearedDropped || (state.Team == CsTeam.Terrorist && state.HadAssignment))
                Recompute(state.Team);
        }

        return HookResult.Continue;
    }

    private void HandleCheapOrder(CCSPlayerController player, ParsedOrder order, string place)
    {
        if (InTacticalSilence())
        {
            if (_teams.TryGetValue(player.Team, out var silentState)
                && silentState.LiveBallots().Count > 0)
            {
                OrderDump.Silence(Logger, player, order.Source, place);
                return;
            }

            OrderDump.SilenceBypass(Logger, player, order.Source, "no-ballots");
        }

        if (order.Kind == OrderKind.SiteClear)
        {
            var site = order.Site != Bombsite.Unknown
                ? order.Site
                : OrderCommands.ParseSite(place);
            if (site == Bombsite.Unknown)
            {
                OrderDump.Rejected(Logger, "clear-not-hard-site", player, order.Source);
                return;
            }

            if (!_teams.TryGetValue(player.Team, out var state))
                return;
            if (state.Bomb.Kind == BombKnowledge.Planted && state.Bomb.Site == site)
            {
                OrderDump.Rejected(Logger, "clear-bomb-there", player, order.Source);
                return;
            }

            state.ClearSite = site;
            Recompute(player.Team);
            return;
        }

        if (order.Kind != OrderKind.GoSite || order.Site is not (Bombsite.A or Bombsite.B))
        {
            OrderDump.Rejected(Logger, "not-site-claim", player, order.Source);
            return;
        }

        if (!_teams.TryGetValue(player.Team, out var claimState))
            return;

        if (!claimState.TryClaim(player.SteamID, order.Site, Server.CurrentTime, out string reject))
        {
            OrderDump.Rejected(Logger, reject, player, order.Source);
            return;
        }

        OrderDump.Claim(Logger, player, order.Site, order.Source);
        Recompute(player.Team);
    }

    private void SetBombFact(BombFact fact, string source, CsTeam? onlyTeam)
    {
        if (!_roundLive)
            return;

        IEnumerable<RoundTeamState> targets = onlyTeam is CsTeam team && _teams.ContainsKey(team)
            ? new[] { _teams[team] }
            : _teams.Values;

        foreach (var state in targets)
        {
            if (state.Bomb.Kind == BombKnowledge.Planted
                && fact.Kind != BombKnowledge.Planted)
                continue;

            state.Bomb = fact;
            if (fact.Kind != BombKnowledge.DroppedEntity)
                state.DroppedOrigin = null;
            OrderDump.BombFactLog(Logger, source, fact, state.Team);
            Recompute(state.Team);
        }
    }

    private void ExpireAndRecompute(CCSPlayerController player, string why)
    {
        if (player.SteamID == 0)
            return;
        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            foreach (var team in _teams.Keys.ToArray())
            {
                if (_teams[team].ExpireClaim(player.SteamID))
                {
                    OrderDump.ExpireClaim(Logger, team, player.SteamID, why);
                    Recompute(team);
                }
            }

            return;
        }

        if (!_teams.TryGetValue(player.Team, out var state))
            return;
        if (!state.ExpireClaim(player.SteamID))
            return;
        OrderDump.ExpireClaim(Logger, player.Team, player.SteamID, why);
        Recompute(player.Team);
    }

    private void Recompute(CsTeam team)
    {
        if (team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return;
        if (!_teams.TryGetValue(team, out var state))
            return;

        var living = LivingOn(team);
        var connectedHumans = new HashSet<ulong>(
            Utilities.GetPlayers()
                .Where(p => IsConnectedHumanOn(p, team))
                .Select(p => p.SteamID));
        foreach (ulong steam in state.Ballots.Keys.ToArray())
        {
            if (connectedHumans.Contains(steam))
                continue;
            state.ExpireClaim(steam);
            OrderDump.ExpireClaim(Logger, team, steam, "not-on-team");
        }

        ulong? c4Holder = team == CsTeam.Terrorist
            ? OrderRoster.HumanC4HolderSteamId(living)
            : null;

        var resolution = OrderArbitration.Resolve(
            state.LiveBallots(),
            state.Bomb,
            team == CsTeam.Terrorist,
            c4Holder,
            state.PrimarySite,
            state.HadAssignment,
            state.ClearSite);

        var roster = resolution.AssignAllBots
            ? OrderRoster.AllBots(living)
            : OrderRoster.None(living.Count);

        state.ApplyResolution(resolution, roster);
        OrderDump.Resolve(Logger, state);
        NotifyInject(state);
    }

    private void NotifyInject(RoundTeamState state)
    {
        var bots = LivingOn(state.Team).Where(p => p.IsBot);
        OrderInject.Apply(Logger, state, bots, Clock(), log: true, _injectSkipLogged);
    }

    private void OnProgress()
    {
        if (!_roundLive)
            return;

        if (!_freezeEnded
            && OrderCommands.Elapsed(_roundStartTime, Server.CurrentTime) > OrderCommands.OpeningCollectSeconds)
        {
            MarkFreezeEnded("collect-cap");
        }

        FlushOpeningIfDue();

        foreach (var state in _teams.Values)
        {
            var bots = LivingOn(state.Team).Where(p => p.IsBot);
            OrderInject.Apply(Logger, state, bots, Clock(), log: false, _injectSkipLogged);
        }
    }

    private void FlushOpeningIfDue()
    {
        if (_openingFlushed)
            return;
        if (OrderCommands.IsInOpeningCollect(_roundStartTime, Server.CurrentTime, _freezeEnded))
            return;

        _openingFlushed = true;
        string source = string.IsNullOrEmpty(_freezeEndSource) ? "progress" : _freezeEndSource;
        OrderDump.OpeningFlush(
            Logger,
            source,
            OrderCommands.Elapsed(_roundStartTime, Server.CurrentTime));
        Recompute(CsTeam.Terrorist);
        Recompute(CsTeam.CounterTerrorist);
    }

    private void MarkFreezeEnded(string source)
    {
        if (_freezeEnded)
            return;
        _freezeEnded = true;
        _freezeEndTime = Server.CurrentTime;
        _freezeEndSource = source;
        OrderDump.FreezeEnd(
            Logger,
            source,
            OrderCommands.Elapsed(_roundStartTime, Server.CurrentTime));
    }

    private void ScheduleHuntRetry(float delay)
    {
        AddTimer(delay, () =>
        {
            if (!_roundLive)
                return;
            foreach (var team in _teams.Keys.ToArray())
            {
                if (_teams[team].Bomb.Kind != BombKnowledge.DroppedEntity)
                    continue;
                Recompute(team);
            }
        });
    }

    private InjectClock Clock() => new(_roundStartTime, _freezeEnded, _freezeEndTime);

    private void WipeAll(string reason)
    {
        foreach (var state in _teams.Values)
        {
            OrderInject.ClearOverride(LivingOn(state.Team).Where(p => p.IsBot));
            state.Wipe();
        }
        OrderDump.Wipe(Logger, reason);
    }

    private bool InTacticalSilence() =>
        OrderCommands.IsInTacticalSilence(_freezeEndTime, Server.CurrentTime, _freezeEnded);

    private void OnDumpCommand(CCSPlayerController? player, CommandInfo info)
    {
        info.ReplyToCommand(
            $"BotOrders {ModuleVersion} live={_roundLive} collect={(OrderCommands.IsInOpeningCollect(_roundStartTime, Server.CurrentTime, _freezeEnded) ? "yes" : "no")} silence={(InTacticalSilence() ? "yes" : "no")} freezeEnded={_freezeEnded} elapsed={OrderCommands.Elapsed(_roundStartTime, Server.CurrentTime):0.0}s flushed={_openingFlushed} radioSlots={OrderCommands.RadioSlots.Count} wheelTokens={OrderCommands.ChatwheelTokens.Count}");
        foreach (var state in _teams.Values)
        {
            string ballots = string.Join(',', state.Ballots.Values.Select(b => $"{b.SteamId}:{b.Site}"));
            info.ReplyToCommand(
                $"  {state.Team} rank={state.Rank} action={state.Current.Action} primary={state.PrimarySite} reason={state.Current.Reason} bomb={state.Bomb.Kind}/{state.Bomb.Site} clear={state.ClearSite} had={state.HadAssignment} ballots=[{ballots}] slots=[{string.Join(',', state.PrimarySlots)}]");
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
        if (player is not { IsValid: true, IsBot: false, IsHLTV: false })
            return false;
        if (player.SteamID == 0)
            return false;
        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            return false;
        return player.PawnIsAlive || player.ControllingBot;
    }

    private static bool IsConnectedHumanOn(CCSPlayerController? player, CsTeam team)
    {
        return player is { IsValid: true, IsBot: false, IsHLTV: false }
               && player.SteamID != 0
               && player.Team == team;
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
