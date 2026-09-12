# Bot 听从人类指令 — 设计笔记（暂存）

日期：2026-09-12  
状态：开发仓为 `CS2-Bot-Orders`（独立仓库）。Improver 只做合包 vendoring，不当开发仓。0.1.9：C4 始终下发给该队全部活 Bot。CT 地上包 / ping 中包（无 A/B）全员 `GuardNearby`（绕包 280，MustRun）。T 死亡掉包：全图最近 1 人 Fetch，其余 Guard。1500 不再挡编制。T 活人扔包仍不 Rally。0.1.8：T **死亡**掉包曾限 1500 才进编制（远处不动，已废）。0.1.7：标点只喂给离 ping ≤1500 的同队 Bot；远处不写 LookAt/LastEnemy；`ClearIfClose=false`。0.1.6：中键一下 = LookAt 有情况；两下警告 `type=13 urgent=true` = LastEnemy + 架枪；不把 ping 写成 Goal。0.1.5：T 持包 Bot 名册排第一，AllGo 不因交火 skip，捡包立刻重算。0.1.4：收集绑 `EventRoundFreezeEnd`（15s 上限）；解冻立刻 flush；静默 5s 只挡已有票的改口，没票的解冻第一下仍收；冻结里有编制先 IgnoreRadio；HuntDropped 用丢包者坐标作后备。0.1.3：解冻后廉价口令静默 5s；同一人改 A/B 无冷却。0.1.2：人类编制压过 Hide/Follow/bot 互喊；死后票保持；接管后同一 SteamID 的轮盘算指挥；A/B 用碰撞盒中心（拒绝 0,0,0）。不写 T 搜点顺序。

方向已经对齐：不重写 Bot AI，也不用 BotController 锁住 WASD 当傀儡。  
Bot 本来就会打 A/B、会搜点、会转点；缺的不是更强的自主判断，而是服从。人类作判断，AI 执行。执行时仍走现有寻路/枪法/搜点，不另写一套走位。

### 立场

在 AI 行为受限（Valve 脑、发现速度差、不会 IGL）的前提下，最好的补充不是增强 AI 自己的决策，而是绝对服从人类指令。AI 的任务是协助人类，不是替代人类做战术判断。

因此：

- 不去做「更好的自动发现炸弹 / 自己决定转点」这类增强。
- **世界事实**（引擎事件、ping 命中包实体）Bot 必须执行，不拿自己的 CSGameState 旧结论顶回去。
- **廉价口令**（GoA/GoB、Ping Site）只在无人反对时能动全队；有争议时抽不走别人的 Bot。
- 经济、发枪已经是 BotBuy 自我决策，且比人类快，不在服从范围内。
- 「绝对服从」指判断权在人类，不是服从每一句轮盘。社区服必须按「有人乱喊」来设计，不按五人开黑。怎么走到点、怎么架、怎么交火，仍用原有 AI。
- **绝对不能** `Follow` 玩家，除非以后有明确的「跟着我」（限人数，另档）。指挥只是让 Bot 去 Rush/堆 A 或 B。

---

## 1. 问题是什么

人类已经知道炸弹在 B，Bot 还往 A 走。  
人类判断 B 空了要转回 A，Bot 还在 B 蹲。

这不是「Bot 不会打 A 打 B」，而是发现/共享速度和人类差太多。  
要做的是：让 Bot 能被人类的轮盘/无线电/标记喂信息，同时继续走原有 AI。

---

## 2. 已确认规则（2026-09-10，抗恶意修订）

只听本队真人（SteamID）。没开口的真人当自由人，不占票。  
死人观战不能发新廉价口令，但上一张票保持到回合结束（死后不废票）。同一 SteamID 接管 Bot 后再按 V/C/Z 算新票。Bot 自己的脑发出的 radio / 「跟着我」不当指挥官。  
有人类编制时 bot 的 `IgnoreRadio` 打开、`IsFollowing=false`、Hurry+MustRun，不能用互喊改走位。无人类编制时恢复原生 Teamwork。  
回合结束 / 下一局 `round_start` 清空票、包事实、编制、注入。

默认威胁模型：同一队里会有人乱喊、空喊、对着干。不检测谁是 troll，不记黑名单。结构上让廉价口令没法抢走全队。

### 2.1 开局收集（冻结）+ 随后静默 5s

竞技购买就在冻结里。收集跟 `EventRoundFreezeEnd`，不跟死的 `round_start+15s`（15s 只在冻结事件没来时封顶）。冻结期内的侦测 A/B **要收下**，不能当静默丢掉。

| 段 | 时钟 | 廉价口令 | 注入 |
| --- | --- | --- | --- |
| 开局等指令 | `round_start` → `round_freeze_end` | 记账；同一人可立刻改 A/B | 不写 Goal（还冻着）；有编制则先 IgnoreRadio + 清 Follow；解冻瞬间 flush |
| 无线电静默 | 解冻后 5s | 队里已有票：忽略改口；**没票：解冻第一下仍收下** | 保持 / 没票的第一下立刻注入 |
| 之后 | 解冻 +5s 之后 | 再接受，走仲裁；同一人可立刻改 A/B | 立刻注入 |

世界事实（下包 / ping 中包 / CT 地上的包）各段都听。开局 3/2 退役。无争议 Go A/B = 全员该点。`round_end` 之后到下一局 `round_start` 之前的包事件丢掉。

### 2.2 信号分级

事实决定「世界是什么样」；口令只在没有争议时能动全队。Go A/B 永远不是事实。

| 级别 | 例子 | 伪造 | 能动全队 |
| --- | --- | --- | --- |
| 世界事实 | `bomb_planted`；地上 `weapon_c4`；ping 命中 `weapon_c4`/`planted_c4` | 不能 | 能。目标是包的坐标/已下的点 |
| 已验证清点 | Clear 且喊的人 `LastPlaceName` 硬归属该点，且包不在这 | 难 | 只能把人从该点叫走 |
| 廉价口令 | GoA/GoB（V 盘 / Z 无线电） | 按一下 | 仅无人反对（全票同一点或 2-1+） |
| 标点信息 | 中键一下 / 两下警告 | 按一下 | **不能**。只告诉离 ping ≤1500 的 Bot：一下 LookAt「有情况」；两下 `type=13` LastEnemy「有人、架枪」。不写 Goal，远处编制不动 |
| 口头假事实 | wePlanted、PlantedAt*、DroppedBomb、BombAt、PickedUpC4 无线电 | 按一下 | **忽略**，只认事件和实体 |

注入「包在这个坐标」和「全员强制走过去」不是同一件事：

- 已下包 / ping 命中包实体 → 仍去该点 / Hunt 包坐标。
- **CT 地上的包 / ping 中包（0.1.9）**：全员 GuardNearby（绕包 280，MustRun）。远处也来，不钉实体。
- **T 死亡掉包（0.1.9）**：全图最近 1 人 Fetch，其余全员 GuardNearby。这是世界事实，档 20。
- **T 自己把包扔角落（人还活着）** → 记事实，**不**全员集结（否则丢包 = 最高权限指挥）。
- 中键标点不是选点：`PlaceName` 是 BombsiteA/B 也不升成 GoSite。走位只认轮盘/无线电。标点是附近感知，不是把全队 Goal 改去标记。

### 2.3 按人记账（不是投票、不是 last-wins）

每个真人只保留最后一张 A/B 票（SteamID 去重）。连按不叠票。先 A 后 B 是改自己的票。  
没有倒计时，玩家不需要知道有「投票系统」。

同一人改 A/B 无间隔（冷却 0）。静默 5s 内廉价口令仍忽略。  
断线 / 离开 T/CT → 废票，立即重算。死亡不废票；接管后同一 SteamID 可改票。

活票结算（`OrderArbitration.Resolve`）：

1. 已下包且点已知 → 全员该点。
2. ping 命中包实体 → 有硬 A/B 则全员该点；否则 Hunt 包坐标（不猜 A/B）。
3. 地上有包且 CT → FetchAndGuard（近 1 捡、近守、远不动）。
4. T 死亡掉包 → FetchAndGuard。T 活人丢包 → 不 Rally，继续口令结算。
5. 活票 0 → 不注入。
6. 全票同一点（1 个真人也算）→ 全员该点。这是主场景（1 人 + 4 Bot）。
7. 2-1 及以上 → 多数点全员去。两人同意，第三人拦不住。
8. **1-1 不对半分**（恶意指挥主战场）：
   - T：持包的 **真人** 若有票，该点全员去。
   - 否则 / CT：有上一拍编制则保持；没有则不注入（自由）。
9. 后开口抽不走已经在执行的 Bot。整表重算，不是后到覆盖。

两人齐心喊同一点：全员去。对着干：编制停住或按 C4 走，不给捣乱的人分一半 Bot。

### 2.4 档位（间隔 10，第二期仍可插）

数字越小越优先。调度器只认结算后的一档。

| 档 | 名 | 第一期 |
| --- | --- | --- |
| 0 | RoundWipe | 回合清空 |
| 10 | FollowMe | 空档。第二期：限人数、只拉自己编制、不能压过已下包 |
| 20 | BombFact | 见 2.2 世界事实 |
| 30 | SiteClear | 已验证清点；15s 内当战术口令忽略 |
| 40 | SiteClaim | 15s 后廉价选点，经仲裁 |
| 50 | （空） | NeedBackup 等 |
| 60 | NoInject | 无票 / 1-1 破不了 / 静默期 |

### 2.5 地点硬归属

引擎没有「含外围的 A 区」。两套对不齐：`func_bomb_target`（能下包的触发体）和 `LastPlaceName`（导航地名）。

- 硬 A/B：在对应 `func_bomb_target` 内，或 `LastPlaceName` 解析为 BombsiteA/B。
- 边缘 / 中路 / 斜坡 / 通道：Unknown。**不要**用「离哪个点中心更近」。
- 伤亡：记 `place=` 和硬归属，**第一期不改编制**。死在 A 可能是假打或转点被黑，是推断不是事实。

### 2.6 T / CT

人数规则相同，任务不同。禁止把 T 的搜点顺序套到 CT。

- T：无争议 Go A → 全员 Rush A。带着 `weapon_c4` 的 **Bot** 必须进这编制（名册第一、交火不 skip Goal），不能让持包的去乱走。C4 在真人身上时，1-1 破平用持包人的票。T **活人**丢包不 Rally；**死亡**掉包 1 捡 + 近守。
- CT：无争议 Go A → 全员堆/转 A（不是假打进点）。1-1 不注入或保持上一拍。包已下 / ping 到包 → 全员去该点。地上的包 → 近 1 捡 + 近守，不跨图钉实体。Clear 某点（包未下、硬归属）→ 离开该点。

### 2.7 第一期范围

做：冻结收集、解冻后 5s 静默（没票仍收第一下）、按人记账、事实优先、1-1/多数/持包破平、已验证 Clear、回合清空。  
不做：GameState 写入（等 `libserver.so` dump）、开局 3/2、假打会合、接触留下改编制、按伤亡改编制、`Follow` 玩家、团队聊天「打A/打B」、要手雷、人质图、BotBuy、Hold、投票倒计时、troll 黑名单。

第二期插档：FollowMe ≈ 10（限人数）；NeedBackup ≈ 50（只拉空闲 Bot）。新指令开工时再定档位。

---

## 3. 注入层（不要新寻路）

两类写入，都复用现有 Bot 脑：

1. **世界事实**  
   已安放点、最后敌人位置、掉包、听到的噪音 → `CSGameState` / `CCSBot` 的 LastEnemy / Noise / planted site。
2. **战术意图**  
   开局搜索顺序、Clear 某点、Follow、Hold。不要只写 `GoalPosition`：Idle 会按旧搜索顺序重选目标。0.1.4 冻结里有编制先 IgnoreRadio + 清 Follow，解冻才写 Goal + 过期 `RepathTimer` + `MustRunTimer`/`HurryTimer`。不写 T 搜点顺序。目标点用 `func_bomb_target` 碰撞盒中心，拒绝 `(0,0,0)`。HuntDropped 先找地上的 `weapon_c4`，没有则用丢包者坐标。

BotAI 1.8.9 已经加速的是**引擎事件之后**：安放全队学到点、T 搜已知安放点、CT 全局听蜂鸣/捡包。  
它不消费人类的 ping / 轮盘。剩下的缺口正好是：

- 安放前的执行喊点
- 蜂鸣前人类已经看到/喊出的信息
- 空点转走

监听面（CSS 已有，当前插件都没用）：

- `EventPlayerPing`（Userid, X/Y/Z, Urgent, Entityid）
- `EventPlayerPingStop`
- `CPlayerPing`（Type, Urgent, PlaceName 最长约 18，例如 BombsiteA/B, PingedEntity, Player）
- `CCSPlayer_PingServices.PlayerPing`
- `EventPlayerRadio`（Userid, Slot）
- `EventPlayerChat`（Teamonly, Userid, Text）
- pawn `LastPlaceName`（NadeSystem 出站手雷无线电已经在用）

未解、实现前必须 dump / 实机打日志：

- CS2 的 `m_bombsiteSearchOrder` / `ClearBombsite` / `MarkBombsiteAsPlanted` 偏移，或直接调 native `CSGameState` 函数
- `CPlayerPing.Type` 的实机枚举
- `EventPlayerRadio.Slot` 与 `CW.*` / 经典无线电的对照表

已知冲突：

- `Idle_IsSafeAlwaysFalse` 会顶「守住这个位置」，但对离开出生点有利。
- BotState `OnTick` 目前不覆盖 Follow / Goal / Radio。
- NadeSystem 区域接管可能和转点打架。
- `botprofile` Teamwork 40–100 只在走原生无线电响应时才有意义。

不要用 BotController `Lock(All)` + WASD。那是另一条路。

---

## 4. CS2 现在人类怎么发指令

三套并行。**V/C 轮盘和 Z 无线电是两条通道**，不是同一件事：轮盘走命令 `playerchatwheel CW.*`，Z 走游戏事件 `player_radio`。插件两条都听，都不拦截。默认「侦测 A/B」在 **V 盘**，不在 Z 菜单。

| 通道 | 默认键 | 服务端入口 | 备注 |
| --- | --- | --- | --- |
| 快捷轮盘 1/2/3 | `C`=`+radialradio`，`V`=`+radialradio2`，`+radialradio3` 常未绑定 | `playerchatwheel CW.*`（另 dump `chatwheel_ping`） | 每页 8 格，可自定义。GoA/GoB 常静音，不发 `player_radio` |
| 经典无线电菜单 | `Z`=`radio`，另有 `radio1`/`radio2`/`radio3` | `EventPlayerRadio` Slot | `go_a`/`go_b` 在 CS2 上常失效。Slot 表等实机 dump，不猜 |
| 准星标记 | 鼠标中键 `player_ping` | `EventPlayerPing` / `CPlayerPing` | 打在准星处；轮盘 A/B 也可能再丢一个静音 ping |

玩家可把任意 `#Chatwheel_*` 绑到三页轮盘。默认页只是常用子集，完整指令表比默认 24 格大得多。  
`sv_playerradio_use_allowlist` 默认 1：旧 CS:GO `playerradio Radio.Loc*` 地点喊话默认被拦，私服关掉才通。

轮盘格子编号（顺时针，社区 CFG 常用）：

```
      3
   2     4
 1         5
   8     6
      7
```

### 4.1 默认三页（设置里的出厂绑定）

来源：社区 CFG 指南整理的 Valve 默认，不是我们服务器实测。玩家改过设置后，服务端仍能从 `CW.*` / ping 认出同一条指令。

轮盘 1（`+radialradio`，C）：

1. Go go go!
2. Stay together
3. Request a weapon
4. What's the plan?
5. Fall back
6. We should save
7. We should buy
8. Spread out

轮盘 1 的「要枪 / 该存 / 该买」对人类队友有用。对本仓库无效：Save/Hard 是 BotBuy 自己的经济决策，开局约 1 秒就买完；发枪也是 BotBuy 自己驱动，人类还没翻到轮盘格子 Bot 已经丢出来了。这三条不要接进指令跟随。

轮盘 2（`+radialradio2`，V）：

1. Cheer
2. No
3. Ping Site B
4. Thanks!
5. Sorry
6. Pep talk
7. Ping Site A
8. Yes

轮盘 3（`+radialradio3`）：

1. Enemy spotted
2. Heard noise
3. Multiple enemies here
4. Bomb was dropped
5. Need Backup
6. You De/Activate Bomb
7. Area clear
8. Sniper spotted

注意：默认页**没有** Mid、Rotate to me、Bombsite clear、Planted at A/B。这些要玩家改轮盘或走控制台/绑定才会发出来，但服务端同样收得到。

### 4.2 静音 ping（默认轮盘里最像「打 A/B」的东西）

`CW.GoA` / `CW.GoB` / `CW.GoToLocMid` / `CW.BombAt` 多数情况下**不说话**，只在对应位置丢标记。  
人类说「打 B」时，实际发出的经常是「Ping Site B」，不是经典 `go_b`。  
15 秒后的选点必须认这几条，不能只等有语音的 `GoGoGo`。

另有 `chatwheel_ping #Chatwheel_*`：部分指令会在自己头上或准星处再打一个 ping（Follow me / On my way / Rotate to me / Enemy spotted）。

---

## 5. 完整 Chatwheel 表（`playerchatwheel CW.*`）

对照来源：

- 2025 Steam 指南 sountain「Chat Wheel Phrases as Console Commands」（完整 `CW.*` + 是否有语音）
- SteamTracking/GameTracking-CS2 `game/csgo/pak01_dir/resource/csgo_english.txt`（2026-09 仍有这些 `#Chatwheel_*` token）
- 仓库归档 `overrides/archived/localizations/csgo_english.txt` / `csgo_schinese.txt`（中英对照）

有语音以 2025 指南标记为准；Agent 语音包不同，个别会哑。  
`#Chatwheel_*` 是界面/聊天文本；`CW.*` 才是语音槽。两者不是同名。

### 准备 Preparation

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| EcoRound | requestecoround | We should save | 我们应该存钱 | 无 | **不接。** Save/Hard 是 BotBuy 自我决策，开局约 1 秒已买完，人类再喊存/起来不及也没必要 |
| SpendRound | requestspend | We should buy | 我们应该购买 | 无 | **不接。** 同上 |
| NeedDrop | requestweapon | Request a weapon | 需要一把武器 | 无 | **不接。** 发枪积极性由 BotBuy AI 自己驱动，人类还没翻到这个选项 Bot 已经给枪了 |
| NeedPlan | requestplan | What's the plan? | 有什么计划？ | 无 | 弱信号，可不理 |
| NeedLeader | requestleader | Who will lead? | 谁来带队？ | 无 | 弱信号；或让最近 Bot Follow 人类 |

### 移动 Movement

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| GoGoGo | gogogo | Go go go! | 行动！行动！ | 有 | 开局无 A/B 时催离出生；或推进当前目标 |
| OMW | onmyway | On my way | 在路上 | 无 | 信息性；头上 ping |
| FollowMe | followme | Follow me | 跟着我 | 有 | `CCSBot.IsFollowing` + Leader=人类 |
| FollowingYou | followingyou | Following you | 跟着你 | 有 | 人类声明自己在跟；Bot 不必动 |
| GoA | aplan | Ping Site A | 侦测 A 点 | 无（静音 ping） | **15s 后廉价选点主入口**（经仲裁）；不是 Follow |
| GoB | bplan | Ping Site B | 侦测 B 点 | 无（静音 ping） | 同上，B |
| GoToLocMid | midplan | Ping Middle | 侦测中路 | 无（静音 ping） | 第三人称目标：lurk/控中，不要清掉 A/B |

### 命令 Commands

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| Regroup | rotatetome | Rotate to me | 转向我 | 有 | Follow 喊话者；头上 ping |
| StickTogether | sticktogether | Stay together | 集中在一起 | 有 | 收束到人类/C4 附近，不要再拆太开 |
| SpreadOut | spreadout | Spread out | 散开 | 无 | 减弱抱团；可取消 Stick |
| TeamFallBack | fallback | Fall back | 撤退 | 有 | 从当前接触面后撤，不要 Clear 已占点 |
| HoldPosition | holdposition | Hold this position | 守住这个位置 | 有 | Hold/Guard；**和 Idle_IsSafeAlwaysFalse 冲突** |
| CheckHostage | gethostage | Check the Hostage | 检查人质 | 无 | 人质图才有意义 |
| NeedQuiet | quiet | Need quiet | 别出声 | 无 | 弱；最多压 Bot 语音/少走 |
| ImAttacking | attacking | Attacking | 进攻 | 无 | 局中推进当前点，类似 GoGoGo |
| RequestGetHostages | requestgethostages | Get the hostages | 解救人质 | 无 | 人质图 |

### 报告 Report

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| HeardNoise | heardnoise | Heard noise | 听到噪音 | 有 | NoisePosition=喊话者处 |
| SeesEnemy | enemyspotted | Enemy spotted | 发现敌人 | 有 | LastEnemy / 朝 ping 看 |
| SeesSingleEnemy | oneenemyhere | One enemy here | 这儿有名敌人 | 有 | 同上，强度低 |
| SeesEnemiesMultiple | multipleenemieshere | Multiple enemies here | 这里发现数名敌人 | 有 | 同上，强度高；可把搜索偏向该点 |
| NeedBackup | needbackup | Need Backup | 需要支援 | 有 | 最近 1–2 个空闲 Bot 朝喊话者靠 |
| SniperWarning | sniperspotted | Sniper spotted | 发现狙击手 | 有 | LookAtSpot；不要当「清点」 |
| BombCarrierHere | bombcarrierspotted | Bomb carrier spotted | 发现炸弹携带者 | 无 | CT：LastEnemy+搜该侧 |
| InPosition | inposition | In position | 已就位 | 有 | 信息性 |
| CoveringYou | coveringyou | Covering you | 在掩护你 | 无 | 信息性 |
| SectorClear | sectorclear | Area clear | 区域安全 | 有 | **局中转点**：Clear 喊话者所在点 |
| ISeeBomb | bombcarrierspotted | Bomb carrier spotted | 发现炸弹携带者 | 无 | 指南里和 BombCarrierHere 共用文本；实机 Slot 需确认是否同槽 |
| wePlanted | planted | We planted the bomb | 我们已经安放炸弹 | 无 | 口头假事实，**忽略**；只认 `bomb_planted` |
| PickedUpC4 | bombpickedup | Bomb picked up | 捡起炸弹 | 无 | 口头假事实，**忽略**；只认 `bomb_pickup` |
| SeesHostagesBeingTaken | seehostagestaken | The hostages are being taken | 人质正在被解救中 | 无 | 人质图 |
| BombsiteClear | bombsiteclear | Bombsite clear | 清除炸弹 / 点已清 | 有 | **比 SectorClear 更硬**：Clear 对应 Bombsite |
| GuardingA | guardinga | I'm guarding A | 我在防守 A 点 | 无 | 信息性；Bot 可把该点当已有人看 |
| GuardingB | guardingb | I'm guarding B | 我在防守 B 点 | 无 | 同上 |

### 炸弹 Bomb Status

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| IFixBomb | ifixbomb | I'll De/Activate Bomb | 我来拆除/激活炸弹 | 无 | 人类声明自己拆/下；Bot 去 Cover 而不是抢包 |
| YouFixBomb | youfixbomb | You De/Activate Bomb | 你去拆除/激活炸弹 | 无 | 让最近有资格的 Bot 去拆/下 |
| DroppedBomb | droppedbomb | Bomb was dropped | 炸弹已投掷 | 有 | 口头假事实，**忽略**；只认 `bomb_dropped` 实体 |
| GuardingDroppedBomb | guardingbomb | Guarding the Bomb | 保卫炸弹 | 无 | 信息性 |
| BombAt | bombat | Ping Bomb | 侦测炸弹 | 无（静音 ping） | 无线电忽略；**ping 命中包实体**才当事实 |
| WeHaveTheBomb | ihavethebomb | Bomb picked up | 捡起炸弹 | 无 | 口头假事实，**忽略**；只认 `bomb_pickup` |
| PlantingAtA | plantingata | I'm planting at A | 我正在 A 点安放炸弹 | 无 | 口头假事实，**忽略** |
| PlantingAtB | plantingatb | I'm planting at B | 我正在 B 点安放炸弹 | 无 | 口头假事实，**忽略** |
| PlantedAtA | plantedata | I planted at A | 我已在 A 点安放炸弹 | 无 | 口头假事实，**忽略**；只认 `bomb_planted` |
| PlantedAtB | plantedatb | I planted at B | 我已在 B 点安放炸弹 | 无 | 同上 B |

### 回应 Responses（多数可不驱动走位）

| CW.* | #Chatwheel_* | 英 | 中 | 语音 |
| --- | --- | --- | --- | --- |
| Agree | affirmative | Yes | 是 | 有 |
| Disagree | negative | No | 否 | 有 |
| Compliment | compliment | Nice! | 漂亮！ | 有 |
| Thanks | thanks | Thanks! | 谢了！ | 有 |
| Cheer | cheer | Cheer | 欢呼！ | 有 |
| PepTalk | peptalk | Pep talk | 加油打气 | 有 |
| Sorry | sorry | Sorry | 对不起 | 无 |
| RoundLost | lostround | Lost this round | 这局白给了 | 有 |
| IKilledSniper | ikilledsniper | I killed sniper | 我击杀了狙击手 | 有 |
| MyHeadshot | gotheadshot | I got a headshot | 一发爆头 | 有 |
| SawHeadshot | sawheadshot | Saw your headshot | 精彩的精准打击 | 有 |

Agree/Disagree 以后若做「Bot 提议、人类确认」才有用，现在不必接走位。

### 投掷物 Grenades

| CW.* | #Chatwheel_* | 英 | 中 | 语音 | 对 Bot 的可能用法 |
| --- | --- | --- | --- | --- | --- |
| NeedDecoy | decoy | Need Decoys | 需要诱饵弹 | 无 | 可转 NadeSystem |
| NeedSmoke | smoke | Need Smokes | 需要烟雾弹 | 无 | 可转 NadeSystem |
| NeedGrenade | grenade | Need Frag Grenades | 需要破片手雷 | 无 | 可转 NadeSystem |
| NeedFire | fire | Need Molotovs | 需要燃烧瓶 | 无 | 可转 NadeSystem |
| NeedFlash | flashbang | Need Flashbangs | 需要闪光震撼弹 | 无 | 可转 NadeSystem |

这些是「我需要」不是「我要扔」。和 NadeSystem 现有出站无线电方向相反：人类要道具 vs Bot 汇报自己在扔。

---

## 6. 有 `#Chatwheel_*` 文本、但 2025 `CW.*` 表里没有的

GameTracking 的 `csgo_english.txt` 里还有这些 token。它们出现在设置分类/旧界面，**不能当成一定能从轮盘发出的指令**。实现时若 `EventPlayerRadio` 打出未知 Slot，再补。

- `plant` Plant the bomb
- `sitea` / `siteb`（和 aplan/bplan 英文相同「Ping Site A/B」，可能是旧名）
- `coverme` Cover me
- `help` Help
- `threeleft` / `twoleft` / `oneleft`
- `covering` Covering
- `pinned` I'm pinned
- `incombat` In combat
- `friendlyfire` Friendly fire
- `lastunitalive` Last unit alive
- `wewon` / `welost` / `wearewinning`
- `wekilledsniper`

分类标题（不是指令）：section_move / prepare / grenades / defend / bomb / enemystatus / enemykilled / combatstatus / responses / command / report。

---

## 7. 经典无线电（`Z` 菜单 / 控制台别名）

这些走 `EventPlayerRadio` Slot，**不是** `CW.*`。CS2 上部分已哑，Hitman 2025 指南用 ✅/❌ 标过。

仍较常可用：

`go` `fallback` `sticktog` `holdpos` `followme` `coverme` `takepoint` `regroup` `report` `reportingin` `enemyspot` `needbackup` `takingfire` `sectorclear` `inposition` `enemydown` `roger` `negative` `thanks` `compliment` `cheer` `getout`

CS2 上常失效或半残：

`go_a` `go_b` `needrop` `sorry`

旧 `playerradio Radio.LocBombA` 一类地点喊话：默认 allowlist 拦。不要作为主入口。

和轮盘的对应关系（语义接近，事件不同）：

| 经典 | 轮盘 |
| --- | --- |
| go | CW.GoGoGo |
| go_a / go_b | CW.GoA / CW.GoB（静音 ping，更常用） |
| followme | CW.FollowMe |
| holdpos | CW.HoldPosition |
| fallback | CW.TeamFallBack |
| sticktog | CW.StickTogether |
| coverme | （轮盘只有 CoveringYou，方向反了） |
| needbackup | CW.NeedBackup |
| sectorclear | CW.SectorClear |
| enemyspot | CW.SeesEnemy |
| needrop | CW.NeedDrop（经典常失效；即便能到也不接 BotBuy） |

15s 后选点应同时认：`CW.GoA/GoB` ping、经典 `go_a/go_b`（若 Slot 还能到）。团队聊天「打A/打B」第一期不做。

---

## 8. 扩展思路（按值得做的顺序）

下面都还是想法，不是实现清单。

### 第一层：信息差（最贴你说的两个例子）

1. **GoA/GoB（15s 后）→ 按人记账 + 仲裁全员去点**（3/2 已退役；1-1 不对半分）
2. **`bomb_planted`、ping 命中包实体 → 包事实**  
   口头 PlantedAt/wePlanted/DroppedBomb 忽略。人类 ping 到包实体时不必等蜂鸣。
3. **BombsiteClear / SectorClear → 仅硬归属且包不在该点**  
   离开该点。不要把人拖去人类身边。
4. **地上的包 → CT FetchAndGuard（近 1 捡+近守）；T 死亡掉包同样；T 活人丢包不 Rally**

### 第二层：队形/跟随（仍用 CCSBot 字段，不锁 WASD）

5. **FollowMe / Rotate to me → IsFollowing + Leader**
6. **NeedBackup / takingfire → 1–2 个空闲 Bot 朝喊话者**
7. **StickTogether / SpreadOut**  
   收束或取消收束；不要写成硬编队。
8. **HoldPosition**  
   先不要做，或做成短时 Guard。和 `Idle_IsSafeAlwaysFalse` 对打。
9. **TeamFallBack**  
   从接触面后撤到出生/另一点，不要 Clear。

### 第三层：局部感知

10. **HeardNoise / SeesEnemy / 单人/多人 / Sniper / 中键危险 ping**  
    附近 Bot 的 LookAtSpot + LastEnemy（中键已做，≤1500，不写 Noise/Goal）。这是最早提过的「危险标记」，现在排在炸弹事实后面。
11. **GoToLocMid**  
    第三人称目标，不是第三炸弹点。少数人去中，多数仍按 A/B 拆分。
12. **GuardingA/B**  
    该点已有人看，开局拆分时少派。

### 第四层：和现有插件衔接，而不是新 AI

13. **NeedSmoke/Flash/Fire/Grenade/Decoy → NadeSystem 请求（仅作候选）**  
    和 Bot 出站手雷无线电分开，避免对打。经济/发枪不要学这一层。
14. **YouFixBomb → 让有资格的 Bot 去拆/下；IFixBomb → 其他人 Cover**

### 明确不做

- 用 BotController 锁视角/WASD 演人类。
- 把每一条轮盘都映射成强制走位。回应类、Pep talk、Cheer 保持闲聊。
- 重写搜点/走位/枪法。
- 把「Clear B」做成把所有人拉到人类身边。
- **不要用轮盘去指挥 BotBuy。** EcoRound / SpendRound / NeedDrop（以及经典 `needrop`）全部忽略。Save/Hard 是 BotBuy 自己的经济决策，新回合约 1 秒内已经买完；发枪也是同一套 AI 自己驱动，比人类翻轮盘快。人类这三条是给人类队友用的，接进去只会和 BotBuy 抢决策。

---

## 9. 实现前还缺的实测

1. 实机打日志：人类轮盘 GoA/GoB、中键 ping、经典 `go`/`holdpos` 分别产生什么 `EventPlayerRadio.Slot`、`CPlayerPing.Type`、`PlaceName`。
2. `libserver.so` dump：`CSGameState` 搜点顺序、`ClearBombsite`、`MarkBombsiteAsPlanted` 在 CS2 的偏移或函数。
3. 确认 `CW.ISeeBomb` 是否真是独立槽，还是和 `BombCarrierHere` 撞文本。
4. 冻结时间内轮盘是否已经进 `player_radio`（15 秒静默从 `round_start` 算，含冻结；此项只确认日志会不会在静默期内刷屏）。

---

## 10. 和本仓库 / Improver 的关系

本文件跟着 `CS2-Bot-Orders` 走，不放在 Improver 开发分支上。合包时再把编好的 DLL 拷进 Improver 的 `addons/counterstrikesharp/plugins/BotOrders/`。

- BotAI 已经在引擎事件后加速学包；本设计补的是**人类事件**。
- NadeSystem 已用 `LastPlaceName` 和出站无线电；入站「需要烟雾」不要复用那条出站路径。
- BotBuy 的 Save/Hard 和发枪都是自我决策，开局极快。指令跟随不要碰 BotBuy，也不要把 EcoRound / SpendRound / NeedDrop 当入口。
- BotState 写 CCSBot 字段的方式可复用；不要在 OnTick 里每帧覆盖 Follow/Goal。

来源（查阅用，不必写进用户 README）：

- https://steamcommunity.com/sharedfiles/filedetails/?id=3522476376
- https://steamcommunity.com/sharedfiles/filedetails/?id=3055798463
- https://steamcommunity.com/sharedfiles/filedetails/?id=3078337270
- https://steamcommunity.com/sharedfiles/filedetails/?id=3038202828
- https://github.com/SteamTracking/GameTracking-CS2 （csgo_english.txt Chatwheel 段，ClientVersion 2000905 / 2026-09-07）
- Improver 归档 `overrides/archived/localizations/csgo_english.txt` 与 `csgo_schinese.txt`
