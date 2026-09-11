using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace BotOrders;

internal readonly record struct AlertResult(int Written, int Far, int Fight);

/// <summary>
/// Feeds ping XYZ as "something here" / "enemy here" to nearby bots.
/// Does not write GoalPosition or NoisePosition (those pull bots onto
/// the marker). Far bots keep their current assignment.
/// </summary>
internal static class OrderAlert
{
    internal static AlertResult Apply(
        IEnumerable<CCSPlayerController> livingBots,
        Vector dest,
        bool enemy,
        float duration)
    {
        int written = 0;
        int far = 0;
        int fight = 0;
        float now = Server.CurrentTime;
        foreach (var bot in livingBots)
        {
            if (!bot.IsBot || bot.ControllingBot)
                continue;

            var pawn = bot.PlayerPawn.Value;
            var ccsBot = pawn?.Bot;
            if (pawn == null || !pawn.IsValid || ccsBot == null)
                continue;

            var origin = pawn.AbsOrigin;
            if (origin == null || Distance3(origin, dest) > OrderCommands.AlertNearbyUnits)
            {
                far++;
                continue;
            }

            if (ccsBot.IsAttacking || ccsBot.IsEnemyVisible)
            {
                fight++;
                continue;
            }

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
            clearIfClose = false;
            ref float angle = ref ccsBot.LookAtSpotAngleTolerance;
            angle = OrderCommands.LookAtAngleTolerance;

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

        return new AlertResult(written, far, fight);
    }

    private static float Distance3(Vector a, Vector b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        float dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
