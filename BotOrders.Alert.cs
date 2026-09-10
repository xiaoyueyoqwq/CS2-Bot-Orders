using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

/// <summary>
/// Feeds ping XYZ as "something here" / "enemy here". Does not write
/// GoalPosition or NoisePosition (those pull bots onto the marker).
/// </summary>
internal static class OrderAlert
{
    internal static int Apply(
        IEnumerable<CCSPlayerController> livingBots,
        Vector dest,
        bool enemy,
        float duration)
    {
        int written = 0;
        float now = Server.CurrentTime;
        foreach (var bot in livingBots)
        {
            if (!bot.IsBot || bot.ControllingBot)
                continue;

            var pawn = bot.PlayerPawn.Value;
            var ccsBot = pawn?.Bot;
            if (pawn == null || !pawn.IsValid || ccsBot == null)
                continue;

            if (ccsBot.IsAttacking || ccsBot.IsEnemyVisible)
                continue;

            var look = ccsBot.LookAtSpot;
            if (look == null)
                continue;

            look.X = dest.X;
            look.Y = dest.Y;
            look.Z = dest.Z;

            ref float lookDuration = ref ccsBot.LookAtSpotDuration;
            lookDuration = duration;
            ref float lookStamp = ref ccsBot.LookAtSpotTimestamp;
            lookStamp = now;
            ref bool attack = ref ccsBot.LookAtSpotAttack;
            attack = enemy;
            ref bool clearIfClose = ref ccsBot.LookAtSpotClearIfClose;
            clearIfClose = true;

            if (enemy)
            {
                var last = ccsBot.LastEnemyPosition;
                if (last != null)
                {
                    last.X = dest.X;
                    last.Y = dest.Y;
                    last.Z = dest.Z;
                }

                ref float saw = ref ccsBot.LastSawEnemyTimestamp;
                saw = now;
                ref bool dead = ref ccsBot.IsLastEnemyDead;
                dead = false;
            }

            written++;
        }

        return written;
    }
}
