# CS2-Bot-Orders

CounterStrikeSharp plugin that makes CS2 bots follow human site orders
(radio, silent ping, player ping). It injects intent into the existing
bot brain (`CSGameState` / `CCSBot`); it does not puppet WASD, does not
`Follow` players, and does not rewrite aim, pathing, or economy.

0.1.6 treats a middle-click ping as "something here" (`LookAtSpot`) and
a warning double-ping (`type=13 urgent=true`) as "enemy here, hold"
(`LastEnemyPosition`). It does not walk bots onto the marker. 0.1.5
puts the T bomb-carrier bot first in the site roster and still
writes its Goal during firefights. 0.1.4 collects GoA/GoB until freeze
ends (`EventRoundFreezeEnd`, 15s cap), injects on freeze end instead of
a 1s timer, and still silences cheap *changes* for 5s after thaw — a
first order with no ballots is kept. IgnoreRadio is armed during freeze
when a human assignment exists. HuntDropped falls back to the dropper
origin if `weapon_c4` is not in the world yet. It injects `CCSBot.GoalPosition` + `RepathTimer`
plus `IgnoreRadio` / Hurry while a human assignment is live (no Follow
of a human, no T search-order write). Dead humans keep their last
ballot until round wipe; the same SteamID taking over a bot can issue
new orders. Site destinations use the bomb-target collision center and
reject `(0,0,0)`. It listens to both the V/C chatwheel
(`playerchatwheel CW.*`) and classic Z radio (`player_radio`).

This is a standalone plugin repo. CS2-Bot-Improver is the combined
distribution; do not develop this plugin on Improver feature branches.

## Build

```text
dotnet build -c Release
```

Copy `bin/Release/net10.0/BotOrders.dll` (and `.pdb` if you want
symbols) into:

```text
game/csgo/addons/counterstrikesharp/plugins/BotOrders/
```

CSS hot-reload is enough; a full server restart is not required.

## Commands

| Command | Description |
| --- | --- |
| `css_botorders` | Print silence, claims, bomb fact, resolution. |

## License

[GNU Affero General Public License v3.0](LICENSE)
