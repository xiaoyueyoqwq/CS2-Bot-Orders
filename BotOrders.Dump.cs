using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace BotOrders;

internal static class OrderDump
{
    internal static void Radio(
        ILogger logger,
        CCSPlayerController player,
        int slot,
        string place,
        bool mapped)
    {
        logger.LogInformation(
            "[BotOrders] radio name={Name} slot={PlayerSlot} team={Team} radioSlot={RadioSlot} place={Place} mapped={Mapped}",
            player.PlayerName,
            player.Slot,
            player.Team,
            slot,
            place,
            mapped);
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

    internal static void Accepted(ILogger logger, RoundTeamState state)
    {
        logger.LogInformation(
            "[BotOrders] accept team={Team} rank={Rank} kind={Kind} primary={Primary} other={Other} source={Source} primarySlots=[{PrimarySlots}] fakeSlots=[{FakeSlots}]",
            state.Team,
            state.Rank,
            state.Kind,
            state.PrimarySite,
            state.OtherSite,
            state.Source,
            string.Join(',', state.PrimarySlots),
            string.Join(',', state.FakeSlots));
    }

    internal static void Rejected(ILogger logger, CsTeam team, ParsedOrder incoming, int currentRank)
    {
        logger.LogInformation(
            "[BotOrders] reject team={Team} incomingRank={Incoming} currentRank={Current} kind={Kind} site={Site} source={Source}",
            team,
            incoming.Rank,
            currentRank,
            incoming.Kind,
            incoming.Site,
            incoming.Source);
    }

    internal static void Wipe(ILogger logger, string reason)
    {
        logger.LogInformation("[BotOrders] wipe reason={Reason}", reason);
    }
}
