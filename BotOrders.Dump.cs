using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotOrders;

internal static class OrderDump
{
    internal static void Radio(
        ILogger logger,
        CCSPlayerController? player,
        int slot,
        string place,
        bool mapped)
    {
        logger.LogInformation(
            "[BotOrders] radio name={Name} slot={PlayerSlot} team={Team} radioSlot={RadioSlot} place={Place} mapped={Mapped} isBot={IsBot} controllingBot={Controlling} pawnAlive={Alive} steam={Steam}",
            player?.PlayerName ?? "?",
            player?.Slot ?? -1,
            player?.Team,
            slot,
            place,
            mapped,
            player?.IsBot,
            player?.ControllingBot,
            player?.PawnIsAlive,
            player?.SteamID ?? 0UL);
    }

    internal static void Chatwheel(
        ILogger logger,
        CCSPlayerController? player,
        string cmd,
        string token,
        string args,
        bool mapped)
    {
        logger.LogInformation(
            "[BotOrders] chatwheel name={Name} slot={PlayerSlot} team={Team} cmd={Cmd} token={Token} args={Args} mapped={Mapped} isBot={IsBot} controllingBot={Controlling} pawnAlive={Alive} steam={Steam}",
            player?.PlayerName ?? "?",
            player?.Slot ?? -1,
            player?.Team,
            cmd,
            token,
            args,
            mapped,
            player?.IsBot,
            player?.ControllingBot,
            player?.PawnIsAlive,
            player?.SteamID ?? 0UL);
    }

    internal static void Ping(
        ILogger logger,
        CCSPlayerController player,
        int type,
        bool urgent,
        string place,
        string entity,
        float x,
        float y,
        float z)
    {
        logger.LogInformation(
            "[BotOrders] ping name={Name} slot={PlayerSlot} team={Team} type={Type} urgent={Urgent} place={Place} entity={Entity} xyz={X:0},{Y:0},{Z:0}",
            player.PlayerName,
            player.Slot,
            player.Team,
            type,
            urgent,
            place,
            entity,
            x,
            y,
            z);
    }

    internal static void Alert(
        ILogger logger,
        string kind,
        CsTeam team,
        string place,
        Vector xyz,
        AlertResult result)
    {
        logger.LogInformation(
            "[BotOrders] alert kind={Kind} team={Team} place={Place} xyz={X:0},{Y:0},{Z:0} bots={Bots} far={Far} fight={Fight} radius={Radius:0}",
            kind,
            team,
            place,
            xyz.X,
            xyz.Y,
            xyz.Z,
            result.Written,
            result.Far,
            result.Fight,
            OrderCommands.AlertNearbyUnits);
    }

    internal static void Silence(ILogger logger, CCSPlayerController player, string source, string place)
    {
        logger.LogInformation(
            "[BotOrders] silence name={Name} team={Team} source={Source} place={Place}",
            player.PlayerName,
            player.Team,
            source,
            place);
    }

    internal static void SilenceBypass(ILogger logger, CCSPlayerController player, string source, string why)
    {
        logger.LogInformation(
            "[BotOrders] silence-bypass why={Why} name={Name} team={Team} source={Source}",
            why,
            player.PlayerName,
            player.Team,
            source);
    }

    internal static void FreezeEnd(ILogger logger, string source, float elapsed)
    {
        logger.LogInformation(
            "[BotOrders] freeze-end source={Source} elapsed={Elapsed:0.0}s",
            source,
            elapsed);
    }

    internal static void OpeningFlush(ILogger logger, string source, float elapsed)
    {
        logger.LogInformation(
            "[BotOrders] opening-flush source={Source} elapsed={Elapsed:0.0}s",
            source,
            elapsed);
    }

    internal static void Claim(ILogger logger, CCSPlayerController player, Bombsite site, string source)
    {
        logger.LogInformation(
            "[BotOrders] claim name={Name} steam={Steam} team={Team} site={Site} source={Source}",
            player.PlayerName,
            player.SteamID,
            player.Team,
            site,
            source);
    }

    internal static void Rejected(ILogger logger, string why, CCSPlayerController? player, string source)
    {
        logger.LogInformation(
            "[BotOrders] reject why={Why} name={Name} team={Team} source={Source}",
            why,
            player?.PlayerName ?? "?",
            player?.Team,
            source);
    }

    internal static void VerbalIgnored(ILogger logger, CCSPlayerController player, RadioMeaning meaning)
    {
        logger.LogInformation(
            "[BotOrders] verbal-ignore name={Name} team={Team} meaning={Meaning}",
            player.PlayerName,
            player.Team,
            meaning);
    }

    internal static void Resolve(ILogger logger, RoundTeamState state)
    {
        string ballots = string.Join(',', state.Ballots.Values.Select(b => $"{b.SteamId}:{b.Site}"));
        logger.LogInformation(
            "[BotOrders] resolve team={Team} rank={Rank} kind={Kind} action={Action} primary={Primary} assignAll={Assign} reason={Reason} bomb={BombKind}/{BombSite} deathDrop={Death} clear={Clear} ballots=[{Ballots}] fetch=[{Fetch}] guard=[{Guard}]",
            state.Team,
            state.Current.Rank,
            state.Current.Kind,
            state.Current.Action,
            state.Current.Primary,
            state.Current.AssignAllBots,
            state.Current.Reason,
            state.Bomb.Kind,
            state.Bomb.Site,
            state.DeathDropped,
            state.ClearSite,
            ballots,
            string.Join(',', state.PrimarySlots),
            string.Join(',', state.GuardSlots));
    }

    internal static void ExpireClaim(ILogger logger, CsTeam team, ulong steamId, string why)
    {
        logger.LogInformation(
            "[BotOrders] expire-claim team={Team} steam={Steam} why={Why}",
            team,
            steamId,
            why);
    }

    internal static void BombFactLog(ILogger logger, string source, BombFact fact, CsTeam? team = null)
    {
        logger.LogInformation(
            "[BotOrders] bomb-fact source={Source} kind={Kind} site={Site} team={Team}",
            source,
            fact.Kind,
            fact.Site,
            team);
    }

    internal static void Casualty(
        ILogger logger,
        CCSPlayerController victim,
        CCSPlayerController? attacker,
        string place,
        Bombsite hardSite)
    {
        logger.LogInformation(
            "[BotOrders] casualty victim={Victim} team={Team} attacker={Attacker} place={Place} hardSite={Site}",
            victim.PlayerName,
            victim.Team,
            attacker?.PlayerName ?? "?",
            place,
            hardSite);
    }

    internal static void Wipe(ILogger logger, string reason)
    {
        logger.LogInformation("[BotOrders] wipe reason={Reason}", reason);
    }

    internal static void Inject(
        ILogger logger,
        CCSPlayerController bot,
        InjectKind kind,
        Bombsite dest,
        Vector xyz,
        Vector? botOrigin,
        string reason,
        bool follow,
        bool ignoreRadio,
        string destSource,
        bool carriesC4)
    {
        logger.LogInformation(
            "[BotOrders] inject bot={Name} slot={Slot} kind={Kind} dest={Dest} xyz={X:0},{Y:0},{Z:0} botxyz={BX:0},{BY:0},{BZ:0} team={Team} reason={Reason} follow={Follow} ignoreRadio={IgnoreRadio} destSource={DestSource} c4={C4}",
            bot.PlayerName,
            bot.Slot,
            kind,
            dest,
            xyz.X,
            xyz.Y,
            xyz.Z,
            botOrigin?.X ?? 0,
            botOrigin?.Y ?? 0,
            botOrigin?.Z ?? 0,
            bot.Team,
            reason,
            follow,
            ignoreRadio,
            destSource,
            carriesC4);
    }

    internal static void InjectFail(
        ILogger logger,
        CCSPlayerController bot,
        InjectKind kind,
        Bombsite dest,
        string why)
    {
        logger.LogInformation(
            "[BotOrders] inject-fail bot={Name} slot={Slot} kind={Kind} dest={Dest} team={Team} why={Why}",
            bot.PlayerName,
            bot.Slot,
            kind,
            dest,
            bot.Team,
            why);
    }

    internal static void Takeover(
        ILogger logger,
        CCSPlayerController? human,
        CCSPlayerController? bot)
    {
        logger.LogInformation(
            "[BotOrders] takeover name={Name} slot={Slot} team={Team} isBot={IsBot} controllingBot={Controlling} pawnAlive={Alive} steam={Steam} botName={BotName} botSlot={BotSlot} botIsBot={BotIsBot} botSteam={BotSteam}",
            human?.PlayerName ?? "?",
            human?.Slot ?? -1,
            human?.Team,
            human?.IsBot,
            human?.ControllingBot,
            human?.PawnIsAlive,
            human?.SteamID ?? 0UL,
            bot?.PlayerName ?? "?",
            bot?.Slot ?? -1,
            bot?.IsBot,
            bot?.SteamID ?? 0UL);
    }

    internal static void InjectHoldFight(
        ILogger logger,
        CCSPlayerController bot,
        InjectKind kind,
        Bombsite dest,
        string reason)
    {
        logger.LogInformation(
            "[BotOrders] inject-hold-fight bot={Name} slot={Slot} kind={Kind} dest={Dest} team={Team} reason={Reason}",
            bot.PlayerName,
            bot.Slot,
            kind,
            dest,
            bot.Team,
            reason);
    }
}
