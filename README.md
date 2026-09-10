# CS2-Bot-Orders

CounterStrikeSharp plugin that makes CS2 bots follow human site orders
(radio, silent ping, player ping). It injects intent into the existing
bot brain (`CSGameState` / `CCSBot`); it does not puppet WASD and does
not rewrite aim, pathing, or economy.

0.1.0 is listen-only: it records human GoA/GoB pings, assigns the 3/2
roster, wipes on round end, and logs `EventPlayerRadio.Slot` /
`CPlayerPing.Type` / `PlaceName`. It does not write GameState yet.

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
| `css_botorders` | Print the current round order state (window, rank, roster). |

## License

[GNU Affero General Public License v3.0](LICENSE)
