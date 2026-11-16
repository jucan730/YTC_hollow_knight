# YTC_死亡统计mod

空洞骑士（Hollow Knight）死亡统计插件：按存档槽位记录**死在哪、怎么死、被谁杀**，并在世界里把每次死亡的位置用图钉标出来。

基于 BepInEx 5 + HarmonyX，纯反射实现，不改游戏资源文件。

---

## 功能

| 功能 | 说明 |
|---|---|
| 死亡记录 | 每次死亡记下时间、场景、死因、详情、坐标；按存档槽位分开存 |
| 死因排行 | 左栏按怪物/陷阱/地形统计"被谁杀了几次" |
| 场景排行 | 右栏按地图统计"死在哪张图最多"（同名合并，不会出现十几行"白色宫殿"） |
| 死亡图钉 | 在世界里画出每处死亡位置，同一个点重复死会堆叠 |
| 中文死因 | 怪物名取自游戏自带语言表，不靠手写对照 |
| 判定日志 | 追加式 `diag-yyyyMMdd.log`，按天分文件、重启不丢，方便排查判错 |

## 安装

1. 先装 **BepInEx 5.4.x（x64）**：解压到游戏根目录，运行一次游戏让它生成 `BepInEx\plugins`。
2. 把 `BepInEx` 文件夹整个拖进游戏根目录（`steamapps\common\Hollow Knight\`）。
3. 进游戏，按 **F4** 打开面板。

详细的图文步骤见 [`docs/YTC_死亡统计mod_安装步骤.docx`](docs/YTC_死亡统计mod_安装步骤.docx)，或者直接下载 [`dist/`](dist/) 里的压缩包（DLL + 说明齐全）。

> 旧版本（`XiaoCanDeathStats.dll` / `DeathStats.dll` / `XiaoCanHelper.dll`）要先删掉，同一个插件加载两遍会让一次死亡记两次。

## 使用

| 操作 | 按键 |
|---|---|
| 开关面板 | `F4` |
| 切换存档槽位 | `1`~`9`、`0`（0 = 不区分存档） |
| 左右切栏 | `←` `→` / `A` `D` |
| 上下滚动 | `↑` `↓` / `W` `S`（优先读游戏自己的视角轴） |
| 确认 | `回车` |
| 关闭 | `Esc` |

面板打开时游戏会暂停，关闭后恢复（`Display/PauseOnOpen` 可关）。

右上角的房间名 HUD 默认关闭，需要调试时把 `Display/ShowRoomHud` 改成 `true`。

## 死因是怎么判出来的

一个"我到底被谁打死的"问题，实际要过五层：

1. **父链**：伤害物有没有挂在某个怪物下面 → 直接沿 transform 往上找。
2. **生成记录**：拦截 `SpawnObject` 类的生成动作，记录"谁放了什么"。中间层不是怪也没关系（蘑菇炮台 → 孢子炸弹 → 毒气爆炸），会继续往上追一层。
3. **名字词根**：`Shockwave Spurt L` 这种带 L/R 变体的，跟预制体 `Shockwave Wave` 只差最后一个词，按首词匹配。预制体名末尾的 L/M/R 也做了归一。
4. **自爆位置**：自爆怪不走 `HealthManager.Die`（自己 `Destroy`），临终记录里查不到它 → 每 0.4 秒扫描一次场上还有哪些自爆怪，消失的那一帧记下位置，等爆炸物出现时按距离匹配。
5. **场地规则**：辐光战场、格林 / 梦魇之王、黑卵圣殿的空洞骑士、初见 / 二见大黄蜂，按场景强制定为对应 BOSS。

五层都失败就老实记"未知"，不硬猜成一个具体怪物名。

## 数据存在哪

```
Hollow Knight\
└─ BepInEx\
   └─ plugins\
      └─ YTC_DeathStats\
         ├─ slot1.tsv            ← 死亡记录（制表符分隔，8 列）
         ├─ slot2.tsv
         └─ diag-20261116.log    ← 判定日志（按天一个文件）
```

`slotN.tsv` 列顺序：

```
时间	场景	死因	详情	X	Y	Z	AnchorVer
```

`AnchorVer` 是图钉落点的算法版本号，老记录靠它决定要不要重新推算标记位置。

从旧目录 `XiaoCanDeathStats\`、`DeathStats\` 升级上来的，首次启动会自动把 `slotN.tsv` 复制过来，同名文件不覆盖。

## 常见问题

**面板打不开**：确认 `BepInEx\LogOutput.log` 里有 `Loading [YTC_死亡统计mod 0.9.25]`。没有说明 DLL 没被加载，检查 BepInEx 是不是 x64 版本、是不是放在了游戏根目录。

**死因显示"未知"**：打开 `diag-*.log`，找 `特效伤害（无归属记录）` 那一行，里面带出生点附近有谁、名字溯源结果、以及房间里的怪清单。

**升级后记录不见了**：检查数据目录是不是变成了两个（旧名 + 新名），把旧的 `slotN.tsv` 手动拷到 `YTC_DeathStats\` 下。

## 编译

需要：Windows、.NET Framework 4.7.2+、游戏本体（提供 `Managed` 下的引用 DLL 和 BepInEx 的 `BepInEx.dll` / `0Harmony.dll`）。

```bat
set HK_DIR=D:\SteamLibrary\steamapps\common\Hollow Knight
cd src
build.bat
```

`build.bat` 会优先用仓库外的 `tools/roslyn/tasks/net472/csc.exe`（C# 最新语法），找不到就退回系统自带的 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`。编译产物输出到 `out\`，并自动复制到 `BepInEx\plugins\`（想只编译不部署，把最后一步 `copy` 注释掉）。

`HK_DIR` 不设置时默认找 Steam 默认安装路径。

## 版本

当前 **v0.9.25**，改动见 [CHANGELOG.md](CHANGELOG.md)。

## 许可

MIT。游戏本体与其中文本资源版权归 Team Cherry 所有，本仓库只包含插件代码。
