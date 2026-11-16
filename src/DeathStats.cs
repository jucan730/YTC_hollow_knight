using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DeathStats
{
    /// <summary>
    /// 死亡统计 Mod - Hollow Knight (Unity 6)
    ///
    /// v0.9.25
    ///  - 生成归属支持「往上追一层」：蘑菇炮台放的 Spore Bomb 再放 Gas Explosion 时，
    ///    中间那层不是怪（isEnemy=false）会被丢掉，导致最上层的怪查不到。
    ///    现在非怪宿主也会继续追它自己的生成记录，一路继承到真正的怪。
    ///  - 预制体名的 L/M/R 变体归一：Gas Explosion Recycle L/M/R 共用一条归属记录，
    ///    不会再出现「这一发记到了、换一种变体就未知」。
    ///
    /// v0.9.24
    ///  - 自爆怪炸出的 Gas Explosion 不再记成「未知」。这类特效没有父物体、没有生成记录、
    ///    也没有共同词根，而且自爆怪不走 HealthManager.Die（临终记录里查不到它），
    ///    唯一的线索是固定映射表 sBurstEffects（Gas Explosion ← Bursting Bouncer /
    ///    Volatile Gruzzer）。前提是房间里确实站着这种怪，一只都找不到就照旧记未知。
    ///  - 爆炸溯源的「临终位置」兜底放宽到 2.5 秒 / 12 单位（自爆到命中有一小段延迟）。
    ///  - 敌人死亡时补一行诊断日志（敌人死亡 | 名字 | 距英雄），方便核对溯源为什么没命中。
    ///
    /// v0.9.23
    ///  - 全名统一成 YTC_死亡统计mod：插件显示名 / DLL 名（YTC_DeathStats.dll）/
    ///    数据目录（plugins\YTC_DeathStats）三处口径一致，不再混用「小残」「Death Stats」。
    ///  - 数据目录再改一次名，启动时把 XiaoCanDeathStats、DeathStats 两个旧目录的
    ///    slotN.tsv 一并搬过来（新目录已有同名文件则不覆盖）。
    ///
    /// v0.9.22
    ///  - 新增追加式判定日志 diag-yyyyMMdd.log（Logging/AppendDiagLog，默认开）：
    ///    只记死因/归属判定的关键行，按天分文件、重启不丢；BepInEx 自己的 LogOutput.log 每次启动会被重写。
    ///  - 去掉名字里的「小残」，作者名与命名空间分开。
    ///
    /// v0.9.21
    ///  - 右上角房间名 HUD 默认关闭（Display/ShowRoomHud 默认 false），正式版不再常驻调试信息。
    ///  - 提供可分发压缩包与安装说明（见 out/ 下的发布包）。
    ///
    /// v0.9.20
    ///  - 大黄蜂改「名字优先、场景兜底」：`Hornet Boss 1` / `Hornet Boss 2` 末尾的数字是
    ///    「第几战」，不是克隆序号。新增 sNumberedNames，在 StripTrailingNumber 之前先查
    ///    （hornetboss1→初见大黄蜂 / hornetboss2→二见大黄蜂），所以从物体真名就能分场。
    ///    场地表保留作兜底：针、丝、冲击波归不到她头上的那些死亡，靠场景补成对应变体。
    ///    两者冲突时以名字为准（`exactVariant` 不被场地覆盖）。
    ///
    /// v0.9.19
    ///  - 「场地 BOSS」从单条硬编码升级成规则表 sArenaBosses：辐光（Dream_Final*/GG_Radiance*）、
    ///    格林（Grimm_Main_Tent_boss/GG_Grimm）、梦魇之王（Grimm_Nightmare/GG_Grimm_Nightmare）、
    ///    空洞骑士（Room_Final_Boss_Core/GG_Hollow_Knight）整场死亡统一归场地主，
    ///    原判定写进 detail。例外：判成「地形/陷阱」的不吞。
    ///  - 大黄蜂分「初见大黄蜂」（Fungus1_04_boss/GG_Hornet_1）与「二见大黄蜂」
    ///    （Deepnest_East_Hornet_boss/GG_Hornet_2）；旧存档里的「大黄蜂」读档时按场景号还原。
    ///
    /// v0.9.18
    ///  - 多主人候选不再放弃：取距离生成物最近的那只；距离打平（差值 ≤0.01）随机选一只。
    ///    覆盖三处：出生点反查、预制体名多主（含首词匹配）、名字词根的同等分候选。
    ///    PrefabRec 增加 owners 列表，可记住同一预制体的所有主人。
    ///  - 辐光战场（Dream_Final* / GG_Radiance*）整场死亡统一记「辐光」，
    ///    原判定写进 detail（辐光战场·原死因），日志另打一行。
    ///
    /// v0.9.17
    ///  - 认不出主人的攻击物统一显示「未知」：以前按物体名猜的归类（冲击波/爆炸/毒气/
    ///    刺球-弹射物/英文原名）全部并入「未知」，归类只留在 detail 里备查。
    ///  - 尖刺 → 「地形/陷阱」：荆棘、骨刺、白宫电锯式机关在面板上归为同一类死法。
    ///  - 旧存档读档时做口径迁移（sKillerRename），不用清空统计。
    ///
    /// v0.9.16
    ///  - 出生点判定补采：池化伤害物 OnEnable 时还在池位上，60ms 后重采一次真实坐标。
    ///    这就是 Shockwave Spurt L「出生点附近无怪」的根因（记录的坐标离怪 7 格以上）。
    ///  - 预制体账加「首词宽松匹配」：物体 Shockwave Spurt L ↔ 预制体 Shockwave Wave，
    ///    精确名查不到时按首词匹配，只在首词唯一对应一个主人时才认。
    ///  - 诊断日志补「出生点最近是谁/多远」与「预制体账条数」。
    ///
    /// v0.9.15
    ///  - Room_Final_Boss_Core 补成「黑卵圣殿」（空洞骑士最终战，之前一直显示英文）。
    ///  - 冲击波这类「查不到主人」的特效伤害加了最后一招：**整个房间只有一种怪**时直接归给它。
    ///    例：Crossroads_48 只有一只躯壳守卫，它砸地放的 Shockwave Spurt L 走池子生成、
    ///    SpawnObject 没拦到，名字也推不出主人，以前记成「冲击波」，现在记「躯壳守卫」。
    ///    房间里有两种以上不同的怪就不猜，仍旧记「冲击波」。
    ///
    /// v0.9.14
    ///  - 修 v0.9.13 没生效：死亡瞬间骑士本体 SpriteRenderer 已经被关掉，实时读包围盒拿到 0，
    ///    落点就退回了原坐标（图钉还贴头顶）。改成活着时每 0.5 秒缓存「身高 + 脚底偏移」，
    ///    死亡时用缓存推算；再读不到才退到 Collider2D，最后才用原坐标。
    ///  - 老记录的图钉也一起修好了：显示时按当前骑士算一份等量下移量整体平移，
    ///    不用清空统计重记。存档新增第 8 列「落点算法版本」区分新老记录。
    ///
    /// v0.9.13
    ///  - 「Dream Final」补成「梦境」：辐光战的场景资源叫 Dream_Final_Boss，但游戏运行时
    ///    GameManager.sceneName 给的是 Dream_Final，对照表里按资源名填的就查不到，掉回英文。
    ///    除了把 Dream_Final 登记进对照表，还加了一层「下划线前缀兜底」：短于资源名的代号
    ///    只要能唯一归属到某个地名就显示中文，有歧义（Abyss / Deepnest / Fungus3）的宁可显示代号。
    ///  - 底部提示补上「回车确认」：面板里本来就能用回车确认当前项，只是提示里没写。
    ///  - 死亡图钉下移：原来直接用角色 transform.position，落点基本贴着头顶；改成读角色
    ///    SpriteRenderer 包围盒，落在「从脚底往上 1/3」（Display/DeathMarkerHeightRatio 可调）。
    ///
    /// v0.7.0
    ///  - 敌人/死因名字改成中文：用的不是手写对照表，而是游戏官方语言表里的
    ///    猎人日志（Journal）条目，158 种敌人的「名字 / 描述 / 猎人笔记」全部是官方简中。
    ///  - 查表改成「归一化」匹配：枚举名 ZOMBIE_RUNNER、字段名 ZombieRunner、
    ///    物体名 "Zombie Runner (5)" 归一化后是同一个键，所以不会再漏成英文。
    ///  - 物体名末尾的变体序号 " (5)" 会被自动剥掉。
    ///
    /// v0.5.3
    ///  - 场景名显示中文：把《空洞骑士_名称对照表.xlsx》「场景名称」页的填写结果编译进插件
    ///    （438/501 个场景有名字，其余回落到游戏原始代号）。
    ///  - 表里的中文名列是合并单元格，一个名字管一片房间 —— 显示时自动补房间号（真菌荒地·01）。
    ///  - 万一两个房间算出同一个名字，面板会补上原始代号区分，不会出现两行一模一样。
    ///  - 开关：Display/ChineseSceneNames（默认 true，关掉看原始代号）。
    ///
    /// v0.6.1
    ///  - 场景名改成「只用地名」：对照表里一个名字管一片房间（白色宫殿管 19 个），
    ///    显示层不再补「·06」这种房间号，统一显示「白色宫殿」。
    ///  - 右栏场景排行同步按地名合并 —— 不合并会出现十几行一模一样的「白色宫殿」，
    ///    看不出到底在那片区域死了几次。存档里存的仍然是原始 sceneName，只是显示时合并。
    ///  - 想知道具体是哪个房间：看右上角 HUD 第二行的原始代号。
    ///
    /// v0.6.0
    ///  - 右上角常驻「当前房间」条（调试用）：上面一行中文名，下面一行游戏原始场景代号，
    ///    换房间时自动刷新（0.15s 轮询一次 sceneName）。
    ///  - HUD 单独挂一个 Canvas，不挂在统计面板底下 —— 面板关闭时 uiRoot 会整个 SetActive(false)，
    ///    挂在里面就跟着消失，没法边玩边看。sortingOrder 比面板高，开着面板也看得见。
    ///  - 开关：Display/ShowRoomHud（v0.9.21 起默认 false，右上角不再常驻房间名）、
    ///    Display/RoomHudSize（字号）、Display/RoomHudShowCode（是否显示原始代号那一行）。
    ///
    /// v0.5.2
    ///  - 右上角「输出日志 / 注入测试 / 清空」三个按钮默认隐藏（Display/ShowButtons 可放回来），
    ///    回调方法全部保留；另给三者各配一个默认 None 的热键，需要时绑键即可。
    ///  - 底部提示精简成「↑↓ 滚动 · ←→ 换栏 · ESC 关闭」，页码行去掉按键说明。
    ///
    /// v0.5.1
    ///  - 导航改成两层，解决「用 ←→ 切栏时会先经过槽1~槽4」：
    ///      上层 = 槽位标签 + 右侧三个按钮
    ///      栏区 = 左栏（死因）/ 右栏（场景）
    ///    ←→ 只在当前这层左右移动；↑↓ 负责进出这两层。
    ///    打开面板默认站在栏区，所以 ←→ 就是切左/右栏，不再误触槽位。
    ///    栏区里 ↑↓ 是滚动，滚到顶再按 ↑ 退回上层（数据不足一屏时按 ↑ 直接退回）。
    ///  - 表头高亮/指示线只在焦点真的在栏区时才亮，避免顶部选标签时误导。
    ///
    /// v0.5.0
    ///  - 左右两栏改成「各自独立滚动」：↑↓ 只滚当前激活的那一栏，两栏各有各的偏移，
    ///    数据少的那栏不再被另一栏拖着走。←→ 的焦点环里加了「左栏 / 右栏」两项，
    ///    焦点移上去就把 ↑↓ 的目标切过去；鼠标点一下栏里的空白也能切。
    ///    激活栏的表头会变金色、表头下面多一条亮线，一眼看出在滚哪一栏。
    ///  - 顶部新增「注入测试」按钮：一次往当前槽位灌 60 条假记录
    ///    （30 个场景 / 14 种敌人 / 5 种陷阱 / 掉落），专门用来测滚动和排版。
    ///
    /// v0.4.9
    ///  - 修复「只死一次却记了 6 次」：落账点从 HeroController.Die / DieFromHazard
    ///    改成 GameManager.PlayerDead。DieFromHazard 在白宫踩刺时会被反复调用
    ///    （走的是 hazard respawn，英雄并没真死），按它落账必然多记。
    ///    现在 Die 系补丁只负责准备死因描述，计数交给 PlayerDead。
    ///  - 死因优先级改为：敌人（5s 内）> 陷阱（6s 内）> 其他伤害 > 血量兜底。
    ///
    /// v0.4.8
    ///  - 修复「鼠标在屏幕中间闪来闪去」：游戏把系统光标锁在了屏幕中心，
    ///    之前每帧抢着设 visible=true，就和游戏互相覆盖 → 闪烁。
    ///    现在彻底不抢：系统光标一律藏掉，面板里画一个自己的小光标，
    ///    锁定时用鼠标位移量积分移动，没锁时直接跟 Input.mousePosition。
    ///  - 修复「方向键不起作用」的真正原因：v0.4.7 只读了游戏的 LEFT/RIGHT/UP/DOWN 轴，
    ///    但 HK 的键盘走 InControl，这些轴只挂了手柄 → 键盘按了也读不到。
    ///    改为「输入轴 + 物理按键」两路并取，方向键/WASD 一定生效。
    ///  - 打开面板时打印光标锁定状态，焦点移动打印日志，方便定位。
    ///
    /// v0.4.7
    ///  - 修复「面板打开却没有鼠标」：游戏自己每帧都在把 Cursor 藏起来，
    ///    只在开面板那一刻设一次会被覆盖。改为 LateUpdate 每帧抢回来。
    ///  - 新增像游戏菜单一样的键盘选择：←→ 在「全部/槽1-4/输出日志/清空」之间移动焦点，
    ///    焦点落在槽位标签上立刻切换；「输出日志/清空」需要回车确认（清空仍要按两次）。
    ///    焦点用四边细框标出，鼠标点哪儿焦点就跟到哪儿。
    ///  - 横向输入同样跟随游戏键位（LEFT/RIGHT/Horizontal 轴），没有轴才回退 A/D 与 ←→。
    ///
    /// v0.4.6
    ///  - 修复「只死一次却记了 9 次」：游戏在英雄已死状态下会反复调 Die / DieFromHazard
    ///    （白宫尖刺重生循环实测 3 秒一次），1.5 秒时间窗口挡不住。
    ///    改为「死亡闸门」：记完账上闸，直到英雄复活（cState.dead=false 且血量>0）才解锁。
    ///  - wp_saw（白宫电锯）不再被误判成酸水：陷阱名改为「物体名优先，枚举兜底」。
    ///  - 每次落账额外打印死亡间隔，方便一眼看出是不是在循环送死。
    ///
    /// v0.4.5
    ///  - 面板打开时按 ESC 直接关面板，不再弹游戏自己的暂停菜单。
    ///    做法两层：① 自己监听 ESC 关面板；② 面板打开期间把游戏的「暂停开关」补丁掉
    ///    （TogglePauseGame / PauseGameToggle / PauseGameToggleByMenu / CheckForPauseButton，
    ///    运行时自动发现，找不到就跳过），从源头阻止菜单打开。
    ///  - 关面板后留 0.25 秒的暂停免疫窗口，避免同一次 ESC 在我们关完面板后又被游戏吃到。
    ///
    /// v0.4.4
    ///  - 修复「mod 把游戏上下左右键顶掉」：原因是自建了一个常驻 EventSystem + StandaloneInputModule，
    ///    会和游戏自己的 UI 输入系统抢事件。现在完全不碰 EventSystem，按钮点击改为按屏幕坐标命中检测。
    ///  - 滚动键改为跟随游戏自己的 look up / look down：启动时探测输入轴，不再写死 W/S 或方向键。
    ///    探测不到纵向轴时才回退到配置里的按键。
    ///  - 暂停自愈：面板未打开却残留暂停时，每帧自动还原 timeScale，避免游戏卡住不动。
    ///
    /// v0.4.3
    ///  - 修复「被怪打死仍记成尖刺」的真正原因：HK 的 DamageHero 组件自带 hazardType 字段，
    ///    默认值就是 SPIKES(1)，所以怪物伤害传进来的 hazardType 也是 1。
    ///    → 判据改为「攻击者身上有没有 HealthManager」：有血条才是活物，没有才是陷阱触发器。
    ///  - hazardType 降级为「陷阱的细分名」，不再参与分类
    ///  - 前 30 次伤害打印诊断：攻击者名 / hazard 值 / 最终判定
    ///
    /// v0.4.2
    ///  - 修复「被怪打死却记成尖刺」：原来按位置取 __args[3] 当陷阱类型，
    ///    但这个版本该位置是伤害值，怪物打 1 点血正好等于 SPIKES=1。
    ///    现在启动时按参数名定位真正的 hazardType 参数，定位不到就完全不判陷阱。
    ///  - 攻击者也改为按 GameObject 类型定位，不再固定 __args[0]
    ///  - DieFromHazard 改为「找枚举参数」而非取 __args[0]
    ///  - 敌人名归一化：Husk_Hornhead / HUSK_HORNHEAD → Husk Hornhead 再查中文表
    ///
    /// v0.4.1
    ///  - 修复陷阱名解析失败：游戏的 HazardType 在 GlobalEnums 命名空间下，
    ///    原来 FindType 只按完整名找，一直返回 null → 陷阱全显示成「陷阱(1)」。
    ///    现在加了短名兜底遍历（带缓存），并用 Enum.ToObject 转换后取名。
    ///  - 新增 DieFromHazard 专属补丁：直接接收游戏传入的 HazardType 枚举实例，
    ///    踩刺/酸水/岩浆/掉坑不再靠 int 反查，也不会再被误判成「掉落/出界」。
    ///  - 死因优先级改为：陷阱(DieFromHazard) > 最后一击(TakeDamage) > 血量兜底
    ///  - 启动时打印 HazardType 全部枚举值，方便核对
    ///
    /// v0.4.0
    ///  - 修复死因全记成「未知」的根因：Unity 6 版 TakeDamage 第 4 参是 Int32，
    ///    对其调 Enum.GetName 会抛异常并中断整段来源记录（之前日志里的 "Type provided must be an Enum"）
    ///  - enemyType 同样兼容「枚举 / int」两种形态
    ///  - 面板放大到 1680x940，字号与行高同步放大，每栏 20 行
    ///  - 打开面板时暂停游戏（Time.timeScale 归零，关闭或卸载时还原）
    ///  - look up / look down（↑↓ 或 W/S）滚动列表，支持按住连续滚
    ///  - 命中所有 Die*/TakeDamage* 重载，不再只拦第一个
    ///  - 死因、场景各自独立排行（不做两两组合）
    ///  - 按存档槽位分别统计（user1.dat ~ user4.dat 四个槽位），删档同步清除
    ///  - 用游戏同款 Unity UI（Canvas + uGUI）渲染，不再是独立窗口
    ///
    /// 全程反射访问游戏类型，不引用 Assembly-CSharp。
    /// </summary>
    [BepInPlugin(GUID, "YTC_死亡统计mod", VERSION)]
    public class DeathStatsPlugin : BaseUnityPlugin
    {
        public const string GUID = "deathstats.hollowknight";
        public const string VERSION = "0.9.25";
        /// <summary>数据目录 / 配置里统一使用的名字（DLL 同名，都是 ASCII，避免中文路径踩坑）</summary>
        public const string DATA_DIR_NAME = "YTC_DeathStats";

        internal static ManualLogSource L;
        internal static DeathStatsPlugin Instance;

        // ---- 诊断日志（追加写、按天分文件，重启不丢）----
        // BepInEx 的 LogOutput.log 每次启动都会被重写，想知道「这次到底判成了谁」必须当场去看。
        // 所以把几类关键判定行额外追加到 BepInEx\plugins\YTC_DeathStats\diag-yyyyMMdd.log。
        internal static bool sDiagOn = true;
        internal static string sDiagDir;
        internal static string sDiagPath;
        internal static string sDiagDay;
        internal static int sDiagLines;

        // ---- 最后一击缓存（Die 触发时取用）----
        internal static string sLastKiller;
        internal static string sLastDetail;
        internal static float sLastHitAt = -999f;
        internal static float sLastRecordAt = -999f;

        // ---- 陷阱致死单独记录 ----
        // DieFromHazard(HazardType, float) 是踩刺/酸水/岩浆/掉坑的确定性入口，
        // 它的优先级高于 TakeDamage（先被怪打、再掉进刺里，死因应该是刺）。
        internal static string sLastHazardKiller;
        internal static float sLastHazardAt = -999f;

        // TakeDamage 里哪个参数才是 hazardType —— 按运行时参数名定位，
        // 不能靠位置猜：这个版本的第 4 参是伤害值，不是陷阱类型。
        internal static int sHazardArgIndex = -1;
        internal static int sSourceArgIndex = -1;
        internal static int sDiagCount = 0;   // 前 30 次伤害打印诊断，避免刷屏

        // ---- 最近死掉的敌人（用于「爆裂致死」溯源）----
        // 跳虫、爆肚蝠这类怪死的时候会炸开，伤到玩家的是它们留下的「Gas Explosion」特效物体，
        // 这个特效既没有 HealthManager，也没有挂在怪物身上当子物体，光看攻击者永远查不到是谁炸的。
        // 唯一可靠的线索是：炸你的那一瞬间，附近刚死过一个会爆的怪。
        // 所以在这里留一小圈「最近的敌人死亡」记录，爆炸致死时按 时间 + 距离 反查。
        private struct EnemyDeathRec
        {
            public string name;
            public Vector3 pos;
            public float at;
        }
        private static readonly EnemyDeathRec[] sEnemyDeaths = new EnemyDeathRec[12];
        private static int sEnemyDeathHead;
        private static bool sEnemyDeathHooked;
        internal static int sBurstLogs;
        private static int sEnemyDeathLogs;

        // ---- 攻击物生成归属表 ----
        // 远程弹幕、自爆火球这些东西都是「谁放的」才算凶手，光看攻击物本身看不出来。
        // 所以拦 PlayMaker 的 SpawnObject 系列动作：它在 OnEnter 里生成完物体后，
        // 我们立刻把「生成者（FSM 宿主，也就是怪物）」和「生成出来的物体」记成一对。
        // 之后这个物体（或它的子物体）打到玩家时，直接查表拿凶手名，不用猜。
        private struct SpawnRec
        {
            public string owner;
            public float at;
        }
        private static readonly Dictionary<int, SpawnRec> sSpawnOwner = new Dictionary<int, SpawnRec>();

        // 预制体名 → 谁放的。mixed = 这个特效被多种敌人用过；
        // owners 记下所有用过的主人，mixed 时按「场上离生成物最近的那只」定人。
        private struct PrefabRec
        {
            public string owner;
            public List<string> owners;
            public float at;
            public bool mixed;
        }
        private static readonly Dictionary<string, PrefabRec> sPrefabOwner =
            new Dictionary<string, PrefabRec>(StringComparer.OrdinalIgnoreCase);
        private static bool sFsmSpawnHooked;
        private static float sFsmRetryAt;
        private static bool sSpawnFirstLogged;
        private static int sInheritLogs;
        private static string sSpawnFieldDump;      // 首次装钩时列出字段名，字段对不上时靠它定位
        private static bool sSpawnNoTypeLogged;

        // OnUpdate 型动作（SpawnObjectFromGlobalPoolOverTime 这类周期生成）每帧都会调进来，
        // 按动作实例节流，免得一秒几十次反射做同样的事。
        private static readonly Dictionary<int, float> sSpawnTick = new Dictionary<int, float>();

        // 已经打过诊断日志的预制体名（每个只记一次，避免刷屏）
        private static readonly HashSet<string> sSpawnSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 名字推导的诊断串（候选数 / 最近距离 / 最高分），认不出主人时写进日志，避免下一轮瞎猜
        private static string sRootDiag;

        // ---- 伤害物「出生点」账 ----
        // 弹射物被打出来的那一瞬间，它的位置几乎就贴着发射者（怪嘴边 / 头顶 / 身上），
        // 比「被打中的位置」可靠得多：酸液是往下掉的，等它砸到玩家时早就离主人很远了。
        // 拦 DamageHero.OnEnable（每次从池里启用都会触发），记下 名字+出生坐标+时间。
        private struct BirthRec
        {
            public string name;
            public Vector3 pos;
            public float at;
            // 池化物体在 OnEnable 那一刻还停在池位上，要等 FSM 把它 SetPosition 到发射点。
            // 所以记下物体本身，50 毫秒后再采一次真实坐标（resampled 防止反复覆盖）。
            public GameObject go;
            public bool resampled;
        }
        private static readonly Dictionary<int, BirthRec> sBirth = new Dictionary<int, BirthRec>();
        private static bool sBirthHooked;
        private static float sBirthRetryAt;
        private static int sBirthLogs;

        // HK 把自定义动作编进了 Assembly-CSharp（PlayMaker.dll 里没有），按全名直接取最稳。
        // 教训：只拦 SpawnObject* 会漏掉一半怪 —— 阿司匹德猎人的弹（Spitter Shot R）就是
        // 走别的动作出的，一条生成记录都没有。所以把「会产生物体」的动作名尽量列全。
        private static readonly string[] SpawnActionNames =
        {
            "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPool",
            "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolOverTime",
            "HutongGames.PlayMaker.Actions.SpawnObjectFromGlobalPoolOverTimeV",
            "HutongGames.PlayMaker.Actions.SpawnObject",
            "HutongGames.PlayMaker.Actions.SpawnObjectByName",
            "HutongGames.PlayMaker.Actions.SpawnFromPool",
            "HutongGames.PlayMaker.Actions.SpawnFromPoolV",
            "HutongGames.PlayMaker.Actions.CreateObject",
            "HutongGames.PlayMaker.Actions.SpawnRandomObjects",
            "HutongGames.PlayMaker.Actions.SpawnRandomObjectsV",
            "HutongGames.PlayMaker.Actions.SpawnRandomObjectsOverTime",
            "HutongGames.PlayMaker.Actions.SpawnRandomObjectsOverTimeV",
            "HutongGames.PlayMaker.Actions.SpawnRandomObjectsVelocity",
        };

        internal static int sOwnerLogs;
        internal static int sEnemyNameLogs;   // 前 25 次敌人识别打印依据，方便核对
        internal static int sUnknownLogs;     // 前 60 次「认不出来源」打印证据
        internal static readonly HashSet<string> sNameDumped = new HashSet<string>(); // 已验明正身的内部名


        // ---- 数据：按槽位 ----
        private readonly Dictionary<int, SlotStats> slots = new Dictionary<int, SlotStats>();

        // ---- 死亡去重闸门 ----
        // 一次死亡只记一次。游戏在「已经死着」的状态下会反复调 Die / DieFromHazard
        // （白宫的尖刺重生循环尤其明显，实测 3 秒一次），只靠时间窗口挡不住。
        // 这里改成：记完账就上闸，直到英雄重新活过来（cState.dead=false 且血量>0）才解锁。
        private static bool sDeathLatched;
        private static float sDeathLatchedAt;
        private static float sLastDeathAt = -999f;
        private static int sDupDeathLogs;

        // ---- 落账权威入口 ----
        // 只有 GameManager.PlayerDead 能保证「一次真死只来一次」。
        // HeroController.Die / DieFromHazard 在白宫里会被反复调用（踩刺后 hazard respawn
        // 走的就是这条路），按它们落账会出现「只死一次却记了 6 次」。
        // 所以：PlayerDead 补丁成功 = 只有它落账，Die 系只负责准备死因描述。
        private static bool sPlayerDeadHooked;

        // 场景名是否显示中文（由《空洞骑士_名称对照表》生成），关掉就显示游戏原始代号
        internal static bool sUseCnSceneNames = true;

        private ConfigEntry<KeyboardShortcut> keyToggle;
        private ConfigEntry<KeyboardShortcut> keyScrollUp;
        private ConfigEntry<KeyboardShortcut> keyScrollDown;
        private ConfigEntry<KeyboardShortcut> keyDump;
        private ConfigEntry<KeyboardShortcut> keyInject;
        private ConfigEntry<KeyboardShortcut> keyClear;
        private ConfigEntry<bool> cfgShowHud;
        private ConfigEntry<bool> cfgShowButtons;
        private ConfigEntry<bool> cfgPauseGame;
        private ConfigEntry<float> cfgCursorSpeed;
        private ConfigEntry<bool> cfgRoomHud;
        private ConfigEntry<float> cfgRoomHudSize;
        private ConfigEntry<bool> cfgRoomHudCode;
        private ConfigEntry<bool> cfgShowMarkers;
        private ConfigEntry<float> cfgMarkerScale;
        private ConfigEntry<int> cfgMarkerOrder;
        private ConfigEntry<float> cfgMarkerHeightRatio;
        private ConfigEntry<bool> cfgDiagLog;
        private Harmony harmony;

        // ---- 死亡位置标记（场景里的红点）----
        private static readonly List<GameObject> sMarkers = new List<GameObject>();
        private Sprite markerSprite;
        private string markerScene;        // 当前已摆放标记的场景，切场景时重建
        private float markerNextPoll;
        private float markerScaleApplied = -1f;
        private Text markerToggleLabel;
        private ClickTarget ctMarkerToggle;

        // ---- 骑士身体尺寸缓存 ----
        // 死亡瞬间骑士本体（SpriteRenderer）会被关掉换成尸体特效，这时候读包围盒拿到的是 0，
        // 落点就只能退回原坐标 —— 表现就是「图钉还在头顶」。所以活着的时候每 0.5 秒采一次
        // 「身高 + 脚底相对 transform 的偏移」，死亡时用最近一次的值推算。
        private static float sHeroHeight;
        private static float sHeroFootOffset;   // bounds.min.y - transform.position.y
        private float heroSampleNext;

        // ---- 自绘光标 ----
        private Vector2 cursorPos;
        private bool cursorInit;
        private RectTransform cursorDot;
        private RectTransform cursorEdge;

        // 悬停：命中项 + 上一帧光标位置（用来判断"是不是人在动鼠标"）
        private ClickTarget hoverTarget;
        private Vector2 lastHoverPos;
        private bool cursorModeLogged;

        private string statsDir;
        private float nextSyncTime;
        private bool uiVisible;
        private bool uiDirty = true;
        private bool fallbackWarned;
        private int viewSlot;          // 0 = 全部，1..4 = 指定槽位

        // ---- 滚动 / 暂停 ----
        private const int VISIBLE_ROWS = 20;
        // 左右两栏各自独立的滚动偏移：死因栏和场景栏行数不一样，
        // 强行共用一个偏移会让数据少的那栏提前卡住，看起来像「两栏没对齐」。
        private int scrollLeft;        // 左栏：死因排行
        private int scrollRight;       // 右栏：场景排行
        private int maxScrollLeft;
        private int maxScrollRight;
        private int activeCol;         // 当前 ↑↓ 作用在哪一栏：0=左（死因）1=右（场景）
        private float scrollRepeatAt;

        // ---- 两层导航 ----
        // 0 = 上层（槽位标签 + 按钮），1 = 栏区（左/右统计栏）
        // focusItems 组成：[0, topItemCount) 是上层项，[topItemCount, ...) 是两栏
        private int focusRow;
        private int topItemCount;
        private int topFocusMemory;    // 从栏区退回上层时，回到上次待的那个顶部项

        // 表头 + 激活栏底下的指示线，跟着 activeCol 变亮，让人知道 ↑↓ 现在滚的是哪一栏
        private readonly Text[] hKiller = new Text[3];
        private readonly Text[] hScene = new Text[3];
        private Image underLeft;
        private Image underRight;
        private float savedTimeScale = 1f;
        private bool pausedByMod;
        private CursorLockMode savedLockMode;
        private bool savedCursorVisible;

        // ---- ESC 拦截 ----
        // 面板关掉之后的短暂窗口里，继续阻止游戏打开暂停菜单。
        // 因为游戏和我们的 Update 谁先执行不固定：万一我们先把面板关了、
        // 游戏再处理同一次 ESC，就会顺手弹出菜单（正是要避免的情况）。
        private float blockPauseUntil;
        private static int sPauseBlockLogs;
        private static readonly List<string> sPausedPatched = new List<string>();

        // ---- UI 引用 ----
        private GameObject uiRoot;
        private RectTransform uiPanel;
        private Text txtSubtitle;
        private readonly List<GameObject> dynamicRows = new List<GameObject>();
        private readonly Image[] tabBgs = new Image[5];
        private readonly Text[] tabLabels = new Text[5];
        private Text txtClear;
        private string pendingClear;
        private Font uiFont;

        // ---- 房间名 HUD ----
        // 挂在独立 Canvas 上，和统计面板互不影响：面板关掉 uiRoot 会 SetActive(false)，
        // 这条要一直留在屏幕上，所以不能放进 uiRoot。
        private GameObject hudRoot;
        private RectTransform hudBox;
        private Image hudBg;
        private Text hudNameText;
        private Text hudCodeText;
        private string hudLastScene;
        private float hudNextPoll;
        private float hudAppliedSize = -1f;
        private bool hudAppliedShowCode = true;

        /// <summary>
        /// 面板里可点击的区域。不用 Unity 的 EventSystem / StandaloneInputModule，
        /// 因为自建 EventSystem 会和游戏自己的输入系统抢事件，导致方向键等按键失灵。
        /// 这里自己按屏幕坐标命中检测，对游戏输入零干扰。
        /// </summary>
        private class ClickTarget
        {
            public RectTransform rt;
            public Action action;
            public int tabIndex = -1;   // >= 0 表示这是槽位标签页：焦点移上去就切换
            public int colIndex = -1;   // >= 0 表示这是排行栏：焦点移上去就把滚动目标切到这一栏

            // ---- 悬停 / 选中高亮 ----
            // bg 是按钮底板。baseColor 是个委托而不是固定色值：槽位标签的底色会随
            // 「当前选中哪个槽」变化，如果缓存固定色，RefreshUI 一改色就会打架。
            // 每帧现算基础色再叠悬停提亮，永远是对的。
            public Image bg;
            public Func<Color> baseColor;
            public Text label;
            public Func<Color> labelColor;

            // 描边开关（死亡标记）专用：edge 是最外面那圈描边，bg 是里面的填充。
            // 普通按钮这两项留空，只有开关按钮才用。
            public Image edge;
            public Func<Color> edgeColor;
        }
        private readonly List<ClickTarget> clickTargets = new List<ClickTarget>();

        // ---- 键盘选择（像游戏自己的菜单那样）----
        private readonly List<ClickTarget> focusItems = new List<ClickTarget>();
        private int focusIndex;
        private float focusRepeatAt;
        private static int sFocusLogs;
        private readonly Image[] focusEdges = new Image[4];

        // 两种状态必须长得不一样，否则用户分不清「当前看的是哪个槽」和「键盘选到了哪」：
        //   焦点项  = 实心金底 + 黑字（最强，像游戏菜单里的选中项）
        //   当前槽位（未获焦点时）= 暗金底 + 金字 + 底部金条
        private static readonly Color FocusBgGold = new Color(0.74f, 0.61f, 0.28f);
        private static readonly Color FocusTextDark = new Color(0.05f, 0.05f, 0.06f);
        // 当前槽位只是"你在看哪个槽"，不该和"键盘选中"抢注意力。
        // 0.33 那一版在实心金焦点旁边几乎一样亮，两个金色块挨着看就像同一个坏掉的按钮。
        private static readonly Color SlotBgIdle = new Color(0.22f, 0.19f, 0.1f);
        private static readonly Color SlotTextIdle = new Color(0.94f, 0.82f, 0.5f);
        private static readonly Color SlotBgOff = new Color(0.1f, 0.11f, 0.12f);
        private static readonly Color SlotTextOff = new Color(0.8f, 0.82f, 0.85f);

        // ---- 描边开关（死亡标记）----
        // 实心金底是"键盘焦点"的专属语义，开关不能也用金块，否则一行里出现两块金色。
        // 开关改用：深色填充 + 一圈状态色描边（开=金，关=灰）。
        private static readonly Color SwitchFillIdle = new Color(0.1f, 0.11f, 0.12f);
        private static readonly Color SwitchEdgeOn = new Color(0.82f, 0.69f, 0.34f, 0.95f);
        private static readonly Color SwitchEdgeOff = new Color(0.32f, 0.33f, 0.35f, 0.95f);
        private static readonly Color SwitchTextOn = new Color(0.94f, 0.82f, 0.5f);
        private static readonly Color SwitchTextOff = new Color(0.76f, 0.78f, 0.81f);
        private readonly Image[] tabUnder = new Image[5];

        private ConfigEntry<KeyboardShortcut> keyConfirm;
        private string lookHorizontalAxis;
        private string leftAxis;
        private string rightAxis;
        private bool horizAxisReady;

        // ---- 滚动输入源 ----
        // 优先读游戏自己的 look up / look down，读不到才退回配置里的按键
        private string lookUpAxis;
        private string lookDownAxis;
        private string lookVerticalAxis;
        private bool lookAxisReady;

        // ================= 生命周期 =================

        private void Awake()
        {
            L = Logger;
            Instance = this;

            keyToggle = Config.Bind("Hotkeys", "TogglePanel", new KeyboardShortcut(KeyCode.F4), "开关死亡统计面板");
            keyScrollUp = Config.Bind("Hotkeys", "ScrollUp", new KeyboardShortcut(KeyCode.UpArrow), "仅在检测不到游戏纵向输入轴时才用：面板内向上滚动");
            keyScrollDown = Config.Bind("Hotkeys", "ScrollDown", new KeyboardShortcut(KeyCode.DownArrow), "仅在检测不到游戏纵向输入轴时才用：面板内向下滚动");
            keyConfirm = Config.Bind("Hotkeys", "Confirm", new KeyboardShortcut(KeyCode.Return), "面板内确认当前选中项（左右选择，回车确认）");

            // 右上角三个功能按钮默认隐藏，但回调全部保留。
            // 想再用的时候有两条路：把 Display/ShowButtons 改成 true 让按钮回来，
            // 或者给下面三个热键绑键（None = 不启用），面板打开时生效。
            keyDump = Config.Bind("Hotkeys", "DumpToLog", new KeyboardShortcut(KeyCode.None),
                "面板内把完整排行写进 LogOutput.log（None = 不绑定）");
            keyInject = Config.Bind("Hotkeys", "InjectTestData", new KeyboardShortcut(KeyCode.None),
                "面板内往当前槽位灌 60 条测试数据（None = 不绑定）");
            keyClear = Config.Bind("Hotkeys", "ClearData", new KeyboardShortcut(KeyCode.None),
                "面板内清空当前视图数据，按两次确认（None = 不绑定）");

            cfgShowHud = Config.Bind("Display", "ShowHint", true, "面板底部显示操作提示");
            cfgShowButtons = Config.Bind("Display", "ShowButtons", false,
                "显示右上角的「输出日志 / 注入测试 / 清空」三个按钮（默认隐藏，功能逻辑仍然保留）");
            cfgPauseGame = Config.Bind("Display", "PauseOnOpen", true, "打开面板时暂停游戏（关闭后恢复）");
            cfgCursorSpeed = Config.Bind("Display", "CursorSpeed", 10f,
                "面板里鼠标移动速度。游戏把系统光标锁在屏幕中心时，只能靠鼠标位移量换算（1 像素约 0.1 单位，所以 10 ≈ 1:1），手感不合适调这个值");

            // 右上角房间名 HUD（调试用）。默认关掉，需要时改配置文件打开。
            cfgRoomHud = Config.Bind("Display", "ShowRoomHud", false,
                "右上角实时显示当前房间（中文名 + 原始场景代号），调试用。false = 关掉（默认）");
            cfgRoomHudSize = Config.Bind("Display", "RoomHudSize", 22f,
                "房间名 HUD 的中文字号（原始代号那行会按它自动缩到 0.72 倍）");
            cfgRoomHudCode = Config.Bind("Display", "RoomHudShowCode", true,
                "房间名 HUD 下面再显示一行游戏原始场景代号（White_Palace_06 这种）");

            cfgShowMarkers = Config.Bind("Display", "ShowDeathMarkers", true,
                "在死亡位置立一个红色标记（只显示当前房间的）。面板里可以随时开关");
            cfgMarkerScale = Config.Bind("Display", "DeathMarkerScale", 1f,
                "死亡标记的大小倍率，1 = 约一个玩家身高");
            cfgMarkerOrder = Config.Bind("Display", "DeathMarkerOrderOffset", 0,
                "标记的渲染层级偏移。默认 0 表示沿用玩家所在层、靠 z 轴排在玩家后面；"
                + "如果发现标记被地板或前景挡住，试着调成 -1 或 -2");
            cfgMarkerHeightRatio = Config.Bind("Display", "DeathMarkerHeightRatio", 0.33f,
                "图钉落在角色身体的高度比例：0 = 脚底，0.5 = 腰，1 = 头顶。"
                + "默认 0.33 落在「从下往上三分之一」，比贴在头顶看得清是谁死在哪");
            cfgDiagLog = Config.Bind("Logging", "AppendDiagLog", true,
                "把死因判定的过程追加写到 BepInEx\\plugins\\YTC_DeathStats\\diag-yyyyMMdd.log（按天分文件、"
                + "不覆盖，重启游戏也还在）。一次游戏几百行；排查死因或怪物名不对时看这个文件。"
                + "false = 只写 BepInEx 自己的 LogOutput.log（每次启动会被重写）");

            ProbeLookAxes();

            statsDir = Path.Combine(Paths.PluginPath, DATA_DIR_NAME);
            try
            {
                Directory.CreateDirectory(statsDir);
                MigrateOldDataDir();
            }
            catch (Exception e) { L.LogWarning("[死亡统计] 创建数据目录失败: " + e.Message); }

            // 诊断日志落在数据目录里，和 slotN.tsv 放一起，找的时候在同一个文件夹
            sDiagDir = statsDir;
            sDiagOn = cfgDiagLog != null ? cfgDiagLog.Value : true;
            sDiagPath = null;
            sDiagDay = null;
            sDiagLines = 0;

            // 订阅自己的日志：之后所有命中关键词的判定行都会追加进 diag 文件
            try { L.LogEvent += OnOwnLog; } catch { }
            DiagLine("===== 启动 v" + VERSION + " | Unity " + Application.unityVersion
                + " | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");

            LoadAllSlots();
            SyncDeletedSaves(true);

            int cur = CurrentSlot();
            viewSlot = cur > 0 ? cur : 0;

            CreateUI();

            L.LogInfo("[死亡统计] 插件已加载 v" + VERSION + " | 当前槽位=" + (cur > 0 ? cur.ToString() : "未识别")
                + " | 已载入槽位 [" + string.Join(",", SlotKeys()) + "]");

            try
            {
                harmony = new Harmony(GUID);
                PatchAll();
            }
            catch (Exception e) { L.LogError("[死亡统计] 补丁初始化失败: " + e); }
        }

        private void OnDestroy()
        {
            // 插件被卸载（关游戏 / 场景重置）时务必把时间流速还原，否则游戏会卡在暂停
            try { if (pausedByMod) Time.timeScale = savedTimeScale <= 0.001f ? 1f : savedTimeScale; } catch { }
            pausedByMod = false;
            try { if (harmony != null) harmony.UnpatchSelf(); } catch { }
        }

        private string SlotKeys()
        {
            List<string> ks = new List<string>();
            foreach (KeyValuePair<int, SlotStats> kv in slots)
                if (kv.Value.Records.Count > 0) ks.Add(kv.Key + ":" + kv.Value.Records.Count);
            return string.Join(" ", ks.ToArray());
        }

        private void Update()
        {
            // ESC 优先处理：面板开着就关面板，不让游戏弹出暂停菜单。
            // 游戏那侧的暂停入口另有补丁挡着（见 Prefix_BlockGamePause）。
            if (uiVisible && Input.GetKeyDown(KeyCode.Escape))
            {
                // 先挂免疫窗口再关面板：即便游戏这一帧晚于我们处理本次 ESC，也弹不出菜单
                blockPauseUntil = Time.realtimeSinceStartup + 0.25f;
                SetPanelVisible(false);
                return;
            }

            if (keyToggle.Value.IsDown()) SetPanelVisible(!uiVisible);

            TickDeathLatch();
            SampleHeroBounds();
            RefreshBirthPositions();
            WatchBurstEnemies();
            UpdateRoomHud();
            UpdateMarkers();

            // PlayMaker 程序集要是比插件晚加载，生成归属补丁会第一把没挂上，这里补挂。
            // 只在前 60 秒内每 2 秒试一次，之后不再折腾。
            if (!sFsmSpawnHooked && Time.realtimeSinceStartup >= sFsmRetryAt && Time.realtimeSinceStartup < 60f)
            {
                sFsmRetryAt = Time.realtimeSinceStartup + 2f;
                try { TryHookFsmSpawns(); } catch { }
            }

            // 出生点补丁同理：DamageHero 可能还没加载，失败就在前 60 秒内补挂。
            if (!sBirthHooked && Time.realtimeSinceStartup >= sBirthRetryAt && Time.realtimeSinceStartup < 60f)
            {
                sBirthRetryAt = Time.realtimeSinceStartup + 2f;
                try { TryHookHazardBirth(); } catch { }
            }

            // 自愈：面板没开却还挂着我暂停过的状态，立刻还原。
            // 任何异常路径导致没走到关闭逻辑时，游戏不会卡在暂停（表现就是方向键没反应）。
            if (!uiVisible && pausedByMod) ApplyPause(false);

            if (Time.realtimeSinceStartup >= nextSyncTime)
            {
                nextSyncTime = Time.realtimeSinceStartup + 5f;
                SyncDeletedSaves(false);
            }

            if (uiVisible)
            {
                for (int i = 1; i <= 4; i++)
                {
                    if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha0 + i)))
                    {
                        viewSlot = i; ResetScroll(); uiDirty = true;
                        SyncFocusToSlot(true);
                    }
                }
                if (Input.GetKeyDown(KeyCode.Alpha0)) { viewSlot = 0; ResetScroll(); uiDirty = true; SyncFocusToSlot(true); }

                // 三个隐藏功能的可选热键（默认 None，不占用任何键）
                if (keyDump.Value.MainKey != KeyCode.None && keyDump.Value.IsDown()) DumpToLog();
                if (keyInject.Value.MainKey != KeyCode.None && keyInject.Value.IsDown()) InjectTestData();
                if (keyClear.Value.MainKey != KeyCode.None && keyClear.Value.IsDown()) OnClearClicked();

                HandleNavKeys();
                UpdateVirtualCursor();
                UpdateHover();
                HandleClicks();
            }

            if (uiDirty && uiVisible) { uiDirty = false; RefreshUI(); }
        }

        private void LateUpdate()
        {
            // 面板开着就把系统光标按住不动（游戏每帧会把它翻出来，翻出来就会在屏幕中间闪）
            if (!uiVisible) return;
            try { if (Cursor.visible) Cursor.visible = false; } catch { }
        }

        /// <summary>开关面板，同时处理暂停与恢复</summary>
        private void SetPanelVisible(bool show)
        {
            uiVisible = show;
            pendingClear = null;
            if (txtClear != null) txtClear.text = "清空";

            if (show)
            {
                int cur = CurrentSlot();
                if (cur > 0) viewSlot = cur;
                ResetScroll();
                uiDirty = true;
                SyncFocusToSlot();   // 先让顶部焦点落在当前槽位上，记住位置

                // 进来直接站在栏区：←→ 就是切换左栏/右栏，不用先穿过 5 个存档标签。
                // 想选标签或点按钮时，连按 ↑ 滚到顶再按一下就会退回上层。
                focusRow = 1;
                focusIndex = Mathf.Clamp(topItemCount + activeCol, 0, Mathf.Max(0, focusItems.Count - 1));

                if (uiRoot != null) uiRoot.SetActive(true);
                ApplyPause(true);
                SetCursorForPanel(true);
                UpdateFocusFrame();

                L.LogInfo("[死亡统计] 打开面板 | 光标锁=" + Cursor.lockState + " 可见=" + Cursor.visible
                    + " | 屏幕=" + Screen.width + "x" + Screen.height
                    + " | 可选项=" + focusItems.Count);
            }
            else
            {
                if (uiRoot != null) uiRoot.SetActive(false);
                ApplyPause(false);
                SetCursorForPanel(false);
                for (int i = 0; i < focusEdges.Length; i++)
                    if (focusEdges[i] != null) focusEdges[i].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 不再和游戏抢系统光标：游戏每帧都把 Cursor.visible 设成 false，甚至把
        /// lockState 锁在屏幕中心（就是"鼠标一直闪在中间"的由来）。
        /// 现在统一把系统光标藏掉，在面板里画一个自己的小光标，位置由我们自己算：
        /// 光标被锁住时用鼠标位移量积分，没被锁时直接跟 Input.mousePosition。
        /// </summary>
        private void SetCursorForPanel(bool show)
        {
            try
            {
                if (show)
                {
                    savedLockMode = Cursor.lockState;
                    savedCursorVisible = Cursor.visible;
                    Cursor.visible = false;      // 系统光标交给我们自己画的这个
                    cursorInit = false;

                    // 试着让游戏把光标放开：一旦 lockState 不再是 Locked，
                    // Input.mousePosition 就恢复成真实屏幕坐标，鼠标指哪打哪，
                    // 不会像积分那样越移越偏。游戏要是又锁回去，UpdateVirtualCursor
                    // 会自己退回积分模式，所以这步失败也不会有副作用。
                    try { if (Cursor.lockState == CursorLockMode.Locked) Cursor.lockState = CursorLockMode.None; }
                    catch { }
                    cursorModeLogged = false;   // 下次打开面板再报一次当前模式
                }
                else
                {
                    Cursor.lockState = savedLockMode;
                    Cursor.visible = savedCursorVisible;
                }
            }
            catch { }
        }

        /// <summary>每帧算光标位置并把自绘光标挪过去（只在面板打开时）</summary>
        private void UpdateVirtualCursor()
        {
            if (!uiVisible) return;

            try { if (Cursor.visible) Cursor.visible = false; } catch { }

            if (!cursorInit)
            {
                cursorPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                cursorInit = true;
            }

            // lockState == Locked 时系统光标被钉在屏幕正中，Input.mousePosition 完全不动，
            // 只能靠 GetAxisRaw("Mouse X/Y") 的位移量自己积分。
            bool locked = false;
            try { locked = Cursor.lockState == CursorLockMode.Locked; } catch { }

            // 只记一次：锁定=靠位移积分（可能有累积偏差），自由=绝对坐标（指哪打哪）。
            // 以后排查"鼠标位置不对"直接看这行日志。
            if (!cursorModeLogged)
            {
                cursorModeLogged = true;
                if (L != null)
                    L.LogInfo("[死亡统计] 光标模式=" + (locked ? "锁定·靠位移积分" : "自由·绝对坐标")
                        + " | mousePos=" + (int)Input.mousePosition.x + "," + (int)Input.mousePosition.y);
            }

            if (locked)
            {
                float sens = cfgCursorSpeed.Value;
                try
                {
                    cursorPos.x += Input.GetAxisRaw("Mouse X") * sens;
                    cursorPos.y += Input.GetAxisRaw("Mouse Y") * sens;
                }
                catch
                {
                    // 没有 Mouse X/Y 轴就退回系统坐标，光标至少还能跟着鼠标走
                    cursorPos = Input.mousePosition;
                }
            }
            else
            {
                cursorPos = Input.mousePosition;
            }

            cursorPos.x = Mathf.Clamp(cursorPos.x, 0f, Screen.width);
            cursorPos.y = Mathf.Clamp(cursorPos.y, 0f, Screen.height);

            if (cursorDot != null)
            {
                // 屏幕坐标 → Canvas 本地坐标（Overlay 画布传 null camera）
                Vector2 local;
                RectTransform canvasRt = uiRoot != null ? uiRoot.GetComponent<RectTransform>() : null;
                if (canvasRt != null
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, cursorPos, null, out local))
                {
                    cursorDot.anchoredPosition = local;
                    if (cursorEdge != null) cursorEdge.anchoredPosition = local;
                }
            }
        }

        /// <summary>
        /// 悬停反馈：光标压在哪一项上，那一项底色就提亮，光标也跟着放大一圈。
        /// 并且只有「人真在动鼠标」时才把键盘焦点一并挪过去——否则光标停在按钮上，
        /// 用户按方向键永远会被拉回去，键盘导航就废了。
        /// </summary>
        private void UpdateHover()
        {
            if (!uiVisible || cursorDot == null) return;

            bool mouseMoved = (cursorPos - lastHoverPos).sqrMagnitude > 1f;
            lastHoverPos = cursorPos;

            // 最上层命中（clickTargets 是后加的后判，所以倒着找）
            ClickTarget hit = null;
            for (int i = clickTargets.Count - 1; i >= 0; i--)
            {
                ClickTarget t = clickTargets[i];
                if (t == null || t.rt == null || !t.rt.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(t.rt, cursorPos, null)) continue;
                hit = t;
                break;
            }

            // 三种状态分开画：
            //   键盘焦点  → 实心金底 + 黑字（"我现在选中了这个，按回车生效"）
            //   鼠标悬停  → 底色提亮一档
            //   普通/当前槽 → 各自的常色
            bool hasFocus = focusItems.Count > 0 && focusIndex >= 0 && focusIndex < focusItems.Count;
            ClickTarget focused = hasFocus ? focusItems[focusIndex] : null;

            for (int i = 0; i < clickTargets.Count; i++)
            {
                ClickTarget t = clickTargets[i];
                if (t == null || t.bg == null || t.baseColor == null) continue;

                Color baseC = t.baseColor();
                Color baseL = t.labelColor != null ? t.labelColor() : Color.white;
                Color c, lc;

                if (t == focused)
                {
                    c = FocusBgGold;
                    lc = FocusTextDark;
                }
                else if (t == hit)
                {
                    c = new Color(
                        Mathf.Min(1f, baseC.r * 1.75f + 0.11f),
                        Mathf.Min(1f, baseC.g * 1.75f + 0.11f),
                        Mathf.Min(1f, baseC.b * 1.60f + 0.07f),
                        Mathf.Max(baseC.a, 0.95f));
                    lc = new Color(
                        Mathf.Min(1f, baseL.r + 0.12f),
                        Mathf.Min(1f, baseL.g + 0.12f),
                        Mathf.Min(1f, baseL.b + 0.12f),
                        baseL.a);
                }
                else
                {
                    c = baseC;
                    lc = baseL;
                }

                if (t.bg.color != c) t.bg.color = c;
                if (t.label != null && t.label.color != lc) t.label.color = lc;

                // 描边开关的边框：焦点时并进金底（让白框单独负责"选中"这件事），
                // 悬停时提亮一档，平时就是开/关各自的状态色。
                if (t.edge != null)
                {
                    Color ec = t.edgeColor != null ? t.edgeColor() : Color.white;
                    if (t == focused)
                    {
                        ec = FocusBgGold;
                    }
                    else if (t == hit)
                    {
                        ec = new Color(
                            Mathf.Min(1f, ec.r + 0.22f),
                            Mathf.Min(1f, ec.g + 0.20f),
                            Mathf.Min(1f, ec.b + 0.16f),
                            Mathf.Max(ec.a, 0.95f));
                    }
                    if (t.edge.color != ec) t.edge.color = ec;
                }
            }

            // 光标压在可点项上时放大 1.5 倍，一眼就知道"鼠标到底在哪"
            bool onButton = hit != null && hit.baseColor != null;
            float s = onButton ? 1.5f : 1f;
            if (!Mathf.Approximately(cursorDot.localScale.x, s))
            {
                cursorDot.localScale = new Vector3(s, s, 1f);
                if (cursorEdge != null) cursorEdge.localScale = new Vector3(s, s, 1f);
            }

            // 鼠标移动时才同步焦点，键盘操作优先
            if (mouseMoved && onButton && hit != hoverTarget)
            {
                for (int k = 0; k < focusItems.Count; k++)
                {
                    if (focusItems[k] != hit) continue;
                    focusIndex = k;
                    if (hit.colIndex >= 0) { focusRow = 1; activeCol = hit.colIndex; }
                    else { focusRow = 0; topFocusMemory = k; }
                    break;
                }
            }

            hoverTarget = hit;
        }

        /// <summary>面板打开期间把游戏时间停住；关闭时还原原值（不覆盖游戏自身的暂停）</summary>
        private void ApplyPause(bool pause)
        {
            if (!cfgPauseGame.Value) return;
            try
            {
                if (pause)
                {
                    if (!pausedByMod && Time.timeScale > 0.001f)
                    {
                        savedTimeScale = Time.timeScale;
                        Time.timeScale = 0f;
                        pausedByMod = true;
                    }
                }
                else if (pausedByMod)
                {
                    Time.timeScale = savedTimeScale <= 0.001f ? 1f : savedTimeScale;
                    pausedByMod = false;
                }
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 暂停切换失败: " + e.Message);
            }
        }

        /// <summary>两栏滚动位置一起归零（切槽位、清空、重开面板时用）</summary>
        private void ResetScroll()
        {
            scrollLeft = 0;
            scrollRight = 0;
        }

        /// <summary>
        /// 二维导航。界面被当成两层：
        ///   上层 = 槽位标签页 + 右侧三个按钮（←→ 在这一层左右走）
        ///   下层 = 左栏（死因）/ 右栏（场景）（←→ 在这两栏之间切，↑↓ 滚动当前栏）
        /// ↑↓ 负责进出这两层。上层按 ↓ 进入栏区；栏区滚到顶再按 ↑ 退回上层，
        /// 所以不会出现「进去以后出不来」。（数据不足一屏、没得滚时，↑ 直接退回上层。）
        /// 输入统一「游戏输入轴 + 物理按键」两路并取：HK 键盘走 InControl，
        /// InputManager 里的轴基本只挂了手柄，只读轴会漏。
        /// </summary>
        private void HandleNavKeys()
        {
            if (focusItems.Count == 0) return;

            // ---- 上下 ----
            bool up = lookAxisReady && ReadLookAxis(true) > 0.5f;
            bool down = lookAxisReady && ReadLookAxis(false) > 0.5f;
            if (Input.GetKey(keyScrollUp.Value.MainKey) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) up = true;
            if (Input.GetKey(keyScrollDown.Value.MainKey) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) down = true;

            // ---- 左右 ----
            int hdir = 0;
            if (horizAxisReady)
            {
                if (ReadHorizAxis(true) > 0.5f) hdir = 1;
                else if (ReadHorizAxis(false) > 0.5f) hdir = -1;
            }
            bool hl = Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
            bool hr = Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
            if (hr && !hl) hdir = 1;
            else if (hl && !hr) hdir = -1;

            if (hdir == 0) focusRepeatAt = 0f;
            else
            {
                float now = Time.unscaledTime;
                bool fresh = focusRepeatAt <= 0f;
                if (fresh || now >= focusRepeatAt)
                {
                    focusRepeatAt = now + (fresh ? 0.28f : 0.13f);
                    MoveFocusH(hdir);
                }
            }

            if (!up && !down) scrollRepeatAt = 0f;
            else
            {
                float now = Time.unscaledTime;
                bool fresh = scrollRepeatAt <= 0f;
                if (fresh || now >= scrollRepeatAt)
                {
                    scrollRepeatAt = now + (fresh ? 0.3f : 0.07f);
                    HandleVertical(up && !down);
                }
            }

            // 回车 / 小键盘回车 / 空格都算确认。keyConfirm 走配置，万一配置文件被改坏了
            // 也能靠后面几个兜底键把按钮点下去。
            if (keyConfirm.Value.IsDown() || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                ActivateFocus();
        }

        /// <summary>↑↓ 的实际动作：在上层/栏区之间进出，或滚动当前栏</summary>
        private void HandleVertical(bool up)
        {
            if (focusRow == 0)
            {
                // 上层按 ↓：落进栏区，停在当前那一栏
                if (!up) EnterColumnRow();
                return;
            }

            int cur = activeCol == 0 ? scrollLeft : scrollRight;
            int max = Mathf.Max(0, activeCol == 0 ? maxScrollLeft : maxScrollRight);
            int next = Mathf.Clamp(cur + (up ? -1 : 1), 0, max);

            if (next != cur)
            {
                if (activeCol == 0) scrollLeft = next; else scrollRight = next;
                uiDirty = true;
                return;
            }

            // 已经滚不动了。向上就退回上层（数据不足一屏时等于按一下 ↑ 就回去），
            // 向下到底则停住 —— 用户还想往下看，不该把他弹走。
            if (up) ExitColumnRow();
        }

        private void EnterColumnRow()
        {
            focusRow = 1;
            focusIndex = Mathf.Clamp(topItemCount + activeCol, 0, focusItems.Count - 1);
            scrollRepeatAt = Time.unscaledTime + 0.25f;   // 别一进去就立刻滚一格
            focusRepeatAt = 0f;
            uiDirty = true;
            LogFocus("进入栏区");
        }

        private void ExitColumnRow()
        {
            focusRow = 0;
            focusIndex = Mathf.Clamp(topFocusMemory, 0, Mathf.Max(0, topItemCount - 1));
            scrollRepeatAt = 0f;
            focusRepeatAt = 0f;
            uiDirty = true;
            LogFocus("退回上层");
        }

        private void LogFocus(string why)
        {
            if (sFocusLogs >= 25 || L == null) return;
            sFocusLogs++;
            L.LogInfo("[死亡统计] 焦点 → " + why + " | 层=" + (focusRow == 0 ? "上" : "栏")
                + " | 索引=" + focusIndex + " | 栏=" + (activeCol == 0 ? "左(死因)" : "右(场景)"));
        }

        /// <summary>
        /// ←→ 只在「当前这一层」里左右移动：
        ///   上层 = 全部 / 槽1..4 / 输出日志 / 注入测试 / 清空（首尾循环）
        ///   栏区 = 左栏（死因）↔ 右栏（场景）
        /// 焦点落到槽位标签上直接切槽位；按钮要回车确认（清空仍按两次）。
        /// </summary>
        private void MoveFocusH(int dir)
        {
            if (focusRow == 0)
            {
                int n = topItemCount;
                if (n <= 0) return;
                focusIndex = ((focusIndex + dir) % n + n) % n;
                topFocusMemory = focusIndex;

                // 焦点落在槽位标签页上：立刻切过去（选槽位是最常见的操作，不该还要按确认）
                ClickTarget ct = focusItems[focusIndex];
                if (ct != null && ct.tabIndex >= 0)
                {
                    int before = viewSlot;
                    viewSlot = ct.tabIndex;
                    if (viewSlot != before) { ResetScroll(); uiDirty = true; }
                }
            }
            else
            {
                int n = focusItems.Count - topItemCount;
                if (n <= 0) return;
                activeCol = ((activeCol + dir) % n + n) % n;
                focusIndex = topItemCount + activeCol;
            }

            uiDirty = true;
            LogFocus(dir > 0 ? "右移" : "左移");
        }

        private void ActivateFocus()
        {
            if (focusItems.Count == 0) return;
            if (focusIndex < 0 || focusIndex >= focusItems.Count) return;

            ClickTarget ct = focusItems[focusIndex];
            if (ct == null || ct.action == null) return;

            try { ct.action(); }
            catch (Exception e) { if (L != null) L.LogWarning("[死亡统计] 确认操作异常: " + e.Message); }
        }

        /// <summary>
        /// 用数字键 1-4 / 0 切槽位时，把焦点同步过去，免得框还停在旧标签上。
        /// keepRow = true 时（数字键这种「顺手切一下」的场景）如果人正在栏区里，
        /// 就别把他弹回上层，只记住顶部该停哪个标签。
        /// </summary>
        private void SyncFocusToSlot(bool keepRow = false)
        {
            int found = -1;
            for (int i = 0; i < focusItems.Count; i++)
            {
                if (focusItems[i] != null && focusItems[i].tabIndex == viewSlot) { found = i; break; }
            }
            if (found >= 0) topFocusMemory = found;

            if (keepRow && focusRow == 1)
            {
                focusIndex = Mathf.Clamp(topItemCount + activeCol, 0, Mathf.Max(0, focusItems.Count - 1));
                return;
            }

            focusRow = 0;
            if (found >= 0) focusIndex = found;
        }

        /// <summary>把焦点框摆到当前选中项的四周（4 条细边，不挡内容）</summary>
        private void UpdateFocusFrame()
        {
            bool show = uiVisible && focusEdges[0] != null
                && focusItems.Count > 0 && focusIndex >= 0 && focusIndex < focusItems.Count
                && focusItems[focusIndex].rt != null;

            if (!show)
            {
                for (int i = 0; i < focusEdges.Length; i++)
                    if (focusEdges[i] != null) focusEdges[i].gameObject.SetActive(false);
                return;
            }

            RectTransform tr = focusItems[focusIndex].rt;
            Vector2 pos = tr.anchoredPosition;
            Vector2 size = tr.sizeDelta;

            float x = pos.x;
            float y = -pos.y;      // UI 里 y 轴向下为正（pivot 在左上角）
            float w = size.x;
            float h = size.y;

            // 焦点落在整栏上时框会非常大，压细一点、淡一点，免得盖住内容视线。
            // 按钮/标签被选中时底下已经是实心金底，框改用白色细边，否则金框压金底看不出来。
            // 注意 pad 只能留 1px：以前留 3px，框和按钮之间露一条底色缝，
            // 看起来像"按钮旁边飘了个无关的白框"，而不是"这个按钮被选中了"。
            ClickTarget cur = focusItems[focusIndex];
            bool isCol = cur != null && cur.colIndex >= 0;
            float pad = isCol ? 2f : 1f;
            float th = isCol ? 1.5f : 2f;
            Color edgeCol = isCol
                ? new Color(1f, 0.85f, 0.42f, 0.45f)
                : new Color(1f, 0.98f, 0.92f, 0.85f);
            for (int i = 0; i < focusEdges.Length; i++)
                if (focusEdges[i] != null) focusEdges[i].color = edgeCol;

            LayoutFocusEdge(0, x - pad, y - pad, w + pad * 2f, th);            // 上
            LayoutFocusEdge(1, x - pad, y + h + pad - th, w + pad * 2f, th);   // 下
            LayoutFocusEdge(2, x - pad, y - pad, th, h + pad * 2f);            // 左
            LayoutFocusEdge(3, x + w + pad - th, y - pad, th, h + pad * 2f);   // 右
        }

        private void LayoutFocusEdge(int i, float x, float y, float w, float h)
        {
            Image img = focusEdges[i];
            if (img == null) return;
            img.gameObject.SetActive(true);
            RectTransform rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        /// <summary>读游戏纵向输入：正数 = look up，负数 = look down</summary>
        private float ReadLookAxis(bool wantUp)
        {
            if (wantUp)
            {
                if (!string.IsNullOrEmpty(lookUpAxis)) { try { return Input.GetAxisRaw(lookUpAxis); } catch { } }
                if (!string.IsNullOrEmpty(lookVerticalAxis)) { try { return Input.GetAxisRaw(lookVerticalAxis); } catch { } }
            }
            else
            {
                if (!string.IsNullOrEmpty(lookDownAxis)) { try { return Input.GetAxisRaw(lookDownAxis); } catch { } }
                if (!string.IsNullOrEmpty(lookVerticalAxis)) { try { return -Input.GetAxisRaw(lookVerticalAxis); } catch { } }
            }
            return 0f;
        }

        /// <summary>启动时探测游戏是否有可用的纵向输入轴</summary>
        private void ProbeLookAxes()
        {
            lookVerticalAxis = AxisExists("Vertical") ? "Vertical" : null;

            lookUpAxis = AxisExists("UP") ? "UP" : (AxisExists("Up") ? "Up" : null);
            lookDownAxis = AxisExists("DOWN") ? "DOWN" : (AxisExists("Down") ? "Down" : null);

            lookAxisReady = lookUpAxis != null || lookDownAxis != null || lookVerticalAxis != null;

            // 横向：用来像游戏菜单那样左右选择
            lookHorizontalAxis = AxisExists("Horizontal") ? "Horizontal" : null;
            leftAxis = AxisExists("LEFT") ? "LEFT" : (AxisExists("Left") ? "Left" : null);
            rightAxis = AxisExists("RIGHT") ? "RIGHT" : (AxisExists("Right") ? "Right" : null);
            horizAxisReady = lookHorizontalAxis != null || leftAxis != null || rightAxis != null;

            L.LogInfo("[死亡统计] 滚动输入源: UP=" + (lookUpAxis ?? "-")
                + " DOWN=" + (lookDownAxis ?? "-") + " Vertical=" + (lookVerticalAxis ?? "-")
                + " → " + (lookAxisReady ? "跟随游戏 look up / look down" : "游戏无纵向轴，回退配置按键"));
            L.LogInfo("[死亡统计] 左右选择输入源: LEFT=" + (leftAxis ?? "-")
                + " RIGHT=" + (rightAxis ?? "-") + " Horizontal=" + (lookHorizontalAxis ?? "-")
                + " → " + (horizAxisReady ? "跟随游戏左右键" : "游戏无横向轴，回退 A/D 与 ←→"));
        }

        /// <summary>读游戏横向输入：正数 = 右，负数 = 左</summary>
        private float ReadHorizAxis(bool wantRight)
        {
            if (wantRight)
            {
                if (!string.IsNullOrEmpty(rightAxis)) { try { return Input.GetAxisRaw(rightAxis); } catch { } }
                if (!string.IsNullOrEmpty(lookHorizontalAxis)) { try { return Input.GetAxisRaw(lookHorizontalAxis); } catch { } }
            }
            else
            {
                if (!string.IsNullOrEmpty(leftAxis)) { try { return Input.GetAxisRaw(leftAxis); } catch { } }
                if (!string.IsNullOrEmpty(lookHorizontalAxis)) { try { return -Input.GetAxisRaw(lookHorizontalAxis); } catch { } }
            }
            return 0f;
        }

        private static bool AxisExists(string name)
        {
            try { Input.GetAxisRaw(name); return true; }
            catch { return false; }
        }

        // ================= 槽位 =================

        /// <summary>当前存档槽位 1..4，识别不到返回 0</summary>
        private int CurrentSlot()
        {
            try
            {
                Type gm = FindType("GameManager");
                if (gm != null)
                {
                    object inst = GetStaticMember(gm, "instance", "Instance");
                    if (inst != null)
                    {
                        object pid = GetMember(inst, "profileID", "profileId", "saveSlot");
                        if (pid != null)
                        {
                            int v = Convert.ToInt32(pid);
                            if (v >= 1 && v <= 4) return v;
                        }
                    }
                }
            }
            catch (Exception e) { L.LogWarning("[死亡统计] 读取槽位失败: " + e.Message); }

            // 回退：取最近修改过的 userN.dat
            try
            {
                int best = 0;
                DateTime bestTime = DateTime.MinValue;
                for (int i = 1; i <= 4; i++)
                {
                    string f = SaveFilePath(i);
                    if (!File.Exists(f)) continue;
                    DateTime t = File.GetLastWriteTime(f);
                    if (t > bestTime) { bestTime = t; best = i; }
                }
                if (best > 0 && !fallbackWarned)
                {
                    fallbackWarned = true;
                    L.LogWarning("[死亡统计] 未能从 GameManager 取到槽位，改用「最近保存的存档」= 槽位 " + best);
                }
                return best;
            }
            catch { }
            return 0;
        }

        private string SaveFilePath(int slot)
        {
            return Path.Combine(Application.persistentDataPath, "user" + slot + ".dat");
        }

        private string SlotFilePath(int slot)
        {
            return Path.Combine(statsDir, "slot" + slot + ".tsv");
        }

        // ================= 诊断日志 =================
        //
        // 为什么用「订阅自己的日志事件」而不是在每处 LogInfo 旁边再写一份：
        //   判定过程有 40 多个输出点，逐处加写文件容易漏，以后新增的也会忘。
        //   订阅 L.LogEvent 一次覆盖全部，还能顺手把每帧刷屏的（光标、焦点）滤掉。
        //
        // 写去哪：BepInEx\plugins\YTC_DeathStats\diag-yyyyMMdd.log，追加写、按天分文件。
        //   BepInEx 自己的 LogOutput.log 每次启动都会被重写，问「这次到底判成了谁」必须当场看，
        //   所以另开一份不会覆盖的。

        private static readonly string[] sDiagKeys =
        {
            "插件已加载", "落账", "+1 |", "死亡间隔", "攻击物归属", "特效伤害",
            "可移动伤害物", "地形/陷阱判定", "认不出来源", "生成动作", "生成归属生效",
            "敌人识别", "未知中文名", "致死陷阱", "本房间摆出", "已记录敌人死亡",
            "场地BOSS统一归类", "旧数据目录已迁移", "重复死亡信号",
        };

        private static void OnOwnLog(object sender, LogEventArgs e)
        {
            if (!sDiagOn || e == null) return;
            string msg = e.Data == null ? null : e.Data.ToString();
            if (string.IsNullOrEmpty(msg)) return;

            for (int i = 0; i < sDiagKeys.Length; i++)
            {
                if (msg.IndexOf(sDiagKeys[i], StringComparison.Ordinal) >= 0)
                {
                    DiagLine(msg);
                    return;
                }
            }
        }

        /// <summary>只写文件、不进日志（进日志会和 OnOwnLog 形成递归）。</summary>
        private static void DiagLine(string msg)
        {
            if (!sDiagOn || string.IsNullOrEmpty(sDiagDir)) return;
            if (sDiagLines > 20000) return;      // 兜底：万一哪版刷屏，别把盘写满
            try
            {
                string day = DateTime.Now.ToString("yyyyMMdd");
                if (day != sDiagDay || string.IsNullOrEmpty(sDiagPath))
                {
                    sDiagDay = day;
                    sDiagPath = Path.Combine(sDiagDir, "diag-" + day + ".log");
                    sDiagLines = 0;
                }
                File.AppendAllText(sDiagPath,
                    DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n",
                    new UTF8Encoding(false));
                sDiagLines++;
            }
            catch { }
        }

        /// <summary>
        /// 数据目录改过两次名：XiaoCanDeathStats → DeathStats → YTC_DeathStats。
        /// 不改名的话旧记录会「看不见」，所以启动时把旧目录里的 slotN.tsv 复制过来；
        /// 新目录已经有同名文件就不覆盖，旧的目录本身留着不动（用户自己决定删不删）。
        /// </summary>
        private void MigrateOldDataDir()
        {
            string[] oldNames = { "XiaoCanDeathStats", "DeathStats" };
            foreach (string name in oldNames)
            {
                try
                {
                    string oldDir = Path.Combine(Paths.PluginPath, name);
                    if (!Directory.Exists(oldDir)) continue;
                    if (string.Equals(oldDir, statsDir, StringComparison.OrdinalIgnoreCase)) continue;

                    int moved = 0;
                    foreach (string src in Directory.GetFiles(oldDir, "slot*.tsv"))
                    {
                        string dst = Path.Combine(statsDir, Path.GetFileName(src));
                        if (File.Exists(dst)) continue;
                        File.Copy(src, dst);
                        moved++;
                    }
                    if (moved > 0 && L != null)
                        L.LogInfo("[死亡统计] 旧数据目录已迁移 " + moved + " 个槽位文件"
                            + "（" + name + " → " + DATA_DIR_NAME + "）");
                }
                catch { }
            }
        }

        /// <summary>存档被删（userN.dat 消失）→ 统计数据一并删除</summary>
        private void SyncDeletedSaves(bool verbose)
        {
            for (int i = 1; i <= 4; i++)
            {
                string stat = SlotFilePath(i);
                string save = SaveFilePath(i);
                bool hasStat = File.Exists(stat);
                bool hasSave = File.Exists(save);

                if (hasStat && !hasSave)
                {
                    try
                    {
                        File.Delete(stat);
                        slots.Remove(i);
                        if (viewSlot == i) viewSlot = 0;
                        uiDirty = true;
                        L.LogInfo("[死亡统计] 检测到槽位 " + i + " 存档已删除，对应统计已清除");
                    }
                    catch (Exception e) { L.LogWarning("[死亡统计] 删除槽位 " + i + " 统计失败: " + e.Message); }
                }
                else if (verbose && !hasStat && hasSave)
                {
                    L.LogInfo("[死亡统计] 槽位 " + i + " 有存档，暂无统计");
                }
            }
        }

        private SlotStats GetSlot(int slot, bool create)
        {
            SlotStats s;
            if (slots.TryGetValue(slot, out s)) return s;
            if (!create) return null;
            s = new SlotStats(slot);
            slots[slot] = s;
            return s;
        }

        // ================= 记录 =================

        /// <summary>一条「场地 BOSS」规则：进这个房间，死亡就算这个主儿的。</summary>
        private struct ArenaBoss
        {
            public string prefix;   // 场景名前缀（运行时 sceneName 常比资源名短，所以按前缀）
            public string killer;   // 面板上显示的死因名
            public string label;    // 写进 detail 的场地名，用来还原「原本是被什么打死的」
            public ArenaBoss(string p, string k, string l) { prefix = p; killer = k; label = l; }
        }

        // 这些房间整场能杀你的基本只有场地主，弹幕/特效又多又密，逐条归属必然错漏，
        // 所以统一算场地主的击杀；原判定留在 detail（存档第 4 列 + 日志）里可还原。
        // 顺序有讲究：前缀长的必须排在前面（GG_Grimm_Nightmare 在 GG_Grimm 之前），
        // 否则先命中短前缀就被吞掉了。
        private static readonly ArenaBoss[] sArenaBosses =
        {
            // ---- 主线 ----
            new ArenaBoss("Dream_Final",             "辐光",      "辐光战场"),
            new ArenaBoss("Grimm_Nightmare",         "梦魇之王",  "梦魇格林战"),
            new ArenaBoss("Grimm_Main_Tent_boss",    "格林",      "格林战"),
            new ArenaBoss("Room_Final_Boss_Core",    "空洞骑士",  "黑卵圣殿"),
            new ArenaBoss("Fungus1_04_boss",         "初见大黄蜂", "初见大黄蜂战"),
            new ArenaBoss("Deepnest_East_Hornet_boss","二见大黄蜂","二见大黄蜂战"),

            // ---- 神居（万神殿里的同名战）----
            new ArenaBoss("GG_Grimm_Nightmare",      "梦魇之王",  "梦魇格林战"),
            new ArenaBoss("GG_Grimm",                "格林",      "格林战"),
            new ArenaBoss("GG_Hollow_Knight",        "空洞骑士",  "黑卵圣殿"),
            new ArenaBoss("GG_Hornet_1",             "初见大黄蜂", "初见大黄蜂战"),
            new ArenaBoss("GG_Hornet_2",             "二见大黄蜂", "二见大黄蜂战"),
            new ArenaBoss("GG_Radiance",             "辐光",      "辐光战场"),
        };

        private static bool ArenaBossOf(string scene, out string killer, out string label)
        {
            killer = null;
            label = null;
            if (string.IsNullOrEmpty(scene)) return false;
            string s = scene.Trim();
            for (int i = 0; i < sArenaBosses.Length; i++)
            {
                if (!s.StartsWith(sArenaBosses[i].prefix, StringComparison.OrdinalIgnoreCase)) continue;
                killer = sArenaBosses[i].killer;
                label = sArenaBosses[i].label;
                return true;
            }
            return false;
        }

        private static void RecordDeath(string scene, string killer, string detail)
        {
            try
            {
                if (Instance == null) return;

                int slot = Instance.CurrentSlot();
                if (slot == 0)
                {
                    L.LogWarning("[死亡统计] 未能识别存档槽位，本次死亡不记录");
                    return;
                }

                SlotStats st = Instance.GetSlot(slot, true);

                // 落账前先把内部物体名翻成官方中文名，存档里就都是中文了，
                // 以后换版本、换机器也不会再冒出一堆英文。
                killer = EnemyToCn(killer);

                // 新游戏覆盖检测：playTime 大幅倒退 = 该槽位被重开
                float pt = Instance.CurrentPlayTime();
                if (pt > 0f && st.MaxPlayTime > 0f && pt + 300f < st.MaxPlayTime)
                {
                    st.Clear();
                    L.LogInfo("[死亡统计] 槽位 " + slot + " 进度倒退（" + Mathf.Round(st.MaxPlayTime) + "s -> "
                        + Mathf.Round(pt) + "s），判定为新游戏，统计已重置");
                }
                if (pt > st.MaxPlayTime) st.MaxPlayTime = pt;

                // 记下死亡坐标，之后在当前房间摆一个图钉
                Vector3 dpos = Vector3.zero;
                bool hasPos = false;
                float rawY = 0f;
                object hero = GetHero();
                Component hc = hero as Component;
                if (hc != null)
                {
                    dpos = hc.transform.position;
                    rawY = dpos.y;
                    // 图钉不贴头顶：按角色包围盒下移到「从脚底往上 1/3」处
                    dpos.y = Instance.MarkerAnchorY(hc, dpos.y);
                    hasPos = true;
                }

                st.Add(new DeathRecord
                {
                    Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Scene = scene,
                    Killer = killer,
                    Detail = detail,
                    X = dpos.x,
                    Y = dpos.y,
                    Z = dpos.z,
                    HasPos = hasPos,
                    AnchorVer = 2
                });

                Instance.SaveSlot(slot);
                Instance.uiDirty = true;
                Instance.markerScene = null;      // 下一帧重建，把新图钉补上

                L.LogInfo("[死亡统计] +1 | 槽位=" + slot + " | 场景=" + scene + " | 死因=" + killer
                    + " | 类型=" + detail + " | 坐标=" + (hasPos ? dpos.x.ToString("0.#") + "," + dpos.y.ToString("0.#") : "未取到")
                    + (hasPos ? "（落点 " + (dpos.y - rawY).ToString("0.00") + "）" : "")
                    + " | 该槽累计 " + st.Records.Count);
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 落账失败: " + e.Message);
            }
        }

        private float CurrentPlayTime()
        {
            try
            {
                Type pdType = FindType("PlayerData");
                if (pdType == null) return -1f;
                object pd = GetStaticMember(pdType, "instance", "Instance");
                if (pd == null) return -1f;
                object v = GetMember(pd, "playTime", "playTimeRaw");
                if (v != null) return Convert.ToSingle(v);
            }
            catch { }
            return -1f;
        }

        // ================= 存储 =================

        private void LoadAllSlots()
        {
            for (int i = 1; i <= 4; i++)
            {
                string f = SlotFilePath(i);
                if (!File.Exists(f)) continue;
                try
                {
                    SlotStats st = new SlotStats(i);
                    foreach (string raw in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        if (string.IsNullOrWhiteSpace(raw)) continue;
                        if (raw.StartsWith("#meta"))
                        {
                            foreach (string part in raw.Split('\t'))
                            {
                                if (part.StartsWith("playTime="))
                                {
                                    float v;
                                    if (float.TryParse(part.Substring(9), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                                        st.MaxPlayTime = v;
                                }
                            }
                            continue;
                        }
                        string[] p = raw.Split('\t');
                        if (p.Length < 3) continue;
                        DeathRecord rec = new DeathRecord
                        {
                            Time = p[0],
                            Scene = p[1],
                            Killer = NormalizeKiller(p.Length > 2 ? p[2] : "", p.Length > 1 ? p[1] : ""),
                            Detail = p.Length > 3 ? p[3] : ""
                        };
                        // 第 5、6、7 列是死亡坐标；旧存档没有这三列，当作"无坐标"
                        if (p.Length > 5)
                        {
                            float rx, ry, rz = 0f;
                            bool okX = float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out rx);
                            bool okY = float.TryParse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture, out ry);
                            if (p.Length > 6) float.TryParse(p[6], NumberStyles.Float, CultureInfo.InvariantCulture, out rz);
                            if (okX && okY) { rec.X = rx; rec.Y = ry; rec.Z = rz; rec.HasPos = true; }
                        }
                        // 第 8 列是落点算法版本；没有就是老算法（图钉贴头顶）
                        if (p.Length > 7) int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out rec.AnchorVer);
                        st.Add(rec);
                    }
                    slots[i] = st;
                }
                catch (Exception e) { L.LogWarning("[死亡统计] 读取槽位 " + i + " 失败: " + e.Message); }
            }
        }

        // ---- 旧存档死因口径迁移 ----
        // v0.9.17 起：认不出主人的攻击物统一显示「未知」，尖刺统一成「地形/陷阱」。
        // 老记录里存的是当时按名字猜的分类名（冲击波/爆炸/刺球/弹射物…），
        // 读档时顺手换掉，这样不用清空统计也能看到统一口径。
        private static readonly Dictionary<string, string> sKillerRename = new Dictionary<string, string>
        {
            { "尖刺", "地形/陷阱" },
            { "冲击波", "未知" }, { "爆炸", "未知" }, { "毒气", "未知" },
            { "光束", "未知" }, { "弹幕", "未知" }, { "火焰", "未知" }, { "斩击", "未知" },
            { "刺球/弹射物", "未知" }, { "攻击物", "未知" },
            { "未识别伤害", "未知" }, { "未识别来源", "未知" }, { "未知伤害", "未知" },
        };

        private static string NormalizeKiller(string k, string scene)
        {
            if (string.IsNullOrEmpty(k)) return k;
            string key = k.Trim();

            // v0.9.19 起大黄蜂按场地分成初见/二见。老记录只写了「大黄蜂」，
            // 但存档里带着场景号，所以能反推出是哪一场，不用清空统计也不会分裂成三条。
            if (key == "大黄蜂")
            {
                string ak, al;
                if (ArenaBossOf(scene, out ak, out al) && !string.IsNullOrEmpty(ak)) return ak;
                return key;
            }

            string to;
            return sKillerRename.TryGetValue(key, out to) ? to : k;
        }

        private void SaveSlot(int slot)
        {
            SlotStats st = GetSlot(slot, false);
            if (st == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("#meta\tplayTime=").Append(st.MaxPlayTime.ToString("0.##", CultureInfo.InvariantCulture)).Append('\n');
                foreach (DeathRecord r in st.Records)
                {
                    sb.Append(r.Time).Append('\t').Append(r.Scene).Append('\t').Append(r.Killer).Append('\t')
                      .Append(r.Detail);
                    if (r.HasPos)
                        sb.Append('\t').Append(r.X.ToString("0.###", CultureInfo.InvariantCulture))
                          .Append('\t').Append(r.Y.ToString("0.###", CultureInfo.InvariantCulture))
                          .Append('\t').Append(r.Z.ToString("0.###", CultureInfo.InvariantCulture))
                          .Append('\t').Append(r.AnchorVer.ToString(CultureInfo.InvariantCulture));
                    sb.Append('\n');
                }
                File.WriteAllText(SlotFilePath(slot), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { L.LogWarning("[死亡统计] 保存槽位 " + slot + " 失败: " + e.Message); }
        }

        private void ClearCurrentView()
        {
            if (viewSlot == 0)
            {
                foreach (KeyValuePair<int, SlotStats> kv in slots) kv.Value.Clear();
                foreach (int i in new List<int>(slots.Keys)) SaveSlot(i);
            }
            else
            {
                SlotStats st = GetSlot(viewSlot, false);
                if (st != null) { st.Clear(); SaveSlot(viewSlot); }
            }
            uiDirty = true;
            ResetScroll();
            markerScene = null;       // 数据清空了，图钉也要跟着消失
            L.LogInfo("[死亡统计] 已清空 " + (viewSlot == 0 ? "全部槽位" : "槽位 " + viewSlot) + " 的统计");
        }

        // ================= 补丁 =================

        private void PatchAll()
        {
            Type hc = FindType("HeroController");
            if (hc == null) { L.LogError("[死亡统计] 找不到 HeroController"); return; }

            // 同名重载全部拦上：不能只抓第一个，否则真正的入口可能被漏掉
            PatchEveryOverload(hc, new string[] { "TakeDamage" }, nameof(OnTakeDamage));
            PatchEveryOverload(hc, new string[] { "Die" }, nameof(OnDie));

            // DieFromHazard(HazardType, float)：只用来拿陷阱类型（踩刺/酸水/岩浆/掉坑），
            // 不再由它落账 —— 白宫踩刺后游戏会反复调它，但英雄并没有真死。
            PatchEveryOverload(hc, new string[] { "DieFromHazard" }, nameof(OnDieFromHazard));

            // 唯一落账点
            Type gm = FindType("GameManager");
            int deadHooked = gm != null ? PatchEveryOverload(gm, new string[] { "PlayerDead" }, nameof(OnPlayerDead)) : 0;
            sPlayerDeadHooked = deadHooked > 0;
            if (sPlayerDeadHooked)
                L.LogInfo("[死亡统计] 落账点 = GameManager.PlayerDead（真死唯一入口）；"
                    + "HeroController.Die / DieFromHazard 仅用于识别死因，不再计数");
            else
                L.LogWarning("[死亡统计] 没拦到 GameManager.PlayerDead，退回用 HeroController.Die 落账（可能重复计数）");

            // 接管游戏的暂停入口，避免面板开着时按 ESC 又弹出游戏菜单
            try { PatchPauseBlockers(); }
            catch (Exception e) { L.LogWarning("[死亡统计] 暂停入口补丁失败: " + e.Message); }

            // 敌人死亡留痕：只给「爆裂致死」溯源用，不改游戏行为
            try
            {
                Type hm = FindType("HealthManager");
                if (hm != null)
                {
                    int hooked = PatchEveryOverload(hm, new string[] { "Die" }, nameof(OnEnemyDie));
                    sEnemyDeathHooked = hooked > 0;
                    if (sEnemyDeathHooked)
                        L.LogInfo("[死亡统计] 已记录敌人死亡（共 " + hooked + " 个 Die 重载），用于爆裂致死溯源");
                }
            }
            catch (Exception e) { L.LogWarning("[死亡统计] 敌人死亡补丁失败: " + e.Message); }

            // 攻击物生成归属：远程弹幕/自爆物靠这个认主人。
            // PlayMaker 可能还没加载，失败就交给 Update 定时重试。
            try { TryHookFsmSpawns(); }
            catch (Exception e) { L.LogWarning("[死亡统计] 攻击物生成补丁失败: " + e.Message); }

            // 伤害物出生点账：拦 DamageHero.OnEnable，记下弹射物被启用时的坐标。
            // 共享预制体（Spitter Shot R 这种）靠名字永远认不出主人，但出生点就在主人身上。
            try { TryHookHazardBirth(); }
            catch (Exception e) { L.LogWarning("[死亡统计] 出生点补丁失败: " + e.Message); }

            // 按参数名定位 TakeDamage 里的 hazardType。
            // 不能按位置取：这个版本 __args[3] 是伤害值，怪打 1 点血会被误判成 SPIKES。
            foreach (MethodInfo m in hc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.Name != "TakeDamage") continue;

                ParameterInfo[] ps = m.GetParameters();
                List<string> sig = new List<string>();
                for (int i = 0; i < ps.Length; i++)
                {
                    sig.Add(ps[i].ParameterType.Name + " " + ps[i].Name);
                    string pn = ps[i].Name ?? "";
                    if (sHazardArgIndex < 0 && pn.IndexOf("hazard", StringComparison.OrdinalIgnoreCase) >= 0)
                        sHazardArgIndex = i;
                    if (sSourceArgIndex < 0 && ps[i].ParameterType == typeof(GameObject))
                        sSourceArgIndex = i;
                }

                L.LogInfo("[死亡统计] TakeDamage 参数: " + string.Join(", ", sig.ToArray())
                    + "  → hazard 参数索引=" + sHazardArgIndex
                    + (sHazardArgIndex < 0 ? "（未识别，陷阱只由 DieFromHazard 判定）" : ""));
                break;
            }

            // 把疑似伤害/死亡入口一并列进日志，方便下次核对有没有漏网
            List<string> names = new List<string>();
            foreach (MethodInfo m in hc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (names.Contains(m.Name)) continue;
                string n = m.Name.ToLowerInvariant();
                if (n.Contains("die") || n.Contains("damage") || n.Contains("hazard") || n.Contains("hurt"))
                    names.Add(m.Name);
            }
            L.LogInfo("[死亡统计] HeroController 伤害/死亡相关方法: " + string.Join(", ", names.ToArray()));

            // 把 HazardType 的实际枚举值打出来，陷阱名对不上时一眼可查
            Type hzEnum = FindType("HazardType");
            if (hzEnum != null && hzEnum.IsEnum)
            {
                List<string> vals = new List<string>();
                foreach (object v in Enum.GetValues(hzEnum))
                    vals.Add(Enum.GetName(hzEnum, v) + "=" + Convert.ToInt32(v));
                L.LogInfo("[死亡统计] HazardType(" + hzEnum.FullName + "): " + string.Join(", ", vals.ToArray()));
            }
            else L.LogWarning("[死亡统计] 找不到 HazardType 枚举，陷阱名将显示为数字");
        }

        /// <summary>
        /// 接管游戏自己的「暂停开关」，这样面板开着时按 ESC 只会关面板，不会弹出游戏菜单
        /// （两个界面叠在一起会互相抢输入、抢 cursor 状态，最后游戏卡在假暂停里）。
        ///
        /// 方法名不写死在某一个类上：在 Assembly-CSharp 里按名字扫描，
        /// 命中几个算几个，全都没命中才写警告 —— 游戏更新改了归属也不会静默失效。
        /// </summary>
        private void PatchPauseBlockers()
        {
            MethodInfo prefix = typeof(DeathStatsPlugin).GetMethod(
                nameof(Prefix_BlockGamePause), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null) { L.LogError("[死亡统计] 找不到暂停拦截方法"); return; }

            string[] names = new string[]
            {
                "TogglePauseGame",        // 最可能的真入口：ESC → 切暂停
                "PauseGameToggle",        // 同上，另一个历史命名
                "PauseGameToggleByMenu",
                "CheckForPauseButton"     // 读取 ESC 键并触发暂停的地方
            };

            // 先只看几个「暂停肯定在这」的类型，快且稳
            string[] preferred = new string[] { "UIManager", "GameManager", "HeroController", "InputHandler", "GameCameras" };
            foreach (string tn in preferred)
            {
                Type t = null;
                try { t = FindType(tn); } catch { }
                if (t != null) PatchPauseOnType(t, prefix, names);
            }

            // 一个都没命中才全库扫一遍兜底（Assembly-CSharp 类型很多，扫一遍是最后手段）
            if (sPausedPatched.Count == 0)
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly asm in asms)
                {
                    string an = null;
                    try { an = asm.GetName().Name; } catch { }
                    if (an == null) continue;
                    if (!an.StartsWith("Assembly-CSharp", StringComparison.Ordinal)) continue;

                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types; }
                    catch { continue; }

                    foreach (Type t in types)
                        if (t != null) PatchPauseOnType(t, prefix, names);
                }
            }

            if (sPausedPatched.Count == 0)
                L.LogWarning("[死亡统计] 没扫到游戏暂停入口，ESC 可能仍会弹出游戏菜单（可直接反馈日志）");
            else
                L.LogInfo("[死亡统计] 暂停入口共接管 " + sPausedPatched.Count + " 个: "
                    + string.Join(", ", sPausedPatched.ToArray()));
        }

        private void PatchPauseOnType(Type t, MethodInfo prefix, string[] names)
        {
            MethodInfo[] ms;
            try
            {
                ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            }
            catch { return; }

            foreach (MethodInfo m in ms)
            {
                if (m.ContainsGenericParameters || m.IsAbstract) continue;
                bool hit = false;
                foreach (string n in names) if (m.Name == n) { hit = true; break; }
                if (!hit) continue;

                try
                {
                    harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                    sPausedPatched.Add(t.Name + "." + m.Name);
                    L.LogInfo("[死亡统计] 已接管暂停入口 " + t.Name + "." + m.Name + "(" + ParamSig(m) + ")");
                }
                catch (Exception e)
                {
                    L.LogWarning("[死亡统计] 接管暂停入口 " + t.Name + "." + m.Name + " 失败: " + e.Message);
                }
            }
        }

        /// <summary>
        /// 面板打开期间（含关闭后的一小段免疫窗口）拒绝一切游戏暂停请求。
        /// 返回 false = 跳过原方法，暂停菜单就不会被打开。
        /// </summary>
        private static bool Prefix_BlockGamePause(MethodBase __originalMethod)
        {
            DeathStatsPlugin inst = Instance;
            if (inst == null) return true;

            bool block = inst.uiVisible || Time.realtimeSinceStartup < inst.blockPauseUntil;
            if (!block) return true;

            if (sPauseBlockLogs < 6)
            {
                sPauseBlockLogs++;
                if (L != null)
                    L.LogInfo("[死亡统计] 已拦下游戏暂停请求: "
                        + (__originalMethod == null ? "?" : __originalMethod.DeclaringType.Name + "." + __originalMethod.Name)
                        + (inst.uiVisible ? "（面板打开中）" : "（关面板免疫窗口）"));
            }
            return false;
        }

        private int PatchEveryOverload(Type t, string[] names, string hook)
        {
            MethodInfo hookMethod = typeof(DeathStatsPlugin).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic);
            if (hookMethod == null) { L.LogError("[死亡统计] 找不到补丁方法 " + hook); return 0; }

            int count = 0;
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.ContainsGenericParameters || m.IsAbstract) continue;

                bool hit = false;
                foreach (string n in names) if (m.Name == n) { hit = true; break; }
                if (!hit) continue;

                try
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(hookMethod));
                    count++;
                    L.LogInfo("[死亡统计] 已拦截 " + t.Name + "." + m.Name + "(" + ParamSig(m) + ")");
                }
                catch (Exception e)
                {
                    L.LogWarning("[死亡统计] 拦截 " + t.Name + "." + m.Name + " 失败: " + e.Message);
                }
            }
            if (count == 0) L.LogWarning("[死亡统计] " + t.Name + " 上没找到 " + string.Join("/", names));
            return count;
        }

        /// <summary>
        /// HealthManager.Die 的 postfix：只记一笔「哪个怪、在哪、什么时候死的」。
        /// 跳虫、爆肚蝠这类怪死后会炸开，伤到你的是它们留下的 Gas Explosion 特效，
        /// 那个物体既不挂 HealthManager、也不是怪的子物体，光看攻击者永远查不到凶手。
        /// 唯一的线索是「炸你的瞬间，附近刚死过一个会爆的怪」，所以这里留痕给爆炸溯源用。
        /// 不参与任何游戏逻辑，异常一律吞掉。
        /// </summary>
        private static void OnEnemyDie(object __instance)
        {
            try
            {
                Component c = __instance as Component;
                if (c == null || c.gameObject == null) return;

                string nm = null;
                object et = GetMember(__instance, "enemyType");
                if (et != null)
                {
                    string en = NameOfEnumOrInt(et);
                    if (!string.IsNullOrEmpty(en)) nm = EnemyToCn(en);
                }
                if (string.IsNullOrEmpty(nm)) nm = EnemyToCn(CleanName(c.gameObject.name));
                if (string.IsNullOrEmpty(nm)) return;

                int i = sEnemyDeathHead % sEnemyDeaths.Length;
                sEnemyDeathHead++;
                sEnemyDeaths[i].name = nm;
                sEnemyDeaths[i].pos = c.transform.position;
                sEnemyDeaths[i].at = Time.realtimeSinceStartup;

                // 留痕，方便核对「自爆怪到底走不走 Die」：走的话这里会有一行，
                // 爆炸溯源却没命中，就说明是距离/时间窗口的问题，不是没记录。
                if (sEnemyDeathLogs < 40)
                {
                    sEnemyDeathLogs++;
                    Component hc = GetHero() as Component;
                    float d = hc != null ? Vector3.Distance(hc.transform.position, c.transform.position) : -1f;
                    DiagLine("[死亡统计] 敌人死亡 | " + nm
                        + " | 距英雄 " + (d >= 0f ? d.ToString("0.0") : "?")
                        + " | 位置 " + c.transform.position.x.ToString("0.0")
                        + "," + c.transform.position.y.ToString("0.0"));
                }
            }
            catch { }
        }

        /// <summary>自爆怪的最后位置快照（见 WatchBurstEnemies）</summary>
        private struct BurstWatch
        {
            public string name;     // 场景里的英文名，序号已剥
            public Vector3 pos;
            public float at;        // 最后一次看到 / 刚消失的时间
        }
        private static readonly Dictionary<int, BurstWatch> sBurstLive = new Dictionary<int, BurstWatch>();
        private static readonly List<BurstWatch> sBurstGone = new List<BurstWatch>();
        private static readonly HashSet<int> sBurstSeen = new HashSet<int>();
        private static float sBurstWatchAt;
        // 会自己炸开的怪。名单窄一点没关系，写错了只会退回「未知」，不会乱扣帽子。
        private static readonly string[] sBurstEnemyPrefixes = { "Bursting Bouncer", "Volatile Gruzzer" };

        /// <summary>
        /// 已知「怪死后自己炸开」的爆炸特效 → 会放它的怪（表 = 特效名前缀 + 候选怪名前缀）。
        /// 这类特效既没有父物体、也没有生成记录，名字和怪名还没有共同词根
        /// （Gas Explosion ← Bursting Bouncer），除了固定映射没有别的线索。
        /// 前提是房间里确实站着一只这种怪：一只能对上才归它，对不上就返回 null 老实记未知。
        /// </summary>
        private static readonly string[][] sBurstEffects = new string[][]
        {
            new string[] { "Gas Explosion", "Bursting Bouncer", "Volatile Gruzzer" },
            new string[] { "Death Wave Infected", "Bursting Bouncer" },
        };

        private static string BurstEffectOwner(string effectName, GameObject go, out float bestDist, out string why)
        {
            bestDist = -1f;
            why = null;
            if (string.IsNullOrEmpty(effectName)) return null;

            string en = SqueezeSpaces(CleanName(effectName));
            string[] row = null;
            for (int i = 0; i < sBurstEffects.Length && row == null; i++)
                if (en.StartsWith(sBurstEffects[i][0], StringComparison.OrdinalIgnoreCase))
                    row = sBurstEffects[i];
            if (row == null) return null;

            Component heroC = GetHero() as Component;
            Vector3 at;
            if (go != null) at = go.transform.position;
            else if (heroC != null) at = heroC.transform.position;
            else return null;

            string best = null;

            // ① 刚刚自爆掉的那只：爆炸物就在它炸开的地方，位置一对就准了。
            //    毒气是慢慢扩散的，从自爆到把你炸到能隔上好几秒（实测 9 秒），窗口别设太小。
            float now = Time.realtimeSinceStartup;
            for (int i = sBurstGone.Count - 1; i >= 0; i--)
            {
                BurstWatch w = sBurstGone[i];
                if (now - w.at > 12f) continue;
                float d = Vector3.Distance(at, w.pos);
                if (d > 5f) continue;
                if (best == null || d < bestDist) { best = w.name; bestDist = d; why = "自爆位置"; }
            }
            if (best != null) return EnemyCnOf(best);

            // ② 房间里还站着的同类怪：自爆的那只已经没了，只能取最近的一只
            Type hmType = FindType("HealthManager");
            if (hmType == null) return null;
            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); }
            catch { return null; }
            if (all == null || all.Length == 0) return null;

            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null || hm.gameObject == null) continue;

                string raw = SqueezeSpaces(StripTrailingNumber(CleanName(hm.gameObject.name)));
                bool hit = false;
                for (int k = 1; k < row.Length && !hit; k++)
                    if (raw.StartsWith(row[k], StringComparison.OrdinalIgnoreCase)) hit = true;
                if (!hit) continue;

                float d = Vector3.Distance(at, hm.transform.position);
                if (d > 60f) continue;              // 同房间内的都算，更远的已经是别的房间
                if (best == null || d < bestDist) { best = raw; bestDist = d; why = "房间内同类"; }
            }
            return best != null ? EnemyCnOf(best) : null;
        }

        private static string EnemyCnOf(string raw)
        {
            string cn;
            return EnemyCnHit(raw, out cn) ? cn : raw;
        }

        /// <summary>
        /// 每 0.4 秒扫一眼场景里「会自爆的怪」，记下它们最后站的位置。
        /// 这类怪自爆时直接销毁，不走 HealthManager.Die，所以敌人死亡记录里根本没有它们；
        /// 但炸出来的 Gas Explosion 就落在自爆点上，用「刚消失 + 位置重合」能准确认人。
        /// </summary>
        private void WatchBurstEnemies()
        {
            float now = Time.realtimeSinceStartup;
            if (now - sBurstWatchAt < 0.4f) return;
            sBurstWatchAt = now;

            try
            {
                Type hmType = FindType("HealthManager");
                if (hmType == null) return;
                UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(hmType);
                if (all == null) return;

                sBurstSeen.Clear();
                for (int i = 0; i < all.Length; i++)
                {
                    Component hm = all[i] as Component;
                    if (hm == null || hm.gameObject == null) continue;

                    string raw = SqueezeSpaces(StripTrailingNumber(CleanName(hm.gameObject.name)));
                    bool hit = false;
                    for (int k = 0; k < sBurstEnemyPrefixes.Length && !hit; k++)
                        if (raw.StartsWith(sBurstEnemyPrefixes[k], StringComparison.OrdinalIgnoreCase)) hit = true;
                    if (!hit) continue;

                    int id = hm.gameObject.GetInstanceID();
                    sBurstSeen.Add(id);
                    BurstWatch w;
                    w.name = raw;
                    w.pos = hm.transform.position;
                    w.at = now;
                    sBurstLive[id] = w;
                }

                // 上一拍还在、这一拍没了 = 刚自爆（或被击杀），把最后位置留 8 秒给爆炸溯源
                List<int> gone = null;
                foreach (KeyValuePair<int, BurstWatch> kv in sBurstLive)
                {
                    if (sBurstSeen.Contains(kv.Key)) continue;
                    if (gone == null) gone = new List<int>();
                    gone.Add(kv.Key);
                }
                if (gone != null)
                {
                    for (int i = 0; i < gone.Count; i++)
                    {
                        BurstWatch w = sBurstLive[gone[i]];
                        w.at = now;
                        sBurstGone.Add(w);
                        sBurstLive.Remove(gone[i]);
                    }
                }

                for (int i = sBurstGone.Count - 1; i >= 0; i--)
                    if (now - sBurstGone[i].at > 20f) sBurstGone.RemoveAt(i);
            }
            catch { }
        }

        /// <summary>
        /// 反查「刚刚在附近炸开的怪」：2.5 秒内、10 单位内最近的一条敌人死亡记录。
        /// 找不到返回 null —— 那就老实记成「爆炸」，绝不瞎猜成一个具体怪物名。
        /// </summary>
        private static string BurstOwner(float maxAge, float maxDist, out float bestDist)
        {
            bestDist = -1f;
            string best = null;

            object hero = GetHero();
            Component hc = hero as Component;
            if (hc == null) return null;
            Vector3 hp = hc.transform.position;

            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < sEnemyDeaths.Length; i++)
            {
                EnemyDeathRec r = sEnemyDeaths[i];
                if (r.name == null) continue;
                if (now - r.at > maxAge) continue;

                float d = Vector3.Distance(hp, r.pos);
                if (d > maxDist) continue;
                if (best == null || d < bestDist) { best = r.name; bestDist = d; }
            }
            return best;
        }

        private static void OnTakeDamage(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;

                object attacker = (sSourceArgIndex >= 0 && sSourceArgIndex < __args.Length)
                    ? __args[sSourceArgIndex]
                    : __args[0];

                int hazard = 0;
                string hazardEnumName = null;
                if (sHazardArgIndex >= 0 && sHazardArgIndex < __args.Length && __args[sHazardArgIndex] != null)
                {
                    hazard = Convert.ToInt32(__args[sHazardArgIndex]);
                    if (hazard != 0) hazardEnumName = ResolveHazardName(__args[sHazardArgIndex], hazard);
                }

                // 判定顺序（越靠前越可信）：
                //   1. 攻击者身上（或父链上）有 HealthManager → 活物，直接就是怪物名（撞人/近战）
                //   2. 生成归属表：这发子弹/这团爆炸是谁放出来的 → 直接就是怪物名（远程/自爆）
                //   3. 特效物体自己的 FSM 变量里挂着主人 → 怪物名
                //   4. 爆炸类再试「附近刚死的会爆的怪」—— 兜底，会猜错，所以排在后面
                //   5. 名字像陷阱（尖刺/酸水/电锯…）
                //   6. hazardType 枚举兜底 —— 极不可靠，见下
                //   7. 什么都不像 → 环境伤害
                //
                // 关键教训：hazardType 参数不能用来区分「怪物」和「陷阱」。
                // HK 的 DamageHero 组件自带 hazardType 字段且默认值就是 SPIKES(1)，
                // 怪物、爆炸物打你时这个参数也是 1。以前在这里兜底，结果凡是认不出的东西
                // 全被记成「尖刺」——怪物死后炸开的 Gas Explosion 就是这么变成尖刺的。
                string enemyName;
                if (TryGetEnemyName(attacker, out enemyName))
                {
                    sLastKiller = enemyName;
                    sLastDetail = "敌人";
                }
                else
                {
                    GameObject go0 = attacker as GameObject;
                    string n0 = go0 != null ? CleanName(go0.name) : null;
                    // 判定框常常是空名子物体，自己的名字读不出来就往上借一级
                    if (string.IsNullOrEmpty(n0)) n0 = BestName(go0, 5);
                    string effect = EffectCn(n0);

                    // 「这会远程和爆炸的怪太多了」—— 靠猜没用，只能查账：
                    // 攻击物被生成的那一刻，生成它的 FSM 宿主就是凶手，记下来直接对号入座。
                    string how = null;
                    string owner = SpawnOwnerOf(go0, out how);
                    if (owner == null) { owner = FsmRefOwner(go0); if (owner != null) how = "FSM变量"; }

                    // 共享预制体（多个怪共用同一个弹射物名）查不到生成记录时，
                    // 用「出生坐标」反查：弹射物被激活时几乎贴在发射者身上。
                    float birthD = -1f;
                    string birthWhy = null;
                    if (owner == null)
                    {
                        owner = BirthOwner(go0, out birthD, out birthWhy);
                        if (owner != null) how = "出生点(" + birthD.ToString("0.0") + ")";
                    }

                    if (owner != null)
                    {
                        sLastKiller = owner;
                        sLastDetail = "敌人·" + (effect ?? "召唤物");

                        if (sOwnerLogs < 15 && L != null)
                        {
                            sOwnerLogs++;
                            // 对照诊断：出生点结论和「名字词根」结论不一致时都写出来。
                            // 出生点会被贴身的飞怪抢走（Fat Fly 这种既撞人又在你脸边的），
                            // 词根又会被共享预制体骗（Spitter Shot R 被多种怪共用）。
                            // 两条一起看才知道到底是它喷的，还是旁边还有一只远程怪。
                            string extra = "";
                            if (how != null && how.StartsWith("出生点") && !string.IsNullOrEmpty(n0))
                            {
                                float nd2 = -1f;
                                string byRoot2 = NameRootOwner(go0, n0, out nd2);
                                if (!string.IsNullOrEmpty(byRoot2))
                                    extra = " | 词根候选=" + byRoot2
                                        + (nd2 >= 0f ? "(" + nd2.ToString("0.0") + ")" : "");
                                else
                                    extra = " | 词根候选=无";
                            }
                            L.LogInfo("[死亡统计] 攻击物归属 | 物体=" + (n0 ?? "null")
                                + " | 主人=" + owner + " | 依据=" + how + " | 类型=" + effect + extra);
                        }
                    }
                    else if (effect != null)
                    {
                        // 生成记录查不到（不少怪不是走 PlayMaker 的 SpawnObject 出弹的），
                        // 退回命名规律：攻击物名 =「怪名 + 动作后缀」。
                        // Spitter Shot R 的词根是 Spitter → 场上那只阿司匹德猎人就是凶手。
                        float nd = -1f;
                        string byRoot = NameRootOwner(go0, n0, out nd);

                        // 爆炸类还有最后一招：附近刚死的会爆的怪
                        float dist = -1f;
                        string burst = null;
                        string burstWhy = null;
                        if (byRoot == null && (effect == "爆炸" || effect == "毒气"))
                        {
                            // 先查固定映射：Gas Explosion 这种自爆特效只能由自爆怪产生，
                            // 而且自爆怪不走 HealthManager.Die（直接 Destroy），临终记录里根本没有它。
                            burst = BurstEffectOwner(n0, go0, out dist, out burstWhy);
                            if (burst == null)
                            {
                                burst = BurstOwner(2.5f, 12f, out dist);
                                if (burst != null) burstWhy = "临终位置";
                            }
                        }

                        if (byRoot != null)
                        {
                            sLastKiller = byRoot;
                            sLastDetail = "敌人·" + effect;
                        }
                        else if (burst != null)
                        {
                            sLastKiller = burst;
                            sLastDetail = "爆裂";
                        }
                        else
                        {
                            // 最后一招：整个房间只有一种怪。生成记录没拦到（Shockwave Spurt L 是
                            // 躯壳守卫砸地时由池子直接放的，不走 SpawnObject）、名字也推不出主人，
                            // 但房间里就它一只，这发特效只能是它放的。
                            // 有两种以上不同的怪就不猜，宁可记成「冲击波」。
                            string solo = SoloRoomEnemy();
                            if (solo != null)
                            {
                                sLastKiller = solo;
                                sLastDetail = "敌人·" + effect;
                            }
                            else
                            {
                                // 认不出主人就统一记「未知」。以前这里写归类名（冲击波/爆炸/毒气），
                                // 面板上看着像已经查明凶手，其实只是按名字猜的类型。
                                // 归类信息留在 detail 里，日志和存档照样能追。
                                sLastKiller = "未知";
                                sLastDetail = "攻击特效·" + effect;
                            }
                        }

                        if (sBurstLogs < 12 && L != null)
                        {
                            sBurstLogs++;
                            L.LogInfo("[死亡统计] 特效伤害（无归属记录）| 物体=" + (n0 ?? "null")
                                + " | 路径=" + FullPath(go0, 5)
                                + " | 归类=" + effect
                                + " | 出生点=" + (birthWhy ?? "-")
                                + " | 名字溯源=" + (byRoot ?? "无") + (byRoot != null ? "（距离 " + nd.ToString("0.0") + "）" : "")
                                + " | 溯源诊断=" + (sRootDiag ?? "-")
                                + " | 房间怪=" + RoomEnemiesBrief(6)
                                + " | 爆裂猜测=" + (burst ?? "无")
                                + (burst != null ? "（" + burstWhy + "，距离 " + dist.ToString("0.0") + "）" : "")
                                + " | 生成记录条数=" + sSpawnOwner.Count
                                + " | 预制体账=" + sPrefabOwner.Count
                                + (sFsmSpawnHooked ? "" : " | 注意：生成归属未挂上"));
                        }
                    }
                    else
                    {
                        // 不是特效 = 陷阱类触发器。先按名字认（wp_saw 是白宫电锯，
                        // 它身上挂的 DamageHero 把 hazardType 写成了 ACID=2，光看枚举会误报成酸水），
                        // 自己名字认不出就往上找父物体（很多场景的刺是 obj_spikes/Spike Collider 这种子物体）。
                        string byName = TrapNameByChain(go0);

                        if (byName != null)
                        {
                            // 名字带 spike 的不一定是地形里的刺：古尔卡（Plant Turret / PlantShooter）会「吐出坚硬的刺球」，
                            // 那颗刺球名字里也有 spike，但它是个会飞的子弹。
                            // 判据只看物理：地形刺是静态碰撞体（没有刚体 / 刚体是运动学），
                            // 被吐出来的东西有动态刚体并且还在飞。
                            if (IsMovingHazard(go0))
                            {
                                string why = null;
                                string shooter = SpawnOwnerOf(go0, out why);
                                if (shooter == null) { shooter = FsmRefOwner(go0); if (shooter != null) why = "FSM变量"; }
                                if (shooter == null)
                                {
                                    float bd; string bw;
                                    shooter = BirthOwner(go0, out bd, out bw);
                                    if (shooter != null) why = "出生点(" + bd.ToString("0.0") + ")";
                                }
                                // 运行时查不到（例如周期生成动作没拦到），退回「这个物体名是否只属于一种怪」
                                if (shooter == null) { shooter = StaticProjectileOwner(n0); if (shooter != null) why = "物体名唯一"; }
                                // 再退回命名规律（Spike Ball 这类名字全是噪声词，推不出东西，靠上一步）
                                if (shooter == null)
                                {
                                    float nd = -1f;
                                    shooter = NameRootOwner(go0, n0, out nd);
                                    if (shooter != null) why = "名字+距离(" + nd.ToString("0.0") + ")";
                                }

                                if (shooter != null)
                                {
                                    sLastKiller = shooter;
                                    sLastDetail = "敌人·弹射物";
                                }
                                else
                                {
                                    sLastKiller = "未知";
                                    sLastDetail = "攻击物";
                                }

                                if (sUnknownLogs < 60 && L != null)
                                {
                                    sUnknownLogs++;
                                    L.LogInfo("[死亡统计] 可移动伤害物 | 路径=" + FullPath(go0, 5)
                                        + " | 名字判定=" + byName
                                        + " | 主人=" + (shooter ?? "未找到") + (why != null ? "(" + why + ")" : "")
                                        + " | 组件=" + ComponentList(go0, 8));
                                }
                            }
                            else
                            {
                                sLastKiller = byName;
                                sLastDetail = "陷阱";

                                if (byName == "地形/陷阱" && sUnknownLogs < 60 && L != null)
                                {
                                    sUnknownLogs++;
                                    L.LogInfo("[死亡统计] 地形/陷阱判定 | 路径=" + FullPath(go0, 5)
                                        + " | 组件=" + ComponentList(go0, 8));
                                }
                            }
                        }
                        else if (hazard != 0 && hazard != 1)
                        {
                            // 非 SPIKES 的枚举值还有参考价值（酸水/岩浆/深渊）
                            sLastKiller = HazardToCn(hazardEnumName, hazard);
                            sLastDetail = "陷阱";
                        }
                        else
                        {
                            // 有名字但认不出 / 整条父链都没名字 → 老实说不知道。
                            // 真正的刺会被 TrapNameByChain 按名字认出来（Spikes / obj_spikes…），
                            // 踩刺真死又走 DieFromHazard（那条路是权威的），
                            // 所以走到这里的一定是「来路不明的伤害载体」，
                            // 再拿 hazardType 的默认值 SPIKES 判成「尖刺」就是瞎猜 —— 这个兜底已删除。
                            // 认不出来就记「未知」：以前这里会把物体英文名（Spike Ball 之类）直接显示出来，
                            // 既是英文又等于没给答案。原名留在日志里，面板上统一成「未知」。
                            sLastKiller = "未知";
                            sLastDetail = "未识别来源";

                            // 有些弹射物名字不带 spike（比如纯 "Ball"），走不到上面的移动判定，
                            // 这里再给出生点、唯一性映射、命名规律各一次机会。
                            string named = null;
                            {
                                float bd; string bw;
                                named = BirthOwner(go0, out bd, out bw);
                            }
                            if (named == null) named = StaticProjectileOwner(n0);
                            if (named == null)
                            {
                                float nd = -1f;
                                named = NameRootOwner(go0, n0, out nd);
                            }
                            if (named != null)
                            {
                                sLastKiller = named;
                                sLastDetail = "敌人·弹射物";
                            }
                        }
                    }
                }

                sLastHitAt = Time.realtimeSinceStartup;

                // 认不出来源的伤害单独记一笔，不受「前 30 条」限制 —— 这类才是需要追的
                if (sLastDetail != null && sLastDetail.IndexOf("未识别", StringComparison.Ordinal) >= 0
                    && sUnknownLogs < 60 && L != null)
                {
                    sUnknownLogs++;
                    GameObject gu = attacker as GameObject;
                    L.LogInfo("[死亡统计] 认不出来源 | 路径=" + FullPath(gu, 6)
                        + " | hazard=" + hazard
                        + " | 物体名长度=" + (gu != null ? gu.name.Length : -1)
                        + " | 组件=" + ComponentList(gu, 8)
                        + " | 判定=" + sLastKiller);
                }

                if (sDiagCount < 30)
                {
                    sDiagCount++;
                    GameObject g2 = attacker as GameObject;
                    L.LogInfo("[诊断] TakeDamage | 攻击者=" + (g2 != null ? CleanName(g2.name) : "null")
                        + " | 父物体=" + (g2 != null ? ParentChain(g2.transform, 2) : "-")
                        + " | hazard=" + hazard + " | 判定=" + sLastKiller + " / " + sLastDetail);
                }
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 记录伤害来源失败: " + e.Message);
            }
        }

        private static string ParentChain(Transform t, int depth)
        {
            if (t == null) return "-";
            string s = "";
            Transform p = t.parent;
            for (int i = 0; i < depth && p != null; i++)
            {
                s += (s.Length > 0 ? " < " : "") + CleanName(p.name);
                p = p.parent;
            }
            return s.Length > 0 ? s : "-";
        }

        /// <summary>整条父链拼成路径，认不出来源时用它定位攻击物到底是什么</summary>
        private static string FullPath(GameObject go, int depth)
        {
            if (go == null) return "-";
            string s = CleanName(go.name);
            if (string.IsNullOrEmpty(s)) s = "(无名)";
            Transform t = go.transform.parent;
            for (int i = 0; i < depth && t != null; i++)
            {
                string n = CleanName(t.name);
                s += "/" + (string.IsNullOrEmpty(n) ? "(无名)" : n);
                t = t.parent;
            }
            return s;
        }

        /// <summary>
        /// 沿父链取第一个非空名字。
        /// 怪物的伤害判定框常常是个空名子物体，只看自己的名字什么都读不出来，
        /// 往上借一级往往就是 "Gas Projectile" 或怪物本体名。
        /// </summary>
        private static string BestName(GameObject go, int depth)
        {
            if (go == null) return null;
            Transform t = go.transform;
            for (int i = 0; i < depth && t != null; i++)
            {
                string n = CleanName(t.name);
                if (!string.IsNullOrEmpty(n) && n != "New Game Object") return n;
                t = t.parent;
            }
            return null;
        }

        /// <summary>攻击物身上挂了哪些组件（只在认不出时打日志，用来判断它到底是什么）</summary>
        private static string ComponentList(GameObject go, int max)
        {
            if (go == null) return "-";
            try
            {
                Component[] cs = go.GetComponents(typeof(Component));
                if (cs == null) return "-";
                string s = "";
                for (int i = 0; i < cs.Length && i < max; i++)
                {
                    if (cs[i] == null) continue;
                    s += (s.Length > 0 ? "," : "") + cs[i].GetType().Name;
                }
                return s.Length > 0 ? s : "-";
            }
            catch { return "-"; }
        }

        /// <summary>
        /// 这个伤害物是「会飞的东西」还是「地形」。
        /// 只看物理：地形里的刺是静态碰撞体（没有刚体，或刚体是运动学），
        /// 被怪吐出来的刺球有动态刚体。名字里都带 spike，但物理特征完全不同。
        /// </summary>
        private static bool IsMovingHazard(GameObject go)
        {
            if (go == null) return false;

            Transform t = go.transform;
            for (int level = 0; level < 3 && t != null; level++)
            {
                Component[] cs = null;
                try { cs = t.gameObject.GetComponents(typeof(Component)); } catch { }
                if (cs != null)
                {
                    foreach (Component c in cs)
                    {
                        if (c == null) continue;
                        string tn;
                        try { tn = c.GetType().Name; } catch { continue; }
                        if (tn != "Rigidbody2D") continue;

                        object bt = GetMember(c, "bodyType");
                        string bts = bt != null ? bt.ToString() : "";
                        if (bts == "Kinematic" || bts == "Static") return false;

                        object v = GetMember(c, "linearVelocity", "velocity");
                        if (v != null)
                        {
                            object sqm = GetMember(v, "sqrMagnitude");
                            float sq = 0f;
                            try { sq = sqm != null ? Convert.ToSingle(sqm) : 0f; } catch { }
                            if (sq > 0.0025f) return true;   // 超过 0.05 单位/秒 = 在飞
                        }
                        // 动态刚体但这一刻是静止的（比如贴脸刚生成），也按攻击物算
                        return true;
                    }
                }
                t = t.parent;
            }
            return false;
        }

        // ================= 攻击物生成归属 =================

        /// <summary>
        /// 拦 PlayMaker 里所有 "SpawnObject*" 动作的 OnEnter。
        /// PlayMaker 程序集可能比本插件晚加载，所以失败不报错，由 Update 定时重试。
        /// </summary>
        private int TryHookFsmSpawns()
        {
            if (sFsmSpawnHooked) return 0;
            MethodInfo hook = typeof(DeathStatsPlugin).GetMethod("OnFsmSpawn", BindingFlags.Static | BindingFlags.NonPublic);
            if (hook == null) return 0;

            List<Type> found = new List<Type>();

            // 1) 直接按类名取。绝对不能依赖 GetTypes()：Assembly-CSharp 里有一堆依赖缺失的类型，
            //    GetTypes() 会整体抛异常，旧写法 catch → continue 把整个程序集吞掉了
            //    （日志里既没有成功也没有失败，说明根本没走到 Patch）。
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = null;
                try { an = asm.GetName().Name; } catch { }
                if (an != "Assembly-CSharp") continue;

                foreach (string full in SpawnActionNames)
                {
                    Type t = null;
                    try { t = asm.GetType(full, false); } catch { }
                    if (t == null)
                    {
                        string shortName = full.Substring(full.LastIndexOf('.') + 1);
                        try { t = asm.GetType(shortName, false); } catch { }
                    }
                    if (t != null && !found.Contains(t)) found.Add(t);
                }
            }

            // 2) 兜底扫描：不依赖上面那张名单 —— 只要名字像「生成」且确实继承 FsmStateAction 就挂。
            //    以前这段只在 found.Count==0 时跑，而且用 catch{continue;} 把异常吞了，
            //    结果漏掉了所有非 SpawnObject 的动作；现在始终跑，异常也写进日志。
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string an = null;
                try { an = asm.GetName().Name; } catch { }
                if (an != "Assembly-CSharp" && an != "Assembly-CSharp-firstpass") continue;

                Type[] types = null;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                    if (L != null)
                        L.LogInfo("[死亡统计] " + an + " 类型枚举部分失败（缺 " 
                            + (e.LoaderExceptions != null ? e.LoaderExceptions.Length : 0) + " 个依赖，忽略）");
                }
                catch (Exception e)
                {
                    if (L != null)
                        L.LogWarning("[死亡统计] " + an + " 类型枚举失败: " + e.GetType().Name + " " + e.Message);
                    continue;
                }
                if (types == null) continue;

                foreach (Type t in types)
                {
                    if (t == null) continue;
                    string tn = t.Name;
                    bool looksSpawn = tn.StartsWith("Spawn", StringComparison.Ordinal)
                        || tn.StartsWith("CreateObject", StringComparison.Ordinal);
                    if (!looksSpawn) continue;
                    if (!HasFsmField(t)) continue;
                    if (!found.Contains(t)) found.Add(t);
                }
            }

            int count = 0;
            foreach (Type t in found)
            {
                // 必须找「本类声明的」入口方法：继承来的 FsmStateAction.OnEnter 是空方法，
                // 打上去等于没打（HarmonyX 会警告 "Patch the declared method …"）。
                //
                // 入口不止 OnEnter：SpawnObjectFromGlobalPoolOverTime（古尔卡吐刺球、周期性生成）
                // 只声明了 Reset + OnUpdate，没有 OnEnter。v0.8.8 只认 OnEnter 把它跳过了，
                // 于是「Spike Ball」从来没有生成记录 → 认不出主人。
                MethodInfo m = null;
                string entry = null;
                string[] entries = { "OnEnter", "OnUpdate", "OnFixedUpdate", "OnLateUpdate" };
                foreach (string en in entries)
                {
                    try
                    {
                        MethodInfo cand = t.GetMethod(en,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        if (cand != null) { m = cand; entry = en; break; }
                    }
                    catch { }
                }

                if (m == null)
                {
                    if (L != null)
                        L.LogWarning("[死亡统计] " + t.Name + " 没有可拦的入口，跳过 | 基类="
                            + (t.BaseType != null ? t.BaseType.FullName : "?")
                            + " | 本类方法=" + DumpMethods(t));
                    continue;
                }

                try
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(hook));
                    count++;
                    L.LogInfo("[死亡统计] 已记录攻击物生成：" + t.FullName + "." + entry);
                    if (string.IsNullOrEmpty(sSpawnFieldDump))
                    {
                        sSpawnFieldDump = DumpFields(t);
                        L.LogInfo("[死亡统计] SpawnObject 字段: " + sSpawnFieldDump);
                    }
                }
                catch (Exception e)
                {
                    L.LogWarning("[死亡统计] 拦 " + t.Name + "." + entry + " 失败: " + e.Message);
                }
            }

            if (count > 0) sFsmSpawnHooked = true;
            else if (!sSpawnNoTypeLogged && L != null)
            {
                sSpawnNoTypeLogged = true;
                L.LogWarning("[死亡统计] 未找到 SpawnObject 动作类，攻击物归属暂不可用（60 秒内继续重试）");
            }
            return count;
        }

        /// <summary>把动作类的字段列出来，字段名对不上时靠这条日志定位</summary>
        private static string DumpFields(Type t)
        {
            string s = "";
            try
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    s += (s.Length > 0 ? "," : "") + f.FieldType.Name + " " + f.Name;
            }
            catch { }
            return s;
        }

        /// <summary>列出本类自己声明的方法名（不含继承来的），用于定位真正的动作入口</summary>
        private static string DumpMethods(Type t)
        {
            string s = "";
            try
            {
                foreach (MethodInfo mi in t.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (mi.IsSpecialName) continue;
                    s += (s.Length > 0 ? "," : "") + mi.Name;
                }
            }
            catch { }
            return s.Length > 0 ? s : "-";
        }

        // ================= 伤害物出生点 =================
        // 共享预制体是死结：Spitter Shot R 这种名字被好几种「会喷东西」的怪共用
        // （实测苍绿之径 Fungus1_19 里根本没有阿司匹德猎人，却被打中三发），
        // 光靠名字必然认错。但弹射物被激活的那一刻，坐标几乎就贴在发射者身上——
        // 酸液在奥波嘴边生成、刺球在古尔卡嘴里生成。用出生点反查，比用命中点反查准得多。

        private int TryHookHazardBirth()
        {
            if (sBirthHooked) return 0;
            MethodInfo hook = typeof(DeathStatsPlugin).GetMethod("OnHazardEnable", BindingFlags.Static | BindingFlags.NonPublic);
            if (hook == null) return 0;

            Type dh = FindType("DamageHero");
            if (dh == null)
            {
                if (L != null) L.LogWarning("[死亡统计] 找不到 DamageHero，伤害物出生点不可用");
                return 0;
            }

            MethodInfo m = null;
            string entry = null;
            string[] tries = { "OnEnable", "OnDisable", "Awake", "Start" };
            foreach (string en in tries)
            {
                try
                {
                    MethodInfo cand = dh.GetMethod(en,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (cand != null) { m = cand; entry = en; break; }
                }
                catch { }
            }

            if (m == null)
            {
                if (L != null)
                    L.LogWarning("[死亡统计] DamageHero 未声明 OnEnable，出生点账不可用 | 本类方法=" + DumpMethods(dh));
                return 0;
            }

            try
            {
                harmony.Patch(m, postfix: new HarmonyMethod(hook));
                sBirthHooked = true;
                if (L != null) L.LogInfo("[死亡统计] 已记录伤害物出生点：DamageHero." + entry);
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 拦 DamageHero." + entry + " 失败: " + e.Message);
            }
            return sBirthHooked ? 1 : 0;
        }

        /// <summary>DamageHero 被启用（从池里取出/重新激活）时记一笔出生坐标</summary>
        private static void OnHazardEnable(object __instance)
        {
            try
            {
                Component c = __instance as Component;
                if (c == null) return;
                GameObject go = c.gameObject;
                if (go == null) return;

                if (sBirth.Count > 600) PruneBirth();
                sBirth[go.GetInstanceID()] = new BirthRec
                {
                    name = CleanName(go.name),
                    pos = go.transform.position,
                    at = Time.realtimeSinceStartup,
                    go = go,
                    resampled = false
                };
            }
            catch { }
        }

        /// <summary>
        /// 池化伤害物的「出生点」要等一帧才准：OnEnable 触发时物体还在对象池的位置上
        /// （冲击波 Shockwave Spurt L 就是这么错过的 —— 实际出生坐标离怪 7 格以上，
        /// 4 格判定自然扑空）。这里在启用后约 60 毫秒补采一次，此时 FSM 已把它瞬移到发射点。
        /// </summary>
        private static void RefreshBirthPositions()
        {
            if (sBirth.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            List<int> keys = null;
            foreach (KeyValuePair<int, BirthRec> kv in sBirth)
            {
                float age = now - kv.Value.at;
                if (age < 0.06f) continue;       // 太早，可能还没瞬移
                if (age > 0.6f || kv.Value.resampled) continue;   // 已经采过 / 太久不管

                if (keys == null) keys = new List<int>();
                keys.Add(kv.Key);
            }
            if (keys == null) return;

            for (int i = 0; i < keys.Count; i++)
            {
                BirthRec r;
                if (!sBirth.TryGetValue(keys[i], out r)) continue;

                r.resampled = true;
                try
                {
                    if (r.go != null) r.pos = r.go.transform.position;
                }
                catch { }
                sBirth[keys[i]] = r;
            }
        }

        private static void PruneBirth()
        {
            float now = Time.realtimeSinceStartup;
            List<int> drop = new List<int>();
            foreach (KeyValuePair<int, BirthRec> kv in sBirth)
                if (now - kv.Value.at > 90f) drop.Add(kv.Key);
            foreach (int k in drop) sBirth.Remove(k);
            if (sBirth.Count > 500) sBirth.Clear();
        }

        /// <summary>
        /// 按出生点反查主人：取出生坐标 4 个单位内最近的活物。
        /// 附近不止一只、且两只距离接近时不判（宁可不写名字，也不写错名字）。
        /// </summary>
        private static string BirthOwner(GameObject go, out float dist, out string detail)
        {
            dist = -1f;
            detail = null;
            if (go == null) { detail = "无物体"; return null; }

            BirthRec b;
            bool has = sBirth.TryGetValue(go.GetInstanceID(), out b);
            if (!has)
            {
                // 打到玩家的可能是判定框子物体，出生记录记在带 DamageHero 的那一层上，
                // 往上找两级（命中点通常就挂在投掷物本体下面）。
                Transform pt = go.transform != null ? go.transform.parent : null;
                for (int i = 0; i < 2 && pt != null && !has; i++)
                {
                    has = sBirth.TryGetValue(pt.gameObject.GetInstanceID(), out b);
                    pt = pt.parent;
                }
            }
            if (!has) { detail = "无出生记录"; return null; }

            float age = Time.realtimeSinceStartup - b.at;
            if (age > 90f) { detail = "出生记录过旧"; return null; }

            Type hmType = FindType("HealthManager");
            if (hmType == null) { detail = "无 HealthManager"; return null; }

            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); }
            catch { detail = "枚举失败"; return null; }
            if (all == null || all.Length == 0) { detail = "场上无怪"; return null; }

            string bestName = null;
            float best = -1f;
            string nearestName = null;
            float nearest = -1f;
            List<string> tieNames = new List<string>();

            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null) continue;
                GameObject eo = hm.gameObject;
                if (eo == null) continue;

                float d = Vector3.Distance(b.pos, eo.transform.position);
                string en = StripTrailingNumber(StripVariantSuffix(CleanName(eo.name)));

                // 不管在不在 4 格内，都留一个"最近是谁、多远"，认不出来时写进日志
                if (nearest < 0f || d < nearest) { nearest = d; nearestName = en; }

                if (d > 4f) continue;                       // 出生点 4 个单位内才算「贴着它出生」

                // 多只候选不再放弃：取离出生点最近的那只。只有距离真的打平（两只叠在一起）
                // 才随机挑一个 —— 这时候没有任何信息能区分，随机至少不会总是冤枉同一只。
                if (best < 0f || d < best - 0.01f)
                {
                    best = d;
                    bestName = en;
                    tieNames.Clear();
                    tieNames.Add(en);
                }
                else if (Mathf.Abs(d - best) <= 0.01f)
                {
                    tieNames.Add(en);
                }
            }

            if (bestName == null)
            {
                detail = nearest >= 0f
                    ? "出生点附近无怪(最近 " + nearest.ToString("0.0") + " " + nearestName + ")"
                    : "出生点附近无怪";
                return null;
            }

            dist = best;
            if (tieNames.Count > 1)
            {
                bestName = tieNames[UnityEngine.Random.Range(0, tieNames.Count)];
                detail = "出生点打平(" + tieNames.Count + "选1)";
            }
            else
            {
                detail = "出生点";
            }

            string cn;
            return EnemyCnHit(bestName, out cn) ? cn : bestName;
        }

        /// <summary>房间里现有怪物的简表（名字@距离），认不出来源时写进日志</summary>
        private static string RoomEnemiesBrief(int max)
        {
            Type hmType = FindType("HealthManager");
            if (hmType == null) return "-";
            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); } catch { return "-"; }
            if (all == null || all.Length == 0) return "-";

            Component heroC = GetHero() as Component;
            Vector3 from = heroC != null ? heroC.transform.position : Vector3.zero;

            List<KeyValuePair<float, string>> list = new List<KeyValuePair<float, string>>();
            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null || hm.gameObject == null) continue;
                float d = Vector3.Distance(from, hm.transform.position);
                list.Add(new KeyValuePair<float, string>(d, CleanName(hm.gameObject.name)));
            }
            list.Sort(delegate (KeyValuePair<float, string> a, KeyValuePair<float, string> b) { return a.Key.CompareTo(b.Key); });

            string s = "";
            for (int i = 0; i < list.Count && i < max; i++)
                s += (s.Length > 0 ? ", " : "") + list[i].Value + "@" + list[i].Key.ToString("0.0");
            return s.Length > 0 ? s : "-";
        }

        /// <summary>
        /// 房间里只有一种敌人时返回它的名字，否则返回 null。
        /// 专门给「特效查不到主人」兜底：Crossroads_48 只有一只躯壳守卫，
        /// 它砸地放出来的 Shockwave Spurt L 生成记录没拦到、名字也推不出主人，
        /// 但整个房间就它一只，凶手只能是它。有两种以上就不猜。
        /// </summary>
        private static string SoloRoomEnemy()
        {
            Type hmType = FindType("HealthManager");
            if (hmType == null) return null;
            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); } catch { return null; }
            if (all == null || all.Length == 0) return null;

            string only = null;
            string onlyKey = null;
            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null || hm.gameObject == null) continue;

                string n = CleanName(hm.gameObject.name);
                if (string.IsNullOrEmpty(n)) continue;
                string key = EnemyKey(n);
                if (string.IsNullOrEmpty(key)) continue;

                if (only == null) { only = n; onlyKey = key; }
                else if (onlyKey != key) return null;    // 不止一个品种，不猜
            }
            return only;
        }

        /// <summary>类型（含基类）里有没有 Fsm 字段 —— 用来确认这是个 PlayMaker 动作</summary>
        private static bool HasFsmField(Type t)
        {
            for (Type cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
            {
                if (cur.GetField("Fsm", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// SpawnObject* 动作的 postfix：记「谁生成了什么」。
        /// 只记生成者身上有血条的（怪物），玩家/UI/粒子/管理器生成的东西全部忽略。
        ///
        /// 记两本账：
        ///   实例账（sSpawnOwner）—— 动作把生成结果存到 storeObject 时才有，精确但覆盖不全；
        ///   预制体账（sPrefabOwner）—— 只按「预制体名 → 谁放的」记，不需要拿到实例，
        ///                              覆盖最广（HK 的生成动作多数是池化的、不回存实例）。
        ///                              同一个预制体被多个敌人用时标记为"混用"，宁可不判也不乱判。
        /// </summary>
        private static void OnFsmSpawn(object __instance)
        {
            try
            {
                if (__instance == null) return;

                // OnUpdate 型动作每帧都会进来，同一动作 0.5 秒内只处理一次
                int aid = 0;
                try { aid = __instance.GetHashCode(); } catch { }
                float tick = Time.realtimeSinceStartup;
                if (aid != 0)
                {
                    float lastTick;
                    if (sSpawnTick.TryGetValue(aid, out lastTick) && tick - lastTick < 0.5f) return;
                    if (sSpawnTick.Count > 600) sSpawnTick.Clear();
                    sSpawnTick[aid] = tick;
                }

                object fsm = GetMember(__instance, "Fsm");
                if (fsm == null) return;
                GameObject ownerGo = GetMember(fsm, "GameObject", "Owner") as GameObject;

                float now = Time.realtimeSinceStartup;
                string prefabName = ReadPrefabName(__instance);
                string actionName = "?";
                try { actionName = __instance.GetType().Name; } catch { }

                string ownerName = null;
                bool isEnemy = ownerGo != null && TryGetEnemyName(ownerGo, out ownerName);

                // 诊断：每个预制体只记第一次。**连宿主不是怪物而被跳过的也记** ——
                // 共享弹射物（Spitter Shot R 这类）究竟是哪个怪放的，只能靠这条线索定位。
                if (L != null && !string.IsNullOrEmpty(prefabName)
                    && sSpawnSeen.Add(prefabName) && sSpawnSeen.Count <= 80)
                {
                    L.LogInfo("[死亡统计] 生成动作 | 动作=" + actionName
                        + " | 预制体=" + prefabName
                        + " | FSM宿主=" + (ownerGo != null ? CleanName(ownerGo.name) : "null")
                        + " | 宿主路径=" + (ownerGo != null ? FullPath(ownerGo, 3) : "-")
                        + " | 记入=" + (isEnemy ? "是→" + ownerName : "否(宿主不是怪)"));
                }

                if (!isEnemy)
                {
                    // 宿主不是怪，不代表查不到主人：怪放出来的东西还会再放东西。
                    // 真菌荒地的例子：斯孢格 → 孢子炸弹（Spore Bomb）→ 毒气爆炸（Gas Explosion Recycle）。
                    // 最后一层是「孢子炸弹」放的，它自己不是怪，但只要它身上带着上一层的生成记录，
                    // 就把那个主人继承下来，两层、三层都能一路追到怪身上。
                    string up = null;
                    string upHow = null;
                    if (ownerGo != null) up = SpawnOwnerOf(ownerGo, out upHow);
                    if (up == null) return;      // 真查不到，不记

                    ownerName = up;
                    isEnemy = true;

                    if (sInheritLogs < 12 && L != null && !string.IsNullOrEmpty(prefabName))
                    {
                        sInheritLogs++;
                        L.LogInfo("[死亡统计] 生成继承 | 预制体=" + prefabName
                            + " | 宿主=" + CleanName(ownerGo.name) + "（不是怪）"
                            + " | 往上追到=" + up + "（" + (upHow ?? "?") + "）");
                    }
                }

                // ---- 预制体账：不需要拿到实例，只要动作里那个 gameObject 字段（被生成的预制体） ----
                if (!string.IsNullOrEmpty(prefabName))
                {
                    NotePrefabOwner(prefabName, ownerName, now);
                    // 同一种爆炸有 L/M/R 三份预制体，只记到「这次被激活的那一份」，
                    // 下次挨到另一种变体就查不到。顺手把去掉尾巴的通用名也记一份。
                    string prefabBase = VariantBase(prefabName);
                    if (prefabBase.Length > 0
                        && !string.Equals(prefabBase, prefabName, StringComparison.OrdinalIgnoreCase))
                        NotePrefabOwner(prefabBase, ownerName, now);
                }

                // ---- 实例账：动作回存了实例才记 ----
                GameObject clone = ReadSpawned(__instance);
                if (clone != null)
                {
                    if (sSpawnOwner.Count > 400) PruneSpawns();
                    sSpawnOwner[clone.GetInstanceID()] = new SpawnRec { owner = ownerName, at = now };
                }

                if (!sSpawnFirstLogged && (clone != null || prefabName != null))
                {
                    sSpawnFirstLogged = true;
                    if (L != null)
                        L.LogInfo("[死亡统计] 生成归属生效：首条记录 预制体=" + (prefabName ?? "(取不到)")
                            + " → 主人=" + ownerName
                            + " | 预制体账=" + (prefabName != null ? "有" : "无")
                            + " | 实例账=" + (clone != null ? "有" : "无"));
                }
            }
            catch { }
        }

        /// <summary>
        /// 把 FsmGameObject / FsmOwnerDefault / FsmObject 这些包装解成真实物体。
        /// SpawnObjectFromGlobalPool 里要生成的预制体是 FsmOwnerDefault（不是 FsmGameObject），
        /// 旧版只认 FsmGameObject，所以即便钩子挂上了也读不出预制体名。
        /// </summary>
        private static GameObject DerefGameObject(object fv)
        {
            if (fv == null) return null;

            GameObject direct = fv as GameObject;
            if (direct != null) return direct;

            string tn;
            try { tn = fv.GetType().Name; } catch { return null; }

            if (tn == "FsmGameObject" || tn == "FsmObject")
            {
                object v = GetMember(fv, "Value");
                return v as GameObject;
            }
            if (tn == "FsmOwnerDefault")
            {
                object inner = GetMember(fv, "GameObject", "gameObject");
                GameObject g = DerefGameObject(inner);
                if (g != null) return g;
                return inner as GameObject;
            }
            return null;
        }

        /// <summary>取 SpawnObject 动作里「要生成的预制体」的名字</summary>
        private static string ReadPrefabName(object action)
        {
            Type t = action.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string n = f.Name.ToLowerInvariant();
                // storeObject=生成结果；spawnPoint/position/rotation/timer=坐标与计时；owner=宿主
                if (n.Contains("store") || n.Contains("spawn") || n.Contains("position")
                    || n.Contains("rotation") || n.Contains("owner") || n.Contains("timer")) continue;

                object fv;
                try { fv = f.GetValue(action); } catch { continue; }
                GameObject g = DerefGameObject(fv);
                if (g != null) return CleanName(g.name);
            }
            return null;
        }

        /// <summary>从 SpawnObject 系列动作里取出刚生成的物体（不同版本字段名不一样，挨个试）</summary>
        private static GameObject ReadSpawned(object action)
        {
            Type t = action.GetType();
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                string n = f.Name.ToLowerInvariant();
                try
                {
                    if (f.FieldType == typeof(GameObject))
                    {
                        if (n.Contains("spawn") || n == "clone" || n == "objectinstance")
                            return f.GetValue(action) as GameObject;
                    }
                    else if (f.FieldType.Name == "FsmGameObject")
                    {
                        // storeObject / storeObjectVariable —— prefab 那个字段叫 gameObject，不会误命中
                        if (!n.Contains("store") && !n.Contains("spawn")) continue;
                        object fv = f.GetValue(action);
                        if (fv == null) continue;
                        GameObject g = GetMember(fv, "Value") as GameObject;
                        if (g != null) return g;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>清掉太老的生成记录，避免池化物体复用后对不上号</summary>
        private static void PruneSpawns()
        {
            float now = Time.realtimeSinceStartup;
            List<int> dead = null;
            foreach (KeyValuePair<int, SpawnRec> kv in sSpawnOwner)
            {
                if (now - kv.Value.at < 20f) continue;
                if (dead == null) dead = new List<int>();
                dead.Add(kv.Key);
            }
            if (dead != null) for (int i = 0; i < dead.Count; i++) sSpawnOwner.Remove(dead[i]);
            if (sSpawnOwner.Count == 0) sSpawnOwner.Clear();
        }

        /// <summary>
        /// 查「这东西是谁放出来的」。
        ///   1. 实例账：攻击物自己或两级父物体在生成表里 → 精确
        ///   2. 预制体账：按物体名查「这种特效平时是谁放的」→ 覆盖广，但同类特效被多种敌人用过时不判
        /// 两本账都只认 20 秒内的记录（池化物体久不用会被别人覆盖，太久的不算数）。
        /// </summary>
        /// <summary>把「某个预制体是谁放的」记进预制体账；被多种怪用过就标 mixed。</summary>
        private static void NotePrefabOwner(string prefabName, string ownerName, float now)
        {
            if (string.IsNullOrEmpty(prefabName) || string.IsNullOrEmpty(ownerName)) return;

            PrefabRec pr;
            if (!sPrefabOwner.TryGetValue(prefabName, out pr))
            {
                pr = new PrefabRec { owner = ownerName, at = now, mixed = false };
                pr.owners = new List<string>();
                pr.owners.Add(ownerName);
                sPrefabOwner[prefabName] = pr;
                return;
            }

            if (pr.owners == null) pr.owners = new List<string>();
            if (!pr.owners.Contains(ownerName))
            {
                if (pr.owners.Count < 8) pr.owners.Add(ownerName);
                pr.mixed = true;
            }
            if (!string.IsNullOrEmpty(pr.owner) && pr.owner != ownerName) pr.mixed = true;
            pr.at = now;
            sPrefabOwner[prefabName] = pr;
        }

        private static string SpawnOwnerOf(GameObject go, out string how)
        {
            how = null;
            if (go == null) return null;

            float now = Time.realtimeSinceStartup;
            Transform t = go.transform;
            for (int i = 0; i < 3 && t != null; i++)
            {
                SpawnRec r;
                if (sSpawnOwner.TryGetValue(t.gameObject.GetInstanceID(), out r))
                {
                    if (now - r.at <= 20f && !string.IsNullOrEmpty(r.owner))
                    {
                        how = "生成记录";
                        return r.owner;
                    }
                }
                t = t.parent;
            }

            // 实例账查不到，按名字查预制体账
            t = go.transform;
            for (int i = 0; i < 3 && t != null; i++)
            {
                string nm = CleanName(t.name);
                if (!string.IsNullOrEmpty(nm))
                {
                    PrefabRec pr;
                    bool found = sPrefabOwner.TryGetValue(nm, out pr);
                    if (!found)
                    {
                        // 名字末位的 L/M/R 是左右中变体，打平以后再查一次（见 VariantBase）
                        string vb = VariantBase(nm);
                        if (vb.Length > 0 && !string.Equals(vb, nm, StringComparison.OrdinalIgnoreCase))
                            found = sPrefabOwner.TryGetValue(vb, out pr);
                    }
                    if (found)
                    {
                        if (now - pr.at <= 30f)
                        {
                            if (!pr.mixed && !string.IsNullOrEmpty(pr.owner))
                            {
                                how = "预制体名";
                                return pr.owner;
                            }
                            // 这个预制体被多种怪用过：不再放弃，取场上离生成物最近的那只实例。
                            // 一只都不在场（怪已死 / 不在同一房间）才判不了。
                            if (pr.owners != null && pr.owners.Count > 0)
                            {
                                float od;
                                string near = NearestOwnerInstance(pr.owners, go, out od);
                                if (near != null)
                                {
                                    how = "预制体名(多怪" + pr.owners.Count + "取最近 " + od.ToString("0.0") + ")";
                                    return near;
                                }
                            }
                            how = "预制体名（多怪混用，场上无实例）";
                            return null;
                        }
                    }
                    else if (i == 0)
                    {
                        // 名字不完全一样是常态：池里的预制体叫 Shockwave Wave，
                        // 打到人的那个物体叫 Shockwave Spurt L（左右两侧各有变体）。
                        // 精确查不到就按「首词」再试，多主时同样取最近。
                        string headHow;
                        string byHead = PrefabOwnerByFirstWord(nm, now, go, out headHow);
                        if (byHead != null) { how = headHow; return byHead; }
                        if (headHow != null) { how = headHow; return null; }   // 有歧义，不去猜
                    }
                }
                t = t.parent;
            }
            return null;
        }

        // 弹幕通用词：这些词做首词没有区分度（"Spike Ball" 和 "Shockwave Wave" 的前缀一样不可信）
        private static readonly HashSet<string> sPrefabNoiseHeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "shot", "ball", "spike", "spikes", "bullet", "projectile", "attack", "effect",
            "wave", "spurt", "slash", "swipe", "hit", "impact", "burst", "explosion",
            "explode", "gas", "flame", "fire", "beam", "orb", "rock", "rocks", "shock",
            "enemy", "damage", "hazard"
        };

        /// <summary>
        /// 预制体账的宽松匹配：物体名「首词 + 后缀变体」对预制体名「首词 + 主干」。
        /// 只在首词足够长、不是通用词、且只对应一个主人时返回，否则返回 null 并写明原因。
        /// </summary>
        private static string PrefabOwnerByFirstWord(string objName, float now, GameObject from, out string how)
        {
            how = null;
            if (string.IsNullOrEmpty(objName)) return null;

            int sp = objName.IndexOf(' ');
            if (sp <= 0) return null;                       // 只有一个词，没有可放宽的余地

            string head = objName.Substring(0, sp).Trim();
            if (head.Length < 5) return null;
            if (sPrefabNoiseHeads.Contains(head)) return null;

            List<string> owners = new List<string>();
            foreach (KeyValuePair<string, PrefabRec> kv in sPrefabOwner)
            {
                string key = kv.Key;
                if (!key.StartsWith(head, StringComparison.OrdinalIgnoreCase)) continue;
                if (key.Length > head.Length && key[head.Length] != ' ') continue;
                if (now - kv.Value.at > 30f) continue;

                if (kv.Value.owners != null)
                {
                    for (int i = 0; i < kv.Value.owners.Count; i++) AddOwnerOnce(owners, kv.Value.owners[i]);
                }
                AddOwnerOnce(owners, kv.Value.owner);
            }

            if (owners.Count == 0) return null;
            if (owners.Count == 1)
            {
                how = "预制体名+首词(" + head + ")";
                return owners[0];
            }

            // 首词对上但主人不止一个：取场上离生成物最近的那只，打平随机。
            float nd;
            string near = NearestOwnerInstance(owners, from, out nd);
            if (near == null)
            {
                how = "预制体名+首词(" + head + "，多主" + owners.Count + "，场上无实例)";
                return null;
            }
            how = "预制体名+首词(" + head + "，多主取最近 " + nd.ToString("0.0") + ")";
            return near;
        }

        private static void AddOwnerOnce(List<string> list, string o)
        {
            if (string.IsNullOrEmpty(o)) return;
            for (int i = 0; i < list.Count; i++) if (list[i] == o) return;
            if (list.Count < 8) list.Add(o);
        }

        /// <summary>
        /// 主人候选不止一个时，按「场上离生成物最近的同名实例」定人：
        /// 同一发弹幕确实是同族多只怪里任意一只放的，但贴得最近的那只最可能是发射者。
        /// 距离打平（两只叠在同一点）就随机挑一只 —— 此时没有任何信息可区分。
        /// 一只候选都不在场（已经死了 / 不在同房间）时返回 null，不硬猜。
        /// </summary>
        private static string NearestOwnerInstance(List<string> owners, GameObject from, out float bestDist)
        {
            bestDist = -1f;
            if (owners == null || owners.Count == 0) return null;

            Type hmType = FindType("HealthManager");
            if (hmType == null) return null;
            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); }
            catch { return null; }
            if (all == null || all.Length == 0) return null;

            Vector3 at = from != null ? from.transform.position : Vector3.zero;
            bool haveFrom = from != null;

            string bestName = null;
            float best = -1f;
            List<string> ties = new List<string>();

            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null || hm.gameObject == null) continue;

                string cn = null;
                try { TryCnAlongChain(hm.gameObject, 4, out cn); } catch { }
                string en = StripTrailingNumber(StripVariantSuffix(CleanName(hm.gameObject.name)));

                bool match = false;
                string ek = EnemyKey(en);
                for (int k = 0; k < owners.Count && !match; k++)
                {
                    string o = owners[k];
                    if (string.IsNullOrEmpty(o)) continue;
                    if (cn != null && (o == cn || o == en)) match = true;
                    else if (ek.Length > 0
                        && string.Equals(EnemyKey(StripTrailingNumber(o)), ek, StringComparison.Ordinal)) match = true;
                }
                if (!match) continue;

                float d = haveFrom ? Vector3.Distance(at, hm.transform.position) : 0f;
                string disp = cn ?? en;
                if (best < 0f || d < best - 0.01f)
                {
                    best = d;
                    bestName = disp;
                    ties.Clear();
                    ties.Add(disp);
                }
                else if (Mathf.Abs(d - best) <= 0.01f)
                {
                    ties.Add(disp);
                }
            }

            if (bestName == null) return null;
            bestDist = best;
            if (ties.Count > 1) return ties[UnityEngine.Random.Range(0, ties.Count)];
            return bestName;
        }

        /// <summary>
        /// 退一步：攻击物自身挂的 FSM 变量里，可能还留着主人的引用（有些攻击物是 C# 生成的）。
        /// 扫 GameObject 类型的变量，值身上有血条的就是主人。
        /// </summary>
        private static string FsmRefOwner(GameObject go)
        {
            if (go == null) return null;
            Type fsmType = FindType("PlayMakerFSM");
            if (fsmType == null) return null;

            Transform t = go.transform;
            for (int level = 0; level < 2 && t != null; level++)
            {
                Component[] comps = null;
                try { comps = t.gameObject.GetComponents(fsmType); }
                catch { }
                if (comps != null)
                {
                    foreach (Component c in comps)
                    {
                        object vars = GetMember(c, "FsmVariables", "fsmVariables");
                        if (vars == null) continue;
                        object govars = GetMember(vars, "GameObjectVariables");
                        IEnumerable list = govars as IEnumerable;
                        if (list == null) continue;

                        foreach (object fv in list)
                        {
                            if (fv == null) continue;
                            GameObject g = GetMember(fv, "Value") as GameObject;
                            string nm;
                            if (g != null && g.GetInstanceID() != go.GetInstanceID() && TryGetEnemyName(g, out nm))
                                return nm;
                        }
                    }
                }
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// 攻击特效归类。投射物、爆炸、射线这些物体是「伤害载体」，不是地形，
        /// 一旦掉进 hazardType 兜底就会变成「尖刺」。
        /// 判断顺序有讲究：Gas Explosion 同时含 gas 和 explosion，得先判爆炸。
        /// </summary>
        private static string EffectCn(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return null;
            string n = objName.ToLowerInvariant();

            if (n.Contains("explo") || n.Contains("blast") || n.Contains("burst")
                || n.Contains("boom") || n.Contains("detonat")) return "爆炸";
            if (n.Contains("gas") || n.Contains("cloud") || n.Contains("spore")
                || n.Contains("poison") || n.Contains("fume")) return "毒气";
            if (n.Contains("shock") || n.Contains("wave")) return "冲击波";
            if (n.Contains("beam") || n.Contains("laser") || n.Contains("ray_")) return "光束";
            if (n.Contains("shot") || n.Contains("bullet") || n.Contains("projectile")
                || n.Contains("spit") || n.Contains("missile") || n.Contains("orb")) return "弹幕";
            if (n.Contains("flame") || n.Contains("fire") || n.Contains("burn")) return "火焰";
            if (n.Contains("slash") || n.Contains("sword") || n.Contains("nail_art")) return "斩击";
            return null;
        }

        /// <summary>
        /// 陷阱识别：先看自己，再看两级父物体。很多场景的刺不叫 spike，而是挂在
        /// obj_spikes / Spike Collider 这种父物体下面，只看自己会漏。
        /// </summary>
        /// <summary>
        /// 攻击物名里表示「动作/方向/尺寸」的词，不能拿来推怪物。
        /// 例如 Spitter Shot R 里只有 Spitter 有用，Shot/R 全是噪声。
        /// </summary>
        private static readonly string[] AttackNoiseWords =
        {
            "shot","shots","ball","balls","blast","explosion","explode","exploding","slash","slashes",
            "impact","hit","hitbox","collider","projectile","proj","orb","beam","wave","spike","spikes",
            "spit","spits","gas","puff","dust","spark","sparks","trail","muzzle","flash","burst",
            "ring","rain","drop","dart","needle","swipe","attack","damage","enemy","hero","player",
            "small","big","large","mid","tiny","huge","left","right","up","down","top","bottom",
            "new","clone","spawn","spawned","effect","effects","proj","bullet","missile","bomb",
            "the","and","for","off","out","all","one","two","three","anim","cln","prefab"
        };

        /// <summary>把名字拆成词（小写，丢掉 1-2 字母的方向/序号词，可选丢掉动作噪声词）</summary>
        private static void CollectWords(string s, List<string> outWords, bool dropNoise)
        {
            if (string.IsNullOrEmpty(s)) return;

            int i = 0;
            while (i < s.Length)
            {
                while (i < s.Length && !char.IsLetterOrDigit(s[i])) i++;
                int start = i;
                while (i < s.Length && char.IsLetterOrDigit(s[i])) i++;
                if (i <= start) break;

                string w = s.Substring(start, i - start).ToLowerInvariant();
                if (w.Length < 3) continue;                       // R / L / M2 这类方向、序号
                if (dropNoise && IsNoiseWord(w)) continue;
                if (!outWords.Contains(w)) outWords.Add(w);
            }
        }

        private static bool IsNoiseWord(string w)
        {
            for (int i = 0; i < AttackNoiseWords.Length; i++)
                if (AttackNoiseWords[i] == w) return true;
            return false;
        }

        /// <summary>去掉名字末尾的纯数字词：'Moss Charger 1' → 'Moss Charger'</summary>
        private static string StripTrailingNumber(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;

            int i = s.Length - 1;
            while (i >= 0 && s[i] == ' ') i--;
            int numEnd = i;
            while (i >= 0 && s[i] >= '0' && s[i] <= '9') i--;
            if (i == numEnd) return s;                       // 末尾本来就不是数字
            if (i >= 0 && s[i] == ' ') return s.Substring(0, i).TrimEnd();
            return s;
        }

        /// <summary>
        /// 靠命名规律认凶手：HK 的攻击物几乎都叫「怪名 + 动作后缀」
        /// （Spitter Shot R / Moss Charger Slash / Hopper Explosion）。
        /// 词根对上哪只怪，就在场上找这只怪，取离玩家最近的那个。
        ///
        /// 距离不是硬门槛：远程怪（阿司匹德猎人）就站在屏幕外几十个单位开火，
        /// 用 45 这个上限会把它整片排除掉。所以只有「模糊匹配」（1 分）才限距离，
        /// 名字明确对上的（2-3 分）不看距离 —— 名字是它自己报的，不会认错。
        /// </summary>
        private static string NameRootOwner(GameObject go, string attackName, out float bestDist)
        {
            bestDist = -1f;
            sRootDiag = null;
            if (string.IsNullOrEmpty(attackName)) return null;

            List<string> aw = new List<string>();
            CollectWords(attackName, aw, true);
            if (aw.Count == 0) return null;                       // 全是噪声词，名字推不出东西

            Type hmType = FindType("HealthManager");
            if (hmType == null) return null;

            Vector3 from;
            Component heroC = GetHero() as Component;
            if (heroC != null) from = heroC.transform.position;
            else if (go != null) from = go.transform.position;
            else return null;

            UnityEngine.Object[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType(hmType); }
            catch { return null; }
            if (all == null || all.Length == 0) return null;

            string bestName = null;
            int bestScore = 0;
            int cand = 0;
            float nearD = -1f;
            List<string> ew = new List<string>();
            string ak = EnemyKey(attackName);                  // "mosschargerslash"

            for (int i = 0; i < all.Length; i++)
            {
                Component hm = all[i] as Component;
                if (hm == null) continue;
                GameObject eo = hm.gameObject;
                if (eo == null) continue;

                // 场景里的怪普遍叫 "Spitter (1)" / "Moss Walker (3)"，序号必须先剥掉，
                // 否则 EnemyKey 得到 "spitter1"，前缀匹配失败掉到 1 分，再被距离筛掉 —— 就是 v0.9.2 的病根。
                string en = StripTrailingNumber(StripVariantSuffix(CleanName(eo.name)));
                if (string.IsNullOrEmpty(en)) continue;

                // 分级评分，避免「moss」把 Moss Walker / Moss Charger / Mossfly 混成一堆：
                //   3 = 整名相同；2 = 攻击物名以怪名开头（Moss Charger Slash ← Moss Charger）
                //   1 = 怪名里含有攻击物名的一个词根（Spitter Shot ← 阿司匹德猎人 Spitter）
                string ek = EnemyKey(en);
                int score = 0;
                if (ek.Length > 0 && ek == ak) score = 3;
                else if (ek.Length >= 5 && ak.StartsWith(ek, StringComparison.Ordinal)) score = 2;
                else if (ak.Length >= 5 && ek.StartsWith(ak, StringComparison.Ordinal)) score = 2;
                else
                {
                    ew.Clear();
                    CollectWords(en, ew, false);
                    for (int a = 0; a < aw.Count; a++)
                    {
                        if (aw[a].Length < 4) continue;
                        bool hit = false;
                        for (int b = 0; b < ew.Count && !hit; b++)
                            if (ew[b] == aw[a]) hit = true;
                        if (hit || ek.Contains(aw[a])) { score = 1; break; }
                    }
                }
                if (score == 0) continue;

                float d = Vector3.Distance(from, eo.transform.position);
                cand++;
                if (nearD < 0f || d < nearD) nearD = d;

                // 名字明确对上的（2-3 分）不看距离：远程怪是站在屏幕外开火的，
                // 卡 45 单位会把阿司匹德猎人整片排除。只有模糊匹配（1 分）才限距离。
                float limit = score >= 2 ? 300f : 45f;
                if (d > limit) continue;

                // 同分同距离时随机选一个：两只怪站在同一个点上，任何信息都分不出是谁，
                // 固定取先遍历到的那只等于每次都冤枉同一只。
                if (score > bestScore
                    || (score == bestScore && (bestName == null
                        || d < bestDist - 0.01f
                        || (Mathf.Abs(d - bestDist) <= 0.01f && UnityEngine.Random.value < 0.5f))))
                {
                    bestName = en;
                    bestScore = score;
                    bestDist = d;
                }
            }

            sRootDiag = "候选" + cand + (nearD >= 0f ? "/最近" + nearD.ToString("0.0") : "")
                + "/最高分" + bestScore;

            if (bestName == null)
            {
                // 关键教训：房间里没有这只怪时，**绝不能**靠名字前缀硬指。
                // 苍绿之径 Fungus1_19 里根本没有阿司匹德猎人，可「Spitter Shot R」是多种
                // 「会喷东西的怪」共用的弹射物预制体，前缀匹配硬把它指成阿司匹德猎人——
                // 名字听着合理，事实是错的。前缀候选只写进诊断，不参与落账。
                string st = StaticNameOwner(attackName);
                if (st != null) sRootDiag += "/名字前缀=" + st + "(房间内无此怪，不采用)";
                return null;
            }

            string cn;
            if (EnemyCnHit(bestName, out cn)) return cn;
            return bestName;                                      // 名字对不上表就原样返回，至少比"弹幕"有用
        }

        /// <summary>
        /// 纯静态推断：攻击物名以某个怪物键**开头**（≥5 字母）就归给那个怪。
        /// 不需要场上有实例 —— 名字本身就是它自报家门（Spitter Shot R 的前缀就是 Spitter）。
        /// 只认前缀、不做包含匹配：这样 "Crystal Shot" 不会因为含 crystal 就乱指到别的虫。
        /// </summary>
        private static string StaticNameOwner(string attackName)
        {
            string ak = EnemyKey(attackName);
            if (ak.Length < 6) return null;

            string bestKey = null;
            foreach (string k in EnemyCn.Keys)
            {
                if (k.Length < 5) continue;
                if (!ak.StartsWith(k, StringComparison.Ordinal)) continue;
                if (bestKey == null || k.Length > bestKey.Length) bestKey = k;
            }
            if (bestKey == null) return null;
            return EnemyCn[bestKey];
        }

        /// <summary>
        /// 只属于某一种怪的弹射物 → 直接给主人。
        ///
        /// 能用这张表的门槛很高：资源里名字**全游戏只出现一次**、且和那种怪的预制体在同一文件，
        /// 否则就可能像 Gas Hit Box 那样被多个怪共用，反推必然出错。
        /// 这里只是「生成归属查不到」时的兜底，优先级低于运行时证据。
        /// </summary>
        private static string StaticProjectileOwner(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return null;

            switch (objName.Trim().ToLowerInvariant())
            {
                // 古尔卡（PLANT_SHOOTER / 游戏内 Plant Turret）吐出的刺球。
                // 依据：Spike Ball 在整个 hollow_knight_Data 里只出现 1 次，
                //       位于 sharedassets140，与 Plant Turret / Corpse Plant Turret 同一预制体。
                case "spike ball":
                case "spikeball":
                case "plant turret":
                case "plantturret":
                    return "古尔卡";
            }
            return null;
        }

        private static string TrapNameByChain(GameObject go)
        {
            if (go == null) return null;
            Transform t = go.transform;
            for (int i = 0; i < 3 && t != null; i++)
            {
                string by = TrapNameByObject(CleanName(t.name));
                if (by != null) return by;
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// 这几个名字末尾的数字**不是克隆序号**，是「第几战」：Hornet Boss 1 / Hornet Boss 2。
        /// 场景里的实例普遍带序号（Fat Fly 4 / Moss Charger 1 (1)），所以下面会统一把末尾数字
        /// 当垃圾剥掉；但大黄蜂的 1/2 恰恰是有意义的那一位，剥完只剩 "Hornet Boss"，
        /// 两场就分不开了。因此必须在剥之前先查这张表。
        /// </summary>
        private static readonly Dictionary<string, string> sNumberedNames = new Dictionary<string, string>
        {
            { "hornetboss1", "初见大黄蜂" },
            { "hornetboss2", "二见大黄蜂" },
        };

        /// <summary>
        /// 只有真正命中名称表才算认出（EnemyToCn 认不出时会原样返回英文，那不能当识别成功）。
        /// </summary>
        private static bool EnemyCnHit(string raw, out string cn)
        {
            cn = null;
            if (string.IsNullOrEmpty(raw)) return false;

            // 先试「带数字才算数」的名字。必须赶在 StripTrailingNumber 之前。
            string numbered;
            if (sNumberedNames.TryGetValue(EnemyKey(StripVariantSuffix(raw)), out numbered)
                && !string.IsNullOrEmpty(numbered))
            {
                cn = numbered;
                return true;
            }

            if (EnemyCn.TryGetValue(EnemyKey(raw), out cn) && !string.IsNullOrEmpty(cn)) return true;

            // 场景里的实例普遍带序号："Fat Fly 4" / "Moss Walker (3)" / "Moss Charger 1 (1)"。
            // 不剥掉就永远对不上表 —— 之前面板上出现的英文名就是这么来的。
            string noNum = StripTrailingNumber(StripVariantSuffix(raw));
            if (noNum != raw && EnemyCn.TryGetValue(EnemyKey(noNum), out cn) && !string.IsNullOrEmpty(cn)) return true;

            string stripped = StripVariantSuffix(raw);
            if (stripped != raw && EnemyCn.TryGetValue(EnemyKey(stripped), out cn) && !string.IsNullOrEmpty(cn)) return true;

            // "Mossman Shaker Boss"、"Boss Mossman Shaker" 这类前后缀
            string alt = stripped.Replace("(Clone)", "").Trim();
            if (alt.StartsWith("Boss ", StringComparison.OrdinalIgnoreCase)) alt = alt.Substring(5).Trim();
            if (alt.EndsWith(" Boss", StringComparison.OrdinalIgnoreCase)) alt = alt.Substring(0, alt.Length - 5).Trim();
            if (alt != stripped && EnemyCn.TryGetValue(EnemyKey(alt), out cn) && !string.IsNullOrEmpty(cn)) return true;

            cn = null;
            return false;
        }

        /// <summary>
        /// 沿父链找第一个能命中名称表的物体名。
        /// 怪物的攻击判定框（Gas Hit Box、Slash Hitbox 之类）通常挂在怪物自己的子物体下，
        /// 用攻击物自己的名字会记成 "Gas Hit Box"，必须往上看宿主。
        /// </summary>
        private static bool TryCnAlongChain(GameObject start, int depth, out string cn)
        {
            cn = null;
            if (start == null) return false;
            Transform t = start.transform;
            for (int i = 0; i < depth && t != null; i++)
            {
                string raw = CleanName(t.name);
                if (!string.IsNullOrEmpty(raw) && raw != "New Game Object" && EnemyCnHit(raw, out cn)) return true;
                t = t.parent;
            }
            cn = null;
            return false;
        }

        /// <summary>
        /// 判断攻击者是不是「活物」：只有活物身上才有 HealthManager。
        /// 这是区分怪物与陷阱触发器的可靠依据。
        ///
        /// 取名字的顺序（越靠前越权威）：
        ///   1. HealthManager.enemyType —— 有些版本存在，直接是敌人枚举
        ///   2. 血条所在的宿主物体名 —— 血条长在谁身上谁才是怪
        ///   3. 攻击者自己及父链
        ///   4. 宿主英文原名（至少不是 "Gas Hit Box" 这种判定框名）
        /// 教训：本作 HealthManager 没有 enemyType 字段（Assembly-CSharp 里搜不到该枚举），
        /// 旧代码取不到就退回攻击物自己的名字，于是「不稳定苔藓」的气体判定框被记成 Gas Hit Box。
        /// </summary>
        private static bool TryGetEnemyName(object attacker, out string name)
        {
            name = null;
            GameObject go = attacker as GameObject;
            if (go == null) return false;

            try
            {
                Type hmType = FindType("HealthManager");
                Component hm = hmType != null ? FindComponent(go, hmType) : null;

                string cn;
                string via = null;
                GameObject host = null;
                if (hm != null)
                {
                    try { host = hm.gameObject; } catch { }

                    object et = GetMember(hm, "enemyType");
                    if (et != null && EnemyCnHit(NameOfEnumOrInt(et), out cn))
                    {
                        name = cn;
                        via = "enemyType";
                    }
                }

                if (string.IsNullOrEmpty(name) && host != null && TryCnAlongChain(host, 4, out cn))
                {
                    name = cn;
                    via = "宿主";
                }

                if (string.IsNullOrEmpty(name) && TryCnAlongChain(go, 4, out cn))
                {
                    name = cn;
                    via = "父链";
                }

                if (string.IsNullOrEmpty(name) && host != null)
                {
                    string raw = CleanName(host.name);
                    if (!string.IsNullOrEmpty(raw) && raw != "New Game Object")
                    {
                        name = EnemyToCn(raw);
                        via = "宿主原名";
                    }
                }

                if (string.IsNullOrEmpty(name)) return false;

                if (sEnemyNameLogs < 25 && L != null)
                {
                    sEnemyNameLogs++;
                    L.LogInfo("[死亡统计] 敌人识别 | 攻击物=" + CleanName(go.name)
                        + " | 宿主=" + (host != null ? CleanName(host.name) : "无血条")
                        + " | 判定=" + name + " | 依据=" + via);
                }

                // 中文表里查不到的内部名（Fat Fly 这种）各打一次家底：
                // 组件里的 AI 脚本名 + 子物体 FSM 名 + 贴图集合名，才是它的正身。
                // 场景作者给实例起的名字经常和官方怪名对不上，靠这些才能定死是哪只。
                if (host != null)
                {
                    string rawHost = StripTrailingNumber(StripVariantSuffix(CleanName(host.name)));
                    string tmpCn;
                    if (!string.IsNullOrEmpty(rawHost) && !EnemyCnHit(rawHost, out tmpCn)
                        && sNameDumped.Add(rawHost))
                    {
                        DumpUnknownEnemy(host, rawHost);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 解析敌人失败: " + e.Message);
            }
            return false;
        }

        /// <summary>
        /// 打印一只「中文表里查不到」的怪的全部线索：组件类型、FSM 名、贴图集合名。
        /// 场景里的内部名（Fat Fly）和官方名（Squit / 斯奎特）经常对不上，
        /// 挂在这只怪身上的 AI 脚本名和贴图名才是它的正身。
        /// </summary>
        private static void DumpUnknownEnemy(GameObject host, string rawName)
        {
            if (L == null) return;
            try
            {
                string comps = "";
                Component[] cs = null;
                try { cs = host.GetComponents<Component>(); } catch { }
                if (cs != null)
                {
                    for (int i = 0; i < cs.Length; i++)
                    {
                        if (cs[i] == null) continue;
                        string n = "";
                        try { n = cs[i].GetType().Name; } catch { continue; }
                        if (comps.IndexOf(n) >= 0) continue;
                        comps += (comps.Length > 0 ? "," : "") + n;
                    }
                }

                string fsms = "";
                try
                {
                    Type pfsm = FindType("PlayMakerFSM");
                    if (pfsm != null)
                    {
                        UnityEngine.Object[] arr = host.GetComponentsInChildren(pfsm, true);
                        if (arr != null)
                        {
                            for (int i = 0; i < arr.Length && i < 12; i++)
                            {
                                Component c = arr[i] as Component;
                                if (c == null) continue;
                                string fname = null;
                                try { fname = GetMember(GetMember(c, "Fsm"), "Name") as string; } catch { }
                                if (string.IsNullOrEmpty(fname))
                                {
                                    try { fname = c.gameObject.name; } catch { }
                                }
                                if (!string.IsNullOrEmpty(fname))
                                    fsms += (fsms.Length > 0 ? "," : "") + fname;
                            }
                        }
                    }
                }
                catch { }

                string tex = "-";
                try
                {
                    Type tk = FindType("tk2dSprite");
                    if (tk != null)
                    {
                        Component c = FindComponent(host, tk);
                        if (c != null)
                        {
                            string col = GetMember(GetMember(c, "Collection"), "spriteCollectionName") as string;
                            string spr = GetMember(GetMember(c, "CurrentSprite"), "name") as string;
                            tex = (col ?? "?") + "/" + (spr ?? "?");
                        }
                    }
                }
                catch { }

                L.LogInfo("[死亡统计] 未知中文名 | 物体=" + rawName
                    + " | 组件=" + (comps.Length > 0 ? comps : "-")
                    + " | FSM=" + (fsms.Length > 0 ? fsms : "-")
                    + " | 贴图=" + tex);
            }
            catch { }
        }

        /// <summary>
        /// DieFromHazard(HazardType, float) 是踩刺/泡酸水/岩浆/掉坑的确定性致死入口。
        /// 这里直接拿到游戏传入的 HazardType 枚举实例，比从 TakeDamage 的 int 参数反查可靠得多。
        /// </summary>
        private static void OnDieFromHazard(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0) return;

                // 不按位置取，直接找那个枚举参数（HazardType）
                object hz = null;
                for (int i = 0; i < __args.Length; i++)
                {
                    if (__args[i] != null && __args[i].GetType().IsEnum) { hz = __args[i]; break; }
                }
                if (hz == null) return;

                int raw = Convert.ToInt32(hz);
                string name = ResolveHazardName(hz, raw);

                sLastHazardKiller = HazardToCn(name, raw);
                sLastHazardAt = Time.realtimeSinceStartup;

                if (L != null)
                {
                    // 血量 > 0 说明这次多半不是真死（白宫踩刺走的是 hazard respawn，
                    // 游戏照样调 DieFromHazard），落账交给 PlayerDead，这里只留个痕迹。
                    int hp = ReadHeroHealth(GetHero());
                    L.LogInfo("[死亡统计] 致死陷阱 = " + (name ?? "?") + " (值 " + raw + ") → " + sLastHazardKiller
                        + " | health=" + hp + (hp > 0 ? "（非真死，仅记死因）" : ""));
                }
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 解析致死陷阱失败: " + e.Message);
            }
        }

        /// <summary>
        /// HeroController.Die() 与 DieFromHazard() 都挂这个。
        /// 它们只说明「英雄进入了死亡/受创流程」，不保证是真死：
        /// 白宫踩刺后游戏会走 hazard respawn，DieFromHazard 可能被反复调用。
        /// 因此只要 GameManager.PlayerDead 补丁在位，这里就只在没有权威入口时兜底落账。
        /// </summary>
        private static void OnDie(object __instance)
        {
            if (sPlayerDeadHooked) return;
            RecordNow(__instance, "Die");
        }

        /// <summary>
        /// GameManager.PlayerDead(float) —— 一次真死只会走一次，
        /// 扣 Geo、掉阴影、回长椅全在这条链上，是唯一的权威落账点。
        /// </summary>
        private static void OnPlayerDead(object __instance)
        {
            RecordNow(__instance, "PlayerDead");
        }

        private static void RecordNow(object __instance, string source)
        {
            try
            {
                float now = Time.realtimeSinceStartup;

                // 已经在死亡状态里了：游戏重复上报同一次死亡，不算新的
                if (sDeathLatched)
                {
                    if (sDupDeathLogs < 10)
                    {
                        sDupDeathLogs++;
                        if (L != null)
                            L.LogInfo("[死亡统计] 忽略重复死亡信号（英雄尚未复活，距上次落账 "
                                + (now - sDeathLatchedAt).ToString("0.0") + "s，来源 " + source + "）");
                    }
                    return;
                }

                if (now - sLastRecordAt < 1.5f) return;

                string killer;
                string detail;

                bool enemyFresh = now - sLastHitAt < 5f && !string.IsNullOrEmpty(sLastKiller)
                    && (sLastDetail == "爆裂"
                        || (sLastDetail != null && sLastDetail.StartsWith("敌人", StringComparison.Ordinal)));
                bool hazardFresh = now - sLastHazardAt < 6f && !string.IsNullOrEmpty(sLastHazardKiller);
                bool hitFresh = now - sLastHitAt < 5f && !string.IsNullOrEmpty(sLastKiller);

                // 优先级 1：最后一下是被怪打的 —— 怪物击杀最可靠，优先于陷阱缓存。
                // 踩刺真死时 TakeDamage 的攻击者是尖刺本身（没有 HealthManager），
                // 所以不会把「踩刺死」错认成敌人。
                string branch;
                if (enemyFresh)
                {
                    killer = sLastKiller;
                    detail = sLastDetail;
                    branch = "敌人缓存";
                }
                // 优先级 2：陷阱致死
                else if (hazardFresh)
                {
                    killer = sLastHazardKiller;
                    detail = "陷阱";
                    branch = "陷阱缓存";
                }
                // 优先级 3：其他伤害来源
                else if (hitFresh)
                {
                    killer = sLastKiller;
                    detail = sLastDetail;
                    branch = "其他伤害缓存";
                }
                else
                {
                    branch = "无缓存";
                    // 没有任何伤害记录：游戏大多是直接调 Die()（掉出地图 / 出界 / 剧情处决）。
                    // 用死亡瞬间的血量区分「没掉血的直接死」和「掉到 0 但没抓到来源」。
                    int hp = ReadDeathHealth(__instance);
                    if (hp > 0) { killer = "掉落/出界"; detail = "非伤害致死"; }
                    else if (hp == 0) { killer = "未知"; detail = "血量归零"; }
                    else { killer = "未知"; detail = "未知"; }

                    if (L != null)
                        L.LogWarning("[死亡统计] 未捕获伤害来源 | 场景=" + CurrentScene() + " | 死亡时 health=" + hp);
                }

                if (L != null)
                {
                    // 每次都记：这次死因是走哪条分支、缓存多久之前的伤害 —— 出问题一眼定位
                    L.LogInfo("[死亡统计] 落账依据 | 分支=" + branch
                        + " | 受击 " + (now - sLastHitAt).ToString("0.0") + "s 前"
                        + " | 陷阱 " + (now - sLastHazardAt).ToString("0.0") + "s 前");
                }

                string sceneNow = CurrentScene();

                // 场地 BOSS：这些房间整场能杀你的基本只有场地主，弹幕/特效又多又密，
                // 逐条归属经常归不到人头上。所以整场死亡统一算场地主的击杀，
                // 原来那套判定留在 detail 里（日志也会打一条），需要时能还原。
                // 两类不吞：
                //   1) 明确判成地形/陷阱的（掉刺、酸水、岩浆）——那是实打实的地形死；
                //   2) 已经从物体真名认出是哪一个变体的（Hornet Boss 1/2）——名字比场景更精确。
                string arenaKiller, arenaLabel;
                bool exactVariant = killer == "初见大黄蜂" || killer == "二见大黄蜂";
                if (ArenaBossOf(sceneNow, out arenaKiller, out arenaLabel)
                    && killer != arenaKiller && killer != "地形/陷阱" && !exactVariant)
                {
                    if (L != null)
                        L.LogInfo("[死亡统计] 场地BOSS统一归类 | 场景=" + sceneNow
                            + " | 场地=" + arenaLabel + " | 原判定=" + killer + "/" + detail);
                    detail = arenaLabel + "·" + killer;
                    killer = arenaKiller;
                }

                RecordDeath(sceneNow, killer, detail);
                sLastRecordAt = now;

                // 上闸：在同一次死亡里，后面的 Die / PlayerDead / DieFromHazard 全部忽略
                sDeathLatched = true;
                sDeathLatchedAt = now;
                if (L != null)
                    L.LogInfo("[死亡统计] 死亡间隔 "
                        + (sLastDeathAt > -900f ? (now - sLastDeathAt).ToString("0.0") + "s" : "首次记录")
                        + "（本场景 " + CurrentScene() + "，来源 " + source + "）");
                sLastDeathAt = now;

                // 落账后清空缓存，避免下一次死亡误用这一轮的死因
                sLastHazardKiller = null;
                sLastHazardAt = -999f;
                sLastKiller = null;
                sLastHitAt = -999f;
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 落账失败: " + e.Message);
            }
        }

        /// <summary>
        /// 每帧检查：英雄复活了就把死亡闸门打开，允许记录下一次死亡。
        /// 判据优先用英雄状态机里的死亡标记（cState.dead / hazardDeath / dying），
        /// 因为这些在「踩刺秒死」时也可靠 —— 那时候血量还剩着，只看血量会误判成活着。
        /// 状态全读不到时才退回血量，再不行靠超时兜底，绝不把之后的死亡永久吞掉。
        /// </summary>
        private static void TickDeathLatch()
        {
            if (!sDeathLatched) return;

            // 刚记完账的头 0.3 秒不给解锁，避免死亡瞬间的状态抖动误开闸
            if (Time.realtimeSinceStartup - sDeathLatchedAt < 0.3f) return;

            bool alive = false;
            bool stateKnown = false;

            try
            {
                object hero = GetHero();
                if (hero != null)
                {
                    object cs = GetMember(hero, "cState");
                    if (cs != null)
                    {
                        bool anyReadable = false;
                        bool anyTrue = false;
                        string[] flags = new string[] { "dead", "hazardDeath", "dying" };
                        foreach (string fn in flags)
                        {
                            object v = GetMember(cs, fn);
                            if (v is bool)
                            {
                                anyReadable = true;
                                if ((bool)v) anyTrue = true;
                            }
                        }
                        if (anyReadable)
                        {
                            stateKnown = true;
                            alive = !anyTrue;
                        }
                    }

                    // 状态标记一个都读不到，才退回血量判断
                    if (!stateKnown)
                    {
                        int hp = ReadHeroHealth(hero);
                        if (hp > 0) { stateKnown = true; alive = true; }
                        else if (hp == 0) { stateKnown = true; alive = false; }
                    }
                }
            }
            catch { }

            if (stateKnown && alive)
            {
                sDeathLatched = false;
                sDupDeathLogs = 0;
                return;
            }

            // 读不到状态：20 秒后强制解锁；读得到但一直"死着"（过场/读盘）：120 秒兜底
            float timeout = stateKnown ? 120f : 20f;
            if (Time.realtimeSinceStartup - sDeathLatchedAt > timeout)
            {
                sDeathLatched = false;
                sDupDeathLogs = 0;
                if (L != null) L.LogWarning("[死亡统计] 死亡闸门超时解锁，后续死亡可继续记录");
            }
        }

        /// <summary>取英雄实例：先 HeroController.instance，再退回 GameManager.hero</summary>
        private static object GetHero()
        {
            try
            {
                Type hc = FindType("HeroController");
                if (hc != null)
                {
                    object h = GetStaticMember(hc, "instance", "Instance", "SilentInstance");
                    if (h != null) return h;
                }

                Type gm = FindType("GameManager");
                if (gm != null)
                {
                    object inst = GetStaticMember(gm, "instance", "Instance");
                    if (inst != null)
                    {
                        object h = GetMember(inst, "hero", "Hero");
                        if (h != null) return h;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>死亡瞬间读血量；读不到返回 -1</summary>
        private static int ReadHeroHealth(object hero)
        {
            try
            {
                object pd = hero != null ? GetMember(hero, "playerData") : null;
                if (pd == null)
                {
                    Type t = FindType("PlayerData");
                    pd = t != null ? GetStaticMember(t, "instance") : null;
                }
                if (pd == null) return -1;
                object h = GetMember(pd, "health");
                return h == null ? -1 : Convert.ToInt32(h);
            }
            catch { return -1; }
        }

        /// <summary>
        /// 落账时读血量：__instance 可能是 HeroController 也可能是 GameManager，
        /// 前者能直接拿到 playerData，后者拿不到，要退回英雄实例。
        /// </summary>
        private static int ReadDeathHealth(object inst)
        {
            int hp = inst != null ? ReadHeroHealth(inst) : -1;
            if (hp < 0) hp = ReadHeroHealth(GetHero());
            return hp;
        }

        // ================= 名称解析 =================

        private static readonly Dictionary<string, string> HazardCn = new Dictionary<string, string>
        {
            // 「尖刺」只是其中一种：荆棘、骨刺、钻头、白宫电锯在玩家眼里都是同一种死法，
            // 也都不属于怪物，统一叫「地形/陷阱」比逐个细分更有用。
            { "Spikes", "地形/陷阱" }, { "Spike", "地形/陷阱" }, { "SpikesDown", "地形/陷阱" },
            { "Acid", "酸水" }, { "Lava", "岩浆" }, { "Pit", "深渊" }, { "None", "地形/陷阱" },
        };

        /// <summary>
        /// 按陷阱物体的名字认类型。HK 的 DamageHero 组件会自己带一个 hazardType 字段，
        /// 白宫电锯 wp_saw 身上写的是 ACID(2)，光看枚举会把它报成酸水，所以名字优先。
        /// 认不出来返回 null，交给枚举兜底。
        /// </summary>
        private static string TrapNameByObject(string objName)
        {
            if (string.IsNullOrEmpty(objName)) return null;
            string n = objName.ToLowerInvariant();

            if (n.Contains("saw") || n.Contains("blade") || n.Contains("spinner")
                || n.Contains("thwomp") || n.Contains("press")) return "电锯";
            if (n.Contains("spike") || n.Contains("thorn") || n.Contains("nail_trap")) return "地形/陷阱";
            if (n.Contains("acid")) return "酸水";
            if (n.Contains("lava") || n.Contains("magma")) return "岩浆";
            if (n.Contains("pit") || n.Contains("abyss")) return "深渊";
            return null;
        }

        private static string HazardToCn(string enumName, int raw)
        {
            if (!string.IsNullOrEmpty(enumName))
            {
                string cn;
                if (HazardCn.TryGetValue(enumName, out cn)) return cn;
                foreach (KeyValuePair<string, string> kv in HazardCn)
                    if (string.Equals(kv.Key, enumName, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return enumName;
            }
            return "地形/陷阱";
        }

        /// <summary>
        /// 把 hazardType 参数翻译成枚举名。
        /// 参数可能是枚举本身，也可能已被拆成 int（Unity 6 版就是 int），
        /// int 情况下借用游戏自己的 HazardType 枚举翻译，避免数值硬编码。
        /// </summary>
        internal static Type sHazardEnum;

        private static string ResolveHazardName(object raw, int value)
        {
            try
            {
                Type t = raw.GetType();
                if (t.IsEnum) return Enum.GetName(t, raw);

                if (sHazardEnum == null) sHazardEnum = FindType("HazardType");
                if (sHazardEnum != null && sHazardEnum.IsEnum)
                {
                    // 用 ToObject 转成枚举实例再取名，避免底层类型不是 int 时取不到
                    object ev = Enum.ToObject(sHazardEnum, value);
                    string n = Enum.GetName(sHazardEnum, ev);
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch { }
            return null;
        }

        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        // ===== 敌人中文名（由 scripts/gen_enemy_block.py 从游戏官方语言表生成，勿手改）=====
        // 键是「归一化」后的名字：小写、只留字母数字。查表前先归一化，
        // 所以 Zombie Runner / ZOMBIE_RUNNER / ZombieRunner(Clone) 会命中同一项。
        private static readonly Dictionary<string, string> EnemyCn = new Dictionary<string, string>
        {
            { "abysscrawler", "阴影爬虫" }, { "abysstendril", "虚空卷须" }, { "acidflyer", "杜兰达" },
            { "acidwalker", "杜兰多" }, { "angrybuzzer", "愤怒反击蝇" }, { "babycentipede", "刻尘者" },
            { "beamminer", "结晶躯壳" }, { "bee", "蜂巢士兵" }, { "beehatchling", "小蜜蜂" },
            { "beestinger", "蜂巢士兵" }, { "bigbee", "蜂巢守卫" }, { "bigbuzzer", "反击蝇之王" },
            { "bigcentipede", "加皮德" }, { "bigfatflyer", "肥波" }, { "bigfly", "格鲁兹之母" },
            { "bindingseal", "束缚封印" }, { "blackknight", "守望者骑士" }, { "blobble", "战斗奥波" },
            { "blobflyer", "奥波" }, { "blocker", "巴德尔长者" }, { "blowfly", "肥波" },
            { "bouncer", "格鲁兹" }, { "burstingbouncer", "不稳定格鲁兹" }, { "burstingzombie", "暴力躯壳" },
            { "buzzer", "反击蝇" }, { "ceilingdropper", "爆肚蝠" }, { "centipedehatcher", "刻尘孵化者" },
            { "climber", "提克提克" }, { "colflyingsentry", "有翼愚人" }, { "colhopper", "死亡鲁多" },
            { "colminer", "强壮愚人" }, { "colmosquito", "装甲斯奎特" }, { "colroller", "尖刺巴德尔" },
            { "colshield", "带盾愚人" }, { "colworm", "重装愚人" }, { "corpseplantturret", "古尔卡" },
            { "crawler", "小爬虫" }, { "cryscrawler", "亮背虫" }, { "crysflyer", "水晶猎人" },
            { "cryslazerbug", "水晶爬虫" }, { "crystalcrawler", "亮背虫" }, { "crystalflyer", "水晶猎人" },
            { "dungdefender", "粪虫防御者" }, { "eggsac", "臭蛋口袋" }, { "elecmage", "闪电扭曲者" },
            { "electricmage", "闪电扭曲者" }, { "falseknight", "假骑士" }, { "fatfluke", "贪食吸虫" },
            // Fat Fly：游戏内部命名，官方语言表没有这个键，但它就是「奥波」(Blob Flyer)。
            // 依据：关卡文件 level147（苍绿之径 Fungus1_19）里摆了 4 只 GameObject 叫 Fat Fly，
            // 同一文件内嵌 4 处 BlobFlyer 预制体引用；该房间实测就是被奥波酸液砸死。
            { "fatfly", "奥波" },
            { "finalboss", "辐光" }, { "flamebearerlarge", "格林亲族梦魇" }, { "flamebearermed", "格林亲族大师" },
            { "flamebearersmall", "格林亲族新手" }, { "fliphopper", "翻身怪" }, { "flukefly", "吸虫幼雏" },
            { "flukeman", "吸虫怪" }, { "flukemother", "吸虫之母" }, { "flyingsentryjavelin", "长矛哨兵" },
            { "flyingsentrysword", "有翼哨兵" }, { "fungcrawler", "安布鲁" }, { "fungifiedzombie", "真菌躯壳" },
            { "fungoon", "真菌球" }, { "fungoonbaby", "小真菌球" }, { "fungusflyer", "真菌球" },
            { "fungzombie", "真菌躯壳" }, { "gardenzombie", "多刺躯壳" }, { "ghostaladar", "戈布" },
            { "ghostgalien", "加利安" }, { "ghosthu", "胡长老" }, { "ghostmarkoth", "马科斯" },
            { "ghostmarmu", "马尔穆" }, { "ghostnoeyes", "无眼" }, { "ghostxero", "泽若" },
            { "gianthopper", "大跳虫" }, { "godseekermask", "风化面具" }, { "gorgeoushusk", "华丽躯壳" },
            { "grasshopper", "鲁多" }, { "gravezombie", "埋葬躯壳" }, { "greatshieldzombie", "大型躯壳哨兵" },
            { "greyprince", "灰色王子左特" }, { "grimm", "格林" }, { "grubmimic", "幼虫模仿者" },
            { "hatcher", "阿司匹德之母" }, { "hatchling", "阿司匹德幼体" }, { "healthscuttler", "生命籽" },
            { "heavymantis", "螳螂叛徒" }, { "hiveknight", "蜂巢骑士" }, { "hollowknight", "空洞骑士" },
            { "hollowknightprime", "纯粹容器" }, { "hollowshade", "阴影" }, { "hopper", "跳虫" },
            { "hornet", "大黄蜂" }, { "huntermark", "猎人的标记" }, { "infectedknight", "残破容器" },
            { "inflater", "呼噗" }, { "inflator", "呼噗" }, { "jarcollector", "收藏家" },
            // 场景里用预制体名 Lil Jellyfish / Lil Jelly 摆的水母宝宝，官方日志没有独立条目，
            // 与 Jellyfish Baby 同源，归到欧玛（同房间的乌玛另有对象名 JellyCrawler）。
            { "jellycrawler", "乌玛" }, { "jellyfish", "欧玛" }, { "jellyfishbaby", "欧玛" },
            { "liljellyfish", "欧玛" }, { "liljelly", "欧玛" },
            { "kinseal", "束缚封印" }, { "laserbug", "水晶爬虫" }, { "lazyflyer", "阿鲁巴" },
            { "lessermawlek", "小型毛里克" }, { "lobsterlancer", "神之驯服者" }, { "mage", "灵魂扭曲者" },
            { "mageballoon", "愚蠢" }, { "mageblob", "错误" }, { "mageknight", "灵魂战士" },
            { "magelord", "灵魂大师" }, { "mantis", "螳螂战士" }, { "mantisflyeradult", "螳螂佩特拉" },
            { "mantisflyerchild", "螳螂青年" }, { "mantisheavy", "螳螂叛徒" }, { "mantisheavyflyer", "螳螂佩特拉" },
            { "mantislord", "螳螂领主" }, { "mawlek", "躁郁的毛里克" }, { "mawlekturret", "毛鲁克" },
            { "megabeamminer", "水晶守卫" }, { "megajellyfish", "乌姆" }, { "megamosscharger", "大型苔藓冲锋者" },
            { "menderbug", "维修虫" }, { "mimicspider", "诺斯克" }, { "minescrawler", "晶刺螨" },
            { "minispider", "深处小虫" }, { "mosquito", "斯奎特" }, { "mosscharger", "苔藓冲锋者" },
            { "mossflyer", "苔藓蝶" }, { "mossknight", "苔藓骑士" }, { "mossknightfat", "苔藓游荡者" },
            { "mossmanrunner", "苔藓怪" }, { "mossmanshaker", "不稳定苔藓" }, { "mosswalker", "苔藓爬虫" },
            { "mummy", "埋葬躯壳" }, { "mushbaby", "小蘑菇" }, { "mushbrawler", "蘑菇巨人" },
            { "mushroller", "蘑菇战士" }, { "mushroombaby", "小蘑菇" }, { "mushroombrawler", "蘑菇巨人" },
            { "mushroomroller", "蘑菇战士" }, { "mushroomturret", "斯孢格" }, { "mushturret", "斯孢格" },
            { "nailbros", "骨钉大师奥罗 & 马托" }, { "nailmasters", "骨钉大师奥罗 & 马托" }, { "nailsage", "伟大骨钉贤者斯莱" },
            { "nightmaregrimm", "梦魇之王" }, { "oblobble", "奥波路波" }, { "orangeballoon", "感染气球" },
            { "orangescuttler", "光籽" }, { "paintmaster", "绘画大师席奥" }, { "palacefly", "翅膀傀儡" },
            { "palelurker", "苍白潜伏者" }, { "parasiteblob", "光籽" }, { "pigeon", "面具鸟" },
            // Plant Trap 是愚人吞食者（SnapperTrap 预制体），不是古尔卡：
            // 关卡里 Plant Trap 只和 Snapper 同房（level128/131/147/150/160/201/224），
            // Plant Turret 只和 PlantShooter 同房（level140/141/156）。
            { "plantshooter", "古尔卡" }, { "planttrap", "愚人吞食者" }, { "plantturret", "古尔卡" },
            { "corpseplanttrap", "愚人吞食者" },
            { "prayerslug", "蛆虫" }, { "purevessel", "纯粹容器" }, { "roller", "巴德尔" },
            { "royalcoward", "怯懦躯壳" }, { "royaldandy", "躯壳公子" }, { "royalguard", "国王傀儡" },
            { "royalplumper", "暴食躯壳" }, { "sagesly", "伟大骨钉贤者斯莱" }, { "sentry", "躯壳哨兵" },
            { "sentry1", "躯壳哨兵" }, { "sentryfat", "重型哨兵" }, { "sentryflying", "有翼哨兵" },
            { "sentryflyingjavelin", "长矛哨兵" }, { "shootspider", "深处猎手" }, { "sibling", "兄弟" },
            { "slashspider", "潜行信徒" }, { "snappertrap", "愚人吞食者" }, { "spidercorpse", "尸体爬虫" },
            { "spiderflyer", "小型编织者" }, { "spikeball", "古尔卡" }, { "spitter", "阿司匹德猎人" },
            { "spittingspider", "深处猎手" }, { "spittingzombie", "流涎躯壳" }, { "superspitter", "原始阿司匹德" },
            { "traitorlord", "叛徒领主" }, { "voididol1", "虚空神像" }, { "voididol2", "虚空神像" },
            { "voididol3", "虚空神像" }, { "whitedefender", "白色防御者" }, { "whitepalacefly", "翅膀傀儡" },
            { "whiteroyal", "皇室家臣" }, { "worm", "戈姆" }, { "zapbug", "带电光蝇" },
            { "zombarger", "躯壳恶霸" }, { "zombeamminer", "结晶躯壳" }, { "zombiebarger", "躯壳恶霸" },
            { "zombieguard", "躯壳守卫" }, { "zombiehive", "蜂巢躯壳" }, { "zombiehornhead", "有角躯壳" },
            { "zombieleaper", "跳跃躯壳" }, { "zombieminer", "躯壳矿工" }, { "zombierunner", "游荡躯壳" },
            { "zombieshield", "躯壳战士" }, { "zombursting", "暴力躯壳" }, { "zomguard", "躯壳守卫" },
            // 场景里对象名是 Zombie Beam Miner / Zombie Spider，与日志 ID 不是同一写法，
            // 之前只登记了 zombeamminer / zombspin 之类，运行时对不上会掉回英文名。
            { "zombiebeamminer", "结晶躯壳" }, { "zombiespider", "尸体爬虫" },
            { "zomhive", "蜂巢躯壳" }, { "zomhornhead", "有角躯壳" }, { "zomleaper", "跳跃躯壳" },
            { "zomminer", "躯壳矿工" }, { "zomroyal1", "躯壳公子" }, { "zomroyal2", "怯懦躯壳" },
            { "zomroyalfat", "暴食躯壳" }, { "zomrunner", "游荡躯壳" }, { "zomshield", "躯壳战士" },
            { "zomspider", "尸体爬虫" }, { "zomspine", "多刺躯壳" }, { "zomspitting", "流涎躯壳" },
            { "zote", "左特" }, { "zoteballoon", "不稳定迷你左特" }, { "zoteflyer", "有翼迷你左特" },
            { "zotehopper", "跳跃迷你左特" }, { "zotelingballoon", "不稳定迷你左特" }, { "zotelingbuzzer", "有翼迷你左特" },
            { "zotelinghopper", "跳跃迷你左特" },
            // ── 实测补齐：来自 EnemyRandomizer 的 500 份场景转储，只认挂 HealthManager 的对象 ──
            // 这些名字不是猜的，是游戏里真实存在的对象名；置信度见 桌面表格「实测对象名」工作表
            { "absoluteradiance", "无上辐光" }, { "babycentipedespawner", "刻尘者" }, { "beehatchlingambient", "小蜜蜂" },
            { "mossknightb", "苔藓骑士" }, { "mossknightc", "苔藓骑士" },
            { "colosseumminer", "强壮愚人" }, { "crawlerfixed", "小爬虫" }, { "crystallisedlazerbug", "水晶爬虫" },
            { "dreammagelord", "灵魂大师" }, { "dreammagelordphase", "灵魂大师" }, { "dummymantis", "螳螂战士" },
            { "electricmagenew", "闪电扭曲者" }, { "falseknightdream", "失败冠军" }, { "falseknightnew", "假骑士" },
            { "flukebaby01", "吸虫幼雏" }, { "flukebaby02", "吸虫幼雏" }, { "flukebaby03", "吸虫幼雏" },
            { "flukeflyspawner", "吸虫幼雏" }, { "fly", "格鲁兹" }, { "gatemantis", "螳螂战士" },
            { "ghostwarriorgalien", "加利安" }, { "ghostwarriorhu", "胡长老" }, { "ghostwarriormarkoth", "马科斯" },
            { "ghostwarriormarmu", "马尔穆" }, { "ghostwarriornoeyes", "无眼" }, { "ghostwarriorslug", "戈布" },
            { "ghostwarriorxero", "泽若" }, { "giantbuzzer", "反击蝇之王" }, { "giantbuzzercol", "反击蝇之王" },
            { "giantfly", "格鲁兹之母" }, { "greatshieldzombiebottom", "大型躯壳哨兵" }, { "grimmboss", "格林" },
            { "hatcherbabyspawner", "阿司匹德幼体" }, { "hatchernp", "阿司匹德之母" }, { "head", "假骑士" },
            { "hivelingspawner", "小蜜蜂" }, { "hkprime", "纯粹容器" }, { "hollowknightboss", "空洞骑士" },
            { "hornetboss", "大黄蜂" }, { "hornetnosk", "有翼诺斯克" }, { "jellyfishbabyinert", "欧玛" },
            { "lancer", "神之驯服者" }, { "lazyflyerenemy", "阿鲁巴" }, { "lobster", "神之驯服者" },
            { "lostkin", "失落近亲" }, { "mageballoonspawner", "愚蠢" }, { "magelordphase", "灵魂大师" },
            { "mantisheavyspawn", "螳螂叛徒" }, { "mantislords", "螳螂领主" }, { "mantistraitorlord", "叛徒领主" },
            { "mato", "马托" }, { "mawlekbody", "躁郁的毛里克" }, { "mawlekturretceiling", "毛鲁克" },
            { "megafatbee", "奥波路波" }, { "megajellyfishgg", "乌姆" }, { "megazombiebeamminer", "水晶守卫" },
            { "nightmaregrimmboss", "梦魇之王" }, { "ordealzoteling", "跳跃迷你左特" }, { "oro", "奥罗" },
            { "parasiteballoon", "感染气球" }, { "plantturretright", "古尔卡" }, { "radiance", "辐光" },
            { "royalgaurd", "国王傀儡" }, { "royalzombie", "躯壳公子" }, { "royalzombiecoward", "怯懦躯壳" },
            { "royalzombiefat", "暴食躯壳" }, { "ruinsflyingsentry", "有翼哨兵" }, { "ruinsflyingsentryb", "有翼哨兵" },
            { "ruinsflyingsentryjavelin", "长矛哨兵" }, { "ruinssentry", "躯壳哨兵" }, { "ruinssentryb", "躯壳哨兵" },
            { "ruinssentryfat", "重型哨兵" }, { "ruinssentryfatb", "重型哨兵" }, { "shadesibling", "兄弟" },
            { "shell", "杜兰多" }, { "sheoboss", "绘画大师席奥" }, { "slyboss", "伟大骨钉贤者斯莱" },
            { "spidermini", "深处小虫" }, { "tinyspider", "小型编织者" }, { "zombiebeamminerrematch", "结晶躯壳" },
            { "zombiefungusa", "真菌躯壳" }, { "zombiefungusb", "真菌躯壳" }, { "zombiehornheadsp", "有角躯壳" },
            { "zombiemyla", "米拉" }, { "zombierunnersp", "游荡躯壳" }, { "zoteballoonordeal", "不稳定迷你左特" },
            { "zoteboss", "灰色王子左特" }, { "zotecrewfat", "左特亲卫" }, { "zotecrewnormal", "左特亲卫" },
            { "zotecrewtall", "左特亲卫" }, { "zotefluke", "吸虫幼雏" }, { "zoteling", "跳跃迷你左特" },
            { "zotesalubra", "左特亲卫" }, { "zotethwomp", "左特亲卫" }, { "zoteturret", "左特亲卫" },
        };

        private static string ResolveEnemy(object attacker)
        {
            string n;
            return TryGetEnemyName(attacker, out n) ? n : "未知";
        }

        private static string EnemyToCn(string en)
        {
            if (string.IsNullOrEmpty(en)) return en;

            // 表里的键是归一化形式（小写、只留字母数字），所以这几种写法都能命中同一项：
            //   ZombieRunner / ZOMBIE_RUNNER / Zombie Runner / Zombie_Runner(Clone)
            string cn;
            if (EnemyCn.TryGetValue(EnemyKey(en), out cn)) return cn;

            // 场景里的物体名常带变体序号："Blow Fly (5)"、"Jellyfish Baby (6)"、"Hollow Knight (7)"
            string stripped = StripVariantSuffix(en);
            if (stripped != en && EnemyCn.TryGetValue(EnemyKey(stripped), out cn)) return cn;

            // 认不出来就按英文原样显示（首字母大写、下划线转空格），至少比内部标识好看
            string tidy = stripped.Replace("(Clone)", "").Replace(" Boss", "").Replace('_', ' ').Replace('-', ' ').Trim();
            return ToTitle(SqueezeSpaces(tidy));
        }

        /// <summary>
        /// 归一化敌人名字：小写、只保留字母与数字。
        /// 游戏同一个敌人在不同地方有三种写法（枚举名 ZOMBIE_RUNNER、字段名 ZombieRunner、
        /// 物体名 "Zombie Runner (5)"），归一化后它们会变成同一个键。
        /// </summary>
        private static string EnemyKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 'A' && c <= 'Z') sb.Append((char)(c + 32));
                else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 去掉名字末尾的左右中变体标记：「Gas Explosion Recycle L」→「Gas Explosion Recycle」。
        /// 游戏的爆炸/冲击波预制体普遍有 L/M/R 三份，生成记录只可能记到被激活的那一份，
        /// 不打平这三种写法永远会漏掉另外两种。
        /// </summary>
        private static string VariantBase(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return s;
            char last = s[s.Length - 1];
            if (last != 'L' && last != 'M' && last != 'R') return s;
            if (s[s.Length - 2] != ' ') return s;
            return s.Substring(0, s.Length - 2).TrimEnd();
        }

        /// <summary>去掉物体名末尾的 " (5)" 这类变体序号</summary>
        private static string StripVariantSuffix(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int end = s.Length - 1;
            if (s[end] != ')') return s;
            int i = end - 1;
            while (i >= 0 && s[i] >= '0' && s[i] <= '9') i--;
            if (i < 0 || s[i] != '(') return s;
            i--;
            while (i >= 0 && s[i] == ' ') i--;
            if (i < 0) return s;
            return s.Substring(0, i + 1);
        }

        /// <summary>多个空格压成一个</summary>
        private static string SqueezeSpaces(string s)
        {
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ");
            return s.Trim();
        }

        /// <summary>把 HUSK_HORNHEAD 这种全大写标识转成 Husk Hornhead</summary>
        private static string ToTitle(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string[] parts = s.Split(' ');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                string w = parts[i].ToLowerInvariant();
                parts[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1) : "");
            }
            return string.Join(" ", parts);
        }

        /// <summary>enemyType 可能是枚举，也可能被拆成 int，两种情况都要能翻译</summary>
        internal static Type sEnemyEnum;

        private static string NameOfEnumOrInt(object v)
        {
            if (v == null) return null;
            try
            {
                Type t = v.GetType();
                if (t.IsEnum) return Enum.GetName(t, v);

                int raw = Convert.ToInt32(v);
                if (sEnemyEnum == null) sEnemyEnum = FindType("EnemyType");
                if (sEnemyEnum != null && sEnemyEnum.IsEnum)
                {
                    string n = Enum.GetName(sEnemyEnum, raw);
                    if (!string.IsNullOrEmpty(n)) return n;
                }
            }
            catch { }
            return null;
        }

        // ===== 场景中文名（由 scripts/gen_cn_block.py 从填好的对照表生成，改表后重跑脚本）=====
        /// <summary>中文名|代号 代号 ...；同名多房间共用一行</summary>
        private const string SceneNameData =
            "神居|GG_Atrium GG_Atrium_Roof GG_Blue_Room GG_Boss_Door_Entrance GG_Broken_Vessel GG_Brooding_Mawlek GG_Brooding_Mawlek_V GG_Collector GG_Collector_V GG_Crystal_Guardian GG_Crystal_Guardian_2 GG_Door_5_Finale GG_Dung_Defender GG_End_Sequence GG_Engine GG_Engine_Prime GG_Engine_Root GG_Entrance_Cutscene GG_Failed_Champion GG_False_Knight GG_Flukemarm GG_Ghost_Galien GG_Ghost_Gorb GG_Ghost_Gorb_V GG_Ghost_Hu GG_Ghost_Markoth GG_Ghost_Markoth_V GG_Ghost_Marmu GG_Ghost_Marmu_V GG_Ghost_No_Eyes GG_Ghost_No_Eyes_V GG_Ghost_Xero GG_Ghost_Xero_V GG_God_Tamer GG_Grey_Prince_Zote GG_Grimm GG_Grimm_Nightmare GG_Gruz_Mother GG_Gruz_Mother_V GG_Hive_Knight GG_Hollow_Knight GG_Hornet_1 GG_Hornet_2 GG_Land_of_Storms GG_Lost_Kin GG_Mage_Knight GG_Mage_Knight_V GG_Mantis_Lords GG_Mantis_Lords_V GG_Mega_Moss_Charger GG_Mighty_Zote GG_Nailmasters GG_Nosk GG_Nosk_Hornet GG_Nosk_V GG_Oblobbles GG_Painter GG_Pipeway GG_Radiance GG_Sly GG_Soul_Master GG_Soul_Tyrant GG_Spa GG_Traitor_Lord GG_Unlock GG_Unlock_Wastes GG_Unn GG_Uumuu GG_Uumuu_V GG_Vengefly GG_Vengefly_V GG_Watcher_Knights GG_Waterways GG_White_Defender GG_Workshop GG_Wyrm Room_GG_Shortcut" + "\n" + "遗忘十字路|Crossroads_01 Crossroads_02 Crossroads_03 Crossroads_04 Crossroads_05 Crossroads_06 Crossroads_07 Crossroads_08 Crossroads_09 Crossroads_10 Crossroads_10_boss Crossroads_10_boss_defeated Crossroads_10_preload Crossroads_11_alt Crossroads_12 Crossroads_13 Crossroads_14 Crossroads_15 Crossroads_16 Crossroads_18 Crossroads_19 Crossroads_21 Crossroads_22 Crossroads_25 Crossroads_27 Crossroads_30 Crossroads_31 Crossroads_33 Crossroads_35 Crossroads_36 Crossroads_37 Crossroads_38 Crossroads_39 Crossroads_40 Crossroads_42 Crossroads_43 Crossroads_45 Crossroads_46 Crossroads_46b Crossroads_47 Crossroads_48 Crossroads_49 Crossroads_49b Crossroads_50 Crossroads_52 Crossroads_ShamanTemple" + "\n" +
            "泪水之城|Ruins1_01 Ruins1_02 Ruins1_03 Ruins1_04 Ruins1_05 Ruins1_05b Ruins1_05c Ruins1_06 Ruins1_09 Ruins1_17 Ruins1_18 Ruins1_23 Ruins1_24 Ruins1_24_boss Ruins1_24_boss_defeated Ruins1_25 Ruins1_27 Ruins1_28 Ruins1_29 Ruins1_30 Ruins1_31 Ruins1_31b Ruins1_32 Ruins2_01 Ruins2_01_b Ruins2_03 Ruins2_03_boss Ruins2_03b Ruins2_04 Ruins2_05 Ruins2_06 Ruins2_07 Ruins2_08 Ruins2_09 Ruins2_10 Ruins2_10b Ruins2_11 Ruins2_11_b Ruins2_11_boss Ruins2_Watcher_Room Ruins_Bathhouse Ruins_Elevator Ruins_House_01 Ruins_House_02 Ruins_House_03" + "\n" + "苍绿之径|Fungus1_01 Fungus1_01b Fungus1_02 Fungus1_03 Fungus1_04 Fungus1_04_boss Fungus1_05 Fungus1_06 Fungus1_07 Fungus1_08 Fungus1_09 Fungus1_10 Fungus1_11 Fungus1_12 Fungus1_13 Fungus1_14 Fungus1_15 Fungus1_16_alt Fungus1_17 Fungus1_19 Fungus1_20_v02 Fungus1_21 Fungus1_22 Fungus1_23 Fungus1_24 Fungus1_25 Fungus1_26 Fungus1_28 Fungus1_29 Fungus1_30 Fungus1_31 Fungus1_32 Fungus1_34 Fungus1_35 Fungus1_36 Fungus1_37 Fungus1_Slug" + "\n" +
            "真菌荒地|Fungus2_01 Fungus2_02 Fungus2_03 Fungus2_04 Fungus2_05 Fungus2_06 Fungus2_07 Fungus2_08 Fungus2_09 Fungus2_10 Fungus2_11 Fungus2_12 Fungus2_13 Fungus2_14 Fungus2_15 Fungus2_15_boss Fungus2_15_boss_defeated Fungus2_17 Fungus2_18 Fungus2_19 Fungus2_20 Fungus2_21 Fungus2_23 Fungus2_25 Fungus2_26 Fungus2_28 Fungus2_29 Fungus2_30 Fungus2_31 Fungus2_32 Fungus2_33 Fungus2_34" + "\n" + "水晶山峰|Mines_01 Mines_02 Mines_03 Mines_04 Mines_05 Mines_06 Mines_07 Mines_10 Mines_11 Mines_13 Mines_16 Mines_17 Mines_18 Mines_18_boss Mines_19 Mines_20 Mines_23 Mines_24 Mines_25 Mines_28 Mines_29 Mines_30 Mines_31 Mines_32 Mines_33 Mines_34 Mines_35 Mines_36 Mines_37" + "\n" +
            "深邃巢穴|Deepnest_01 Deepnest_01b Deepnest_02 Deepnest_03 Deepnest_09 Deepnest_10 Deepnest_14 Deepnest_16 Deepnest_17 Deepnest_26 Deepnest_26b Deepnest_30 Deepnest_31 Deepnest_32 Deepnest_33 Deepnest_34 Deepnest_35 Deepnest_36 Deepnest_37 Deepnest_38 Deepnest_39 Deepnest_40 Deepnest_41 Deepnest_42 Deepnest_43 Deepnest_44 Deepnest_45_v02 Deepnest_Spider_Town Room_spider_small" + "\n" + "王国边境|Deepnest_East_01 Deepnest_East_02 Deepnest_East_03 Deepnest_East_04 Deepnest_East_06 Deepnest_East_07 Deepnest_East_08 Deepnest_East_09 Deepnest_East_10 Deepnest_East_11 Deepnest_East_12 Deepnest_East_13 Deepnest_East_14 Deepnest_East_14b Deepnest_East_15 Deepnest_East_16 Deepnest_East_17 Deepnest_East_18 Deepnest_East_Hornet Deepnest_East_Hornet_boss" + "\n" +
            "白色宫殿|White_Palace_01 White_Palace_02 White_Palace_03_hub White_Palace_04 White_Palace_05 White_Palace_06 White_Palace_07 White_Palace_08 White_Palace_09 White_Palace_11 White_Palace_12 White_Palace_13 White_Palace_14 White_Palace_15 White_Palace_16 White_Palace_17 White_Palace_18 White_Palace_19 White_Palace_20" + "\n" + "雾之峡谷|Fungus3_01 Fungus3_02 Fungus3_03 Fungus3_24 Fungus3_25 Fungus3_25b Fungus3_26 Fungus3_27 Fungus3_28 Fungus3_30 Fungus3_34 Fungus3_35 Fungus3_39 Fungus3_40 Fungus3_40_boss Fungus3_44 Fungus3_47" + "\n" +
            "王后花园|Fungus3_04 Fungus3_05 Fungus3_08 Fungus3_10 Fungus3_11 Fungus3_13 Fungus3_21 Fungus3_22 Fungus3_23 Fungus3_23_boss Fungus3_48 Fungus3_49 Fungus3_50 Fungus3_archive Fungus3_archive_02 Fungus3_archive_02_boss" + "\n" + "皇家下水道|Waterways_01 Waterways_02 Waterways_03 Waterways_04 Waterways_04b Waterways_05 Waterways_05_boss Waterways_06 Waterways_07 Waterways_08 Waterways_09 Waterways_12 Waterways_12_boss Waterways_13 Waterways_14 Waterways_15" + "\n" +
            "古老盆地|Abyss_01 Abyss_02 Abyss_03 Abyss_03_b Abyss_03_c Abyss_04 Abyss_05 Abyss_08 Abyss_17 Abyss_18 Abyss_19 Abyss_20 Abyss_21 Abyss_22" + "\n" + "梦境|Dream_01_False_Knight Dream_02_Mage_Lord Dream_03_Infected_Knight Dream_04_White_Defender Dream_Abyss Dream_Backer_Shrine Dream_Final Dream_Final_Boss Dream_Guardian_Hegemol Dream_Guardian_Lurien Dream_Guardian_Monomon Dream_Mighty_Zote Dream_Nailcollection Dream_Room_Believer_Shrine" + "\n" +
            "安息之地|RestingGrounds_02 RestingGrounds_02_boss RestingGrounds_04 RestingGrounds_05 RestingGrounds_06 RestingGrounds_07 RestingGrounds_08 RestingGrounds_09 RestingGrounds_10 RestingGrounds_12 RestingGrounds_17" + "\n" + "愚人斗兽场|GG_Lurker Room_Colosseum_01 Room_Colosseum_02 Room_Colosseum_Bronze Room_Colosseum_Gold Room_Colosseum_Silver Room_Colosseum_Spectate" + "\n" +
            "呼啸悬崖|Cliffs_01 Cliffs_02 Cliffs_02_boss Cliffs_03 Cliffs_04 Cliffs_05 Cliffs_06" + "\n" + "深渊|Abyss_06_Core Abyss_09 Abyss_10 Abyss_12 Abyss_15 Abyss_16 Abyss_Lighthouse_room" + "\n" +
            "蜂巢|Hive_01 Hive_02 Hive_03 Hive_03_c Hive_04 Hive_05" + "\n" + "格林剧团|Grimm_Divine Grimm_Main_Tent Grimm_Main_Tent_boss Grimm_Nightmare" + "\n" +
            "德特茅斯|Room_Jinn Room_Ouiji Town" + "\n" + "国王山道|Opening_Sequence Tutorial_01" + "\n" +
            "黑卵圣殿|Room_Final_Boss_Core";

        private static Dictionary<string, string> sSceneCn;

        /// <summary>场景短名 → 中文名。游戏里有些场景的运行时名字比资源名短，见 SceneCnName。</summary>
        private static Dictionary<string, string> sSceneAlias;

        /// <summary>
        /// 解析 SceneNameData（「中文名|代号 代号 ...」一行一个名字）。
        /// 同一名字对应多个代号 = 一个大地名管着一片房间（白色宫殿就管着 19 个房间）。
        /// </summary>
        private static void EnsureSceneCn()
        {
            if (sSceneCn != null) return;

            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                string[] lines = SceneNameData.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;

                    string cn = line.Substring(0, bar).Trim();
                    if (cn.Length == 0) continue;

                    string[] codes = line.Substring(bar + 1)
                        .Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    for (int k = 0; k < codes.Length; k++) d[codes[k]] = cn;
                }
            }
            catch { }

            sSceneCn = d;
        }

        /// <summary>
        /// 场景代号 → 显示名：表里有就用地名（白色宫殿），没有就回落成原始代号。
        /// 一个大地名管着几十个房间，但显示层不补「·06」这种房间号 —— 统计要的是「在哪片区域」。
        /// 想看具体房间代号，看右上角 HUD 第二行，那里永远是原始 sceneName。
        /// </summary>
        private static string SceneCnName(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            if (!sUseCnSceneNames) return code.Replace('_', ' ');

            EnsureSceneCn();

            string cn;
            if (sSceneCn.TryGetValue(code, out cn)) return cn;

            // 有些场景「运行时名字」比「资源名」短：辐光战的场景资源叫 Dream_Final_Boss，
            // 但 GameManager.sceneName 给的却是 Dream_Final，表里按资源名填的就查不到，
            // 于是掉回英文显示成「Dream Final」。这里拿下划线切前缀再查一次就能补上。
            // 只用「唯一归属」的前缀：同一个前缀指向两个不同地名时不猜，宁可显示代号。
            EnsureSceneAlias();
            if (sSceneAlias.TryGetValue(code, out cn) && cn != null) return cn;

            return code.Replace('_', ' ');
        }

        /// <summary>预先把每个场景代号的所有下划线前缀列出来，指回它自己的中文名。</summary>
        private static void EnsureSceneAlias()
        {
            if (sSceneAlias != null) return;
            sSceneAlias = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (KeyValuePair<string, string> kv in sSceneCn)
                {
                    string k = kv.Key;
                    int us = k.IndexOf('_');
                    while (us > 0 && us < k.Length - 1)
                    {
                        string prefix = k.Substring(0, us);
                        string prev;
                        if (!sSceneAlias.TryGetValue(prefix, out prev)) sSceneAlias[prefix] = kv.Value;
                        else if (prev != kv.Value) sSceneAlias[prefix] = null;   // 有歧义，不猜
                        us = k.IndexOf('_', us + 1);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 场景排行按显示名（大地名）合并：白色宫殿有 19 个房间，
        /// 不合并的话右栏会出现十几行一模一样的「白色宫殿」，看不出到底死了几次。
        /// 表里没填的房间仍走原始代号，各自独立，不会互相并进去。
        /// </summary>
        private static Dictionary<string, int> MergeScenesByArea(Dictionary<string, int> byScene)
        {
            Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> kv in byScene)
            {
                string area = SceneCnName(kv.Key);
                int n;
                d.TryGetValue(area, out n);
                d[area] = n + kv.Value;
            }
            return d;
        }

        private static string CleanName(string n)
        {
            if (string.IsNullOrEmpty(n)) return n;
            return n.Replace("(Clone)", "").Replace(" Clone", "").Trim();
        }

        private static string CurrentScene()
        {
            try
            {
                Type gm = FindType("GameManager");
                if (gm != null)
                {
                    object inst = GetStaticMember(gm, "instance", "Instance");
                    if (inst != null)
                    {
                        object sn = GetMember(inst, "sceneName");
                        if (sn != null && !string.IsNullOrEmpty(sn.ToString())) return sn.ToString();
                    }
                }
            }
            catch { }
            try
            {
                string n = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            return "未知场景";
        }

        // ================= 原生 UI =================

        private void CreateUI()
        {
            try
            {
                uiFont = Font.CreateDynamicFontFromOSFont(
                    new string[] { "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "Noto Sans CJK SC" }, 22);
            }
            catch (Exception e) { L.LogWarning("[死亡统计] 中文字体加载失败: " + e.Message); }

            uiRoot = new GameObject("DeathStatsUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.transform.SetParent(transform, false);

            Canvas canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;

            CanvasScaler scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 按钮点击不用 EventSystem：自建 EventSystem 会抢游戏自己的输入事件，
            // 把方向键之类的按键「顶掉」。这里改成自己按屏幕坐标命中检测。
            clickTargets.Clear();
            focusItems.Clear();
            focusIndex = 0;

            // 面板
            RectTransform panel = NewRect("Panel", uiRoot.transform);
            uiPanel = panel;
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1680f, 940f);
            panel.anchoredPosition = Vector2.zero;
            Image bg = panel.gameObject.AddComponent<Image>();
            bg.color = new Color(0.045f, 0.05f, 0.06f, 0.97f);
            bg.raycastTarget = true;

            MakeText(panel, "Title", "死亡统计", 34, new Color(0.87f, 0.8f, 0.58f), TextAnchor.MiddleLeft, 44f, 26f, 520f, 48f, FontStyle.Bold);

            txtSubtitle = MakeText(panel, "Subtitle", "", 19, new Color(0.68f, 0.7f, 0.72f), TextAnchor.MiddleLeft, 44f, 86f, 900f, 30f, FontStyle.Normal);

            // 右上角三个功能按钮默认隐藏（Display/ShowButtons 可开回来）。
            // 隐藏的只是界面，DumpToLog / InjectTestData / OnClearClicked 全部保留，
            // 既能靠配置放出来，也能靠 Hotkeys 里的三个键触发。
            ClickTarget ctDump = null, ctInject = null, ctClear = null;
            if (cfgShowButtons.Value)
            {
                Text dumpLabel, injectLabel;
                ctDump = MakeButton(panel, "输出日志", 1140f, 32f, 150f, 42f, DumpToLog, out dumpLabel);
                ctInject = MakeButton(panel, "注入测试", 1310f, 32f, 150f, 42f, InjectTestData, out injectLabel);
                ctClear = MakeButton(panel, "清空", 1480f, 32f, 150f, 42f, OnClearClicked, out txtClear);
            }

            // 槽位标签页
            string[] tabNames = { "全部", "槽1", "槽2", "槽3", "槽4" };
            List<ClickTarget> tabCTs = new List<ClickTarget>();
            for (int i = 0; i < 5; i++)
            {
                int captured = i;
                float x = 44f + i * 132f;
                Image tabBg = MakeRectImage(panel, "Tab" + i, x, 128f, 120f, 44f, SlotBgOff);
                ClickTarget ctTab = RegisterClick(tabBg.rectTransform, MakeSlotSwitch(captured), captured);
                Text lb = MakeText(tabBg.transform, "Label", tabNames[i], 19, Color.white, TextAnchor.MiddleCenter, 0f, 0f, 120f, 44f, FontStyle.Normal);
                if (ctTab != null)
                {
                    ctTab.bg = tabBg;
                    ctTab.baseColor = delegate { return viewSlot == captured ? SlotBgIdle : SlotBgOff; };
                    ctTab.label = lb;
                    ctTab.labelColor = delegate { return viewSlot == captured ? SlotTextIdle : SlotTextOff; };
                }
                // 当前槽位的第二重视觉标记：底部一条金线（和"焦点项整块金底"区分开）
                tabUnder[i] = MakeRectImage(panel, "TabU" + i, x, 169f, 120f, 3f, new Color(0.9f, 0.76f, 0.38f, 0.95f));
                tabUnder[i].gameObject.SetActive(false);

                tabCTs.Add(ctTab);
                tabBgs[i] = tabBg;
                tabLabels[i] = lb;
            }

            // 死亡标记开关：挪到标题栏最右侧，离标签页远远的。
            // 以前紧贴「槽4」右边（间距 12px，和标签之间的间距一模一样），
            // 又是个同尺寸实心金块，眼睛会把它当成第 6 个标签页，看起来像标签页画坏了。
            // 现在右对齐 + 描边样式，一眼就能看出这是另一个控件。
            ctMarkerToggle = MakeSwitchButton(panel, "开关_标记", 1428f, 128f, 208f, 44f,
                ToggleMarkers, out markerToggleLabel);

            // 左 / 右两栏各自的「可点区域」。焦点移到哪一栏，↑↓ 就滚哪一栏；
            // 鼠标点一下栏里的空白处也能切过去，比记按键省事。
            Image leftZone = MakeRectImage(panel, "ColLeft", 40f, 188f, 748f, 642f, new Color(0f, 0f, 0f, 0f));
            ClickTarget ctLeft = RegisterClick(leftZone.rectTransform, MakeColSwitch(0), -1, 0);
            Image rightZone = MakeRectImage(panel, "ColRight", 852f, 188f, 750f, 642f, new Color(0f, 0f, 0f, 0f));
            ClickTarget ctRight = RegisterClick(rightZone.rectTransform, MakeColSwitch(1), -1, 1);

            // 焦点列表分两段：前面是「上层」（标签页 + 按钮），末尾两项是「栏区」（左栏/右栏）。
            // 这样 topItemCount 之后就是两栏，索引区间直接对应层，不用额外做映射。
            focusItems.AddRange(tabCTs);
            if (ctMarkerToggle != null) focusItems.Add(ctMarkerToggle);
            if (ctDump != null) focusItems.Add(ctDump);
            if (ctInject != null) focusItems.Add(ctInject);
            if (ctClear != null) focusItems.Add(ctClear);
            topItemCount = focusItems.Count;
            if (ctLeft != null) focusItems.Add(ctLeft);
            if (ctRight != null) focusItems.Add(ctRight);

            // 列头：左「死因」右「场景」两张独立排行，互不组合
            Color headColor = new Color(0.55f, 0.57f, 0.6f);
            hKiller[0] = MakeText(panel, "H1", "死因", 20, headColor, TextAnchor.MiddleLeft, 44f, 192f, 440f, 30f, FontStyle.Bold);
            hKiller[1] = MakeText(panel, "H2", "次数", 20, headColor, TextAnchor.MiddleRight, 500f, 192f, 100f, 30f, FontStyle.Bold);
            hKiller[2] = MakeText(panel, "H3", "占比", 20, headColor, TextAnchor.MiddleRight, 610f, 192f, 180f, 30f, FontStyle.Bold);
            hScene[0] = MakeText(panel, "H4", "场景", 20, headColor, TextAnchor.MiddleLeft, 856f, 192f, 440f, 30f, FontStyle.Bold);
            hScene[1] = MakeText(panel, "H5", "次数", 20, headColor, TextAnchor.MiddleRight, 1312f, 192f, 100f, 30f, FontStyle.Bold);
            hScene[2] = MakeText(panel, "H6", "占比", 20, headColor, TextAnchor.MiddleRight, 1422f, 192f, 180f, 30f, FontStyle.Bold);

            // 中缝竖线 + 表头下横线
            MakeRectImage(panel, "VDiv", 824f, 186f, 1f, 650f, new Color(0.22f, 0.23f, 0.25f, 0.9f));
            MakeRectImage(panel, "Line", 44f, 226f, 1592f, 1f, new Color(0.25f, 0.26f, 0.28f, 0.7f));

            // 激活栏的指示线：压在表头下横线上，只有当前在滚的那一栏是亮的
            underLeft = MakeRectImage(panel, "UnderL", 44f, 225f, 744f, 2f, new Color(0f, 0f, 0f, 0f));
            underRight = MakeRectImage(panel, "UnderR", 856f, 225f, 746f, 2f, new Color(0f, 0f, 0f, 0f));

            if (cfgShowHud.Value)
                MakeText(panel, "Hint", "↑↓ 滚动 · ←→ 换栏 · 回车确认 · ESC 关闭",
                    16, new Color(0.45f, 0.47f, 0.5f), TextAnchor.MiddleLeft, 44f, 884f, 900f, 26f, FontStyle.Normal);

            // 键盘焦点框（4 条细边，跟着当前选中项走；用边框而不是换底色，
            // 这样不会和槽位标签页自己的"当前槽"高亮打架）
            Color focusCol = new Color(1f, 0.85f, 0.42f, 0.95f);
            string[] edgeNames = { "FocusT", "FocusB", "FocusL", "FocusR" };
            for (int i = 0; i < 4; i++)
            {
                focusEdges[i] = MakeRectImage(panel, edgeNames[i], 0f, 0f, 0f, 0f, focusCol);
                focusEdges[i].gameObject.SetActive(false);
            }

            // 自绘光标：外圈深色 + 内芯亮色，挂在 uiRoot 下，永远压在面板上面
            cursorEdge = MakeCursorPart("CursorEdge", uiRoot.transform, 18f, new Color(0f, 0f, 0f, 0.85f));
            cursorDot = MakeCursorPart("CursorDot", uiRoot.transform, 10f, new Color(1f, 0.92f, 0.6f, 1f));

            uiRoot.SetActive(false);
            uiDirty = true;

            CreateRoomHud();
        }

        // ================= 右上角房间名 HUD =================

        /// <summary>
        /// 建那条常驻的「当前房间」。位置固定在右上角，右对齐、贴边 20px。
        /// 两行：中文名（金色、粗体）+ 游戏原始场景代号（灰色、小一号）。
        /// </summary>
        private void CreateRoomHud()
        {
            if (hudRoot != null) return;

            hudRoot = new GameObject("RoomHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            hudRoot.transform.SetParent(transform, false);

            Canvas c = hudRoot.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 32010;   // 压在统计面板(32000)上面，开着面板也看得见

            CanvasScaler sc = hudRoot.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;

            hudBox = NewRect("RoomHudBox", hudRoot.transform);
            // 锚到屏幕右上角，自己的右上角也作为轴心 → 窗口分辨率变了也一直贴着右上
            hudBox.anchorMin = new Vector2(1f, 1f);
            hudBox.anchorMax = new Vector2(1f, 1f);
            hudBox.pivot = new Vector2(1f, 1f);
            hudBox.sizeDelta = new Vector2(640f, 76f);
            hudBox.anchoredPosition = new Vector2(-20f, -16f);

            hudBg = hudBox.gameObject.AddComponent<Image>();
            hudBg.color = new Color(0f, 0f, 0f, 0.55f);
            hudBg.raycastTarget = false;

            hudNameText = MakeText(hudBox, "RoomHudName", "", 22,
                new Color(1f, 0.86f, 0.5f), TextAnchor.MiddleRight, 10f, 4f, 620f, 36f, FontStyle.Bold);
            hudCodeText = MakeText(hudBox, "RoomHudCode", "", 16,
                new Color(0.62f, 0.65f, 0.68f), TextAnchor.MiddleRight, 10f, 44f, 620f, 24f, FontStyle.Normal);

            hudLastScene = null;
            hudNextPoll = 0f;
            hudAppliedSize = -1f;

            hudRoot.SetActive(cfgRoomHud.Value);
            LayoutRoomHud();
        }

        private void UpdateRoomHud()
        {
            if (hudRoot == null) return;

            if (hudRoot.activeSelf != cfgRoomHud.Value) hudRoot.SetActive(cfgRoomHud.Value);
            if (!cfgRoomHud.Value) return;

            // 改配置文件（ConfigurationManager 那种热改）也要立刻跟上，不用重开游戏
            if (!Mathf.Approximately(hudAppliedSize, cfgRoomHudSize.Value) || hudAppliedShowCode != cfgRoomHudCode.Value)
            {
                hudAppliedSize = cfgRoomHudSize.Value;
                hudAppliedShowCode = cfgRoomHudCode.Value;
                LayoutRoomHud();
            }

            if (Time.unscaledTime < hudNextPoll) return;
            hudNextPoll = Time.unscaledTime + 0.15f;   // 场景名不会一秒变几次，没必要每帧反射

            string code = CurrentScene();
            if (code == hudLastScene) return;
            hudLastScene = code;
            ApplyRoomHudText(code);
        }

        // ================= 死亡位置标记 =================

        /// <summary>
        /// 每 0.3 秒对一次场景名：换房间就重建标记。标记是场景里的物体，不是 UI，
        /// 所以面板关着也照样看得见。
        /// </summary>
        private void UpdateMarkers()
        {
            if (cfgShowMarkers == null) return;

            if (!cfgShowMarkers.Value)
            {
                if (sMarkers.Count > 0) ClearMarkers();
                markerScene = null;
                return;
            }

            if (Time.unscaledTime < markerNextPoll) return;
            markerNextPoll = Time.unscaledTime + 0.3f;

            // 改配置里的缩放值不用重启，下个轮询周期重建一次
            float sc = cfgMarkerScale != null ? cfgMarkerScale.Value : 1f;
            if (!Mathf.Approximately(sc, markerScaleApplied))
            {
                markerScaleApplied = sc;
                markerScene = null;
            }

            string scene = CurrentScene();
            if (scene != markerScene) RebuildMarkers(scene);
        }

        private void ClearMarkers()
        {
            for (int i = 0; i < sMarkers.Count; i++)
            {
                if (sMarkers[i] != null) UnityEngine.Object.Destroy(sMarkers[i]);
            }
            sMarkers.Clear();
        }

        /// <summary>把当前房间里所有「存过坐标」的死亡记录摆成图钉</summary>
        private void RebuildMarkers(string scene)
        {
            ClearMarkers();
            markerScene = scene;

            if (string.IsNullOrEmpty(scene)) return;
            if (GetHero() == null) return;      // 菜单 / 读档动画里不摆

            // 渲染层次每个房间只算一次：跟玩家同一 sortingLayer、同一 order，
            // 只把 z 往后挪一点。HK 是 2D 正交相机，z 越大离相机越远，
            // 所以图钉必然压在玩家和怪物后面，不会挡住人。
            string layer = "Default";
            int order = 0;
            float baseZ = 0.1f;
            object hero = GetHero();
            Component hc = hero as Component;
            if (hc != null)
            {
                baseZ = hc.transform.position.z + 0.08f;
                SpriteRenderer hs = hc.GetComponentInChildren<SpriteRenderer>(true);
                if (hs != null)
                {
                    layer = hs.sortingLayerName;
                    order = hs.sortingOrder;
                }
            }
            order += cfgMarkerOrder != null ? cfgMarkerOrder.Value : 0;

            // 老记录（AnchorVer < 2）存的是当年 transform.position，图钉贴在头顶。
            // 老存档没法回溯当时的身高，但同一房间骑士身高是固定的，所以用「当前」骑士
            // 算一份等量偏移，把老图钉整体平移到新落点上 —— 不用清空重记。
            float legacyDelta = 0f;
            if (hc != null)
            {
                float pin = MarkerAnchorY(hc, hc.transform.position.y);
                float d = pin - hc.transform.position.y;
                if (Mathf.Abs(d) < 3f) legacyDelta = d;
            }

            int count = 0;
            foreach (KeyValuePair<int, SlotStats> kv in slots)
            {
                foreach (DeathRecord r in kv.Value.Records)
                {
                    if (!r.HasPos || r.Scene != scene) continue;
                    if (SpawnMarker(r, layer, order, baseZ, legacyDelta)) count++;
                }
            }

            if (L != null && count > 0)
                L.LogInfo("[死亡统计] 本房间摆出 " + count + " 个死亡标记（" + scene + "）"
                    + " | 身高=" + sHeroHeight.ToString("0.00")
                    + " 脚底偏移=" + sHeroFootOffset.ToString("0.00")
                    + " 老记录下移=" + legacyDelta.ToString("0.00"));
        }

        /// <summary>
        /// 图钉的落点高度：按角色包围盒算，落在「从脚底往上 ratio」处（默认 1/3）。
        /// 之前直接用 transform.position，图钉基本贴在头顶；改成包围盒之后，
        /// 不管骑士的 pivot 在脚底还是腰上，落点都稳定在身体下部。
        /// 包围盒读不到/尺寸异常时退回原坐标，宁可略偏也不能飞到天上去。
        /// </summary>
        /// <summary>
        /// 骑士的「身体」包围盒：优先取子物体里最高的 SpriteRenderer（避开刀光、粒子这些特效），
        /// 精灵读不到时退到碰撞体。死亡动画把本体关掉时两者都可能拿不到，返回 false。
        /// </summary>
        private static bool TryHeroBounds(Component hero, out Bounds b)
        {
            b = new Bounds();
            if (hero == null) return false;
            try
            {
                SpriteRenderer[] srs = hero.GetComponentsInChildren<SpriteRenderer>(true);
                if (srs != null && srs.Length > 0)
                {
                    b = srs[0].bounds;
                    for (int i = 1; i < srs.Length; i++)
                    {
                        if (srs[i].bounds.size.y > b.size.y) b = srs[i].bounds;
                    }
                    if (b.size.y >= 0.3f && b.size.y <= 12f) return true;
                }

                Collider2D[] cols = hero.GetComponentsInChildren<Collider2D>(true);
                if (cols != null && cols.Length > 0)
                {
                    b = cols[0].bounds;
                    for (int i = 1; i < cols.Length; i++)
                    {
                        if (cols[i].bounds.size.y > b.size.y) b = cols[i].bounds;
                    }
                    if (b.size.y >= 0.1f && b.size.y <= 12f) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>活着的时候定期采一次身体尺寸，死亡时才有得用（见 sHeroHeight 的注释）。</summary>
        private void SampleHeroBounds()
        {
            if (Time.unscaledTime < heroSampleNext) return;
            heroSampleNext = Time.unscaledTime + 0.5f;

            try
            {
                Component hc = GetHero() as Component;
                if (hc == null) return;

                Bounds b;
                if (!TryHeroBounds(hc, out b)) return;

                sHeroHeight = b.size.y;
                sHeroFootOffset = b.min.y - hc.transform.position.y;
            }
            catch { }
        }

        /// <summary>
        /// 图钉的落点高度：落在「从脚底往上 ratio」处（默认 1/3）。
        /// 先试实时包围盒；死亡瞬间本体被关掉时，退回活着时缓存的身高推算。
        /// 两条路都不成立（刚进房间就死、尺寸异常）才用原坐标。
        /// </summary>
        private float MarkerAnchorY(Component hero, float fallbackY)
        {
            if (hero == null) return fallbackY;
            float ratio = cfgMarkerHeightRatio != null
                ? Mathf.Clamp01(cfgMarkerHeightRatio.Value) : 0.33f;

            Bounds b;
            if (TryHeroBounds(hero, out b))
            {
                float y = Mathf.Lerp(b.min.y, b.max.y, ratio);
                if (Mathf.Abs(y - fallbackY) <= 3f) return y;
            }

            if (sHeroHeight >= 0.3f)
            {
                float y = fallbackY + sHeroFootOffset + sHeroHeight * ratio;
                if (Mathf.Abs(y - fallbackY) <= 3f) return y;
            }

            return fallbackY;
        }

        private bool SpawnMarker(DeathRecord r, string layer, int order, float baseZ, float legacyDelta)
        {
            try
            {
                Sprite sp = GetMarkerSprite();

                GameObject go = new GameObject("DeathMarker");
                // 老记录的坐标要补一个下移量，否则还是贴在头顶
                float y = r.AnchorVer < 2 ? r.Y + legacyDelta : r.Y;
                go.transform.position = new Vector3(r.X, y, baseZ);
                float scale = Mathf.Clamp(cfgMarkerScale != null ? cfgMarkerScale.Value : 1f, 0.25f, 3f);
                go.transform.localScale = new Vector3(scale, scale, 1f);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sp;
                sr.color = new Color(1f, 1f, 1f, 0.92f);
                try { sr.sortingLayerName = layer; } catch { }
                sr.sortingOrder = order;

                sMarkers.Add(go);
                return true;
            }
            catch (Exception e)
            {
                if (L != null) L.LogWarning("[死亡统计] 生成死亡标记失败: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// 代码里画一个地图图钉：红底、黑边、中间一个黑叉。
        /// 不用任何图片资源，所以不存在贴图丢失的问题。
        /// </summary>
        private Sprite GetMarkerSprite()
        {
            if (markerSprite != null) return markerSprite;

            const int S = 96;
            const int SS = 2;                  // 2x2 超采样，边缘不至于太毛
            Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;

            float cx = S * 0.5f;
            float cyHead = S * 0.665f;         // 圆头中心（Unity 的 y 轴朝上，所以偏上）
            float rOut = S * 0.315f;           // 黑边外缘
            float rIn = S * 0.255f;            // 红色主体
            float tip = S * 0.05f;             // 底部的尖
            float arm = S * 0.125f;            // 叉的臂长
            float half = S * 0.034f;           // 叉的笔画半宽

            Color32 ink = new Color32(16, 16, 18, 255);
            Color32 red = new Color32(240, 88, 88, 240);

            Color32[] px = new Color32[S * S];
            int n = SS * SS;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int ar = 0, ag = 0, ab = 0, aa = 0;
                    for (int sy = 0; sy < SS; sy++)
                    {
                        for (int sx = 0; sx < SS; sx++)
                        {
                            float fx = x + (sx + 0.5f) / SS;
                            float fy = y + (sy + 0.5f) / SS;
                            Color32 c = new Color32(0, 0, 0, 0);

                            if (InPinShape(fx, fy, cx, cyHead, rOut, tip)) c = ink;
                            if (InPinShape(fx, fy, cx, cyHead, rIn, tip + S * 0.045f)) c = red;
                            if (InCross(fx, fy, cx, cyHead, arm, half)) c = ink;

                            ar += c.r; ag += c.g; ab += c.b; aa += c.a;
                        }
                    }
                    px[y * S + x] = new Color32((byte)(ar / n), (byte)(ag / n), (byte)(ab / n), (byte)(aa / n));
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, false);

            // 96px = 1 世界单位；轴心放在尖点上，标记就正好"钉"在死亡坐标处
            markerSprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, tip / S), S);
            return markerSprite;
        }

        /// <summary>图钉形状 = 圆头 ∪ 下方三角形</summary>
        private static bool InPinShape(float fx, float fy, float cx, float cyHead, float r, float tipY)
        {
            float dx = fx - cx;
            float dy = fy - cyHead;
            if (dx * dx + dy * dy <= r * r) return true;

            // 三角：尖端 (cx,tipY)，两个上角落在圆头下半部
            return InTriangle(fx, fy,
                cx, tipY,
                cx - r * 0.78f, cyHead + r * 0.30f,
                cx + r * 0.78f, cyHead + r * 0.30f);
        }

        private static bool InTriangle(float px, float py,
            float ax, float ay, float bx, float by, float cx2, float cy2)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx2) * (by - cy2) - (bx - cx2) * (py - cy2);
            float d3 = (px - ax) * (cy2 - ay) - (cx2 - ax) * (py - ay);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        private static bool InCross(float fx, float fy, float cx, float cy, float arm, float half)
        {
            if (DistanceToSegment(fx, fy, cx - arm, cy + arm, cx + arm, cy - arm) <= half) return true;
            if (DistanceToSegment(fx, fy, cx - arm, cy - arm, cx + arm, cy + arm) <= half) return true;
            return false;
        }

        private static float DistanceToSegment(float px, float py, float x1, float y1, float x2, float y2)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 0.0001f ? 0f : ((px - x1) * dx + (py - y1) * dy) / len2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float qx = x1 + t * dx;
            float qy = y1 + t * dy;
            float ddx = px - qx;
            float ddy = py - qy;
            return Mathf.Sqrt(ddx * ddx + ddy * ddy);
        }

        /// <summary>按配置重排两行文字的高度/字号，代号那行关掉时背景条也跟着变矮</summary>
        private void LayoutRoomHud()
        {
            bool showCode = cfgRoomHudCode.Value;
            float nameSize = Mathf.Clamp(cfgRoomHudSize.Value, 10f, 60f);
            float nameH = nameSize + 14f;
            float codeH = Mathf.Max(18f, nameSize * 0.72f + 8f);

            if (hudBox != null)
                hudBox.sizeDelta = new Vector2(640f, (showCode ? nameH + codeH : nameH) + 8f);

            if (hudNameText != null)
            {
                hudNameText.fontSize = Mathf.RoundToInt(nameSize);
                hudNameText.rectTransform.sizeDelta = new Vector2(620f, nameH);
                hudNameText.rectTransform.anchoredPosition = new Vector2(10f, -4f);
            }

            if (hudCodeText != null)
            {
                hudCodeText.gameObject.SetActive(showCode);
                hudCodeText.fontSize = Mathf.Max(9, Mathf.RoundToInt(nameSize * 0.72f));
                hudCodeText.rectTransform.sizeDelta = new Vector2(620f, codeH);
                hudCodeText.rectTransform.anchoredPosition = new Vector2(10f, -4f - nameH);
            }
        }

        private void ApplyRoomHudText(string code)
        {
            if (hudNameText == null) return;

            string name;
            if (string.IsNullOrEmpty(code)) name = "读取场景中…";
            else if (IsNonGameplayScene(code)) name = "（菜单 / 非游戏场景）";
            else name = SceneCnName(code);

            hudNameText.text = name;
            if (hudCodeText != null) hudCodeText.text = code ?? "";
        }

        /// <summary>主菜单、过场、制作名单这些不是"房间"，显示中文名没意义</summary>
        private static bool IsNonGameplayScene(string code)
        {
            if (string.IsNullOrEmpty(code)) return true;
            if (code.StartsWith("Menu", StringComparison.OrdinalIgnoreCase)) return true;
            if (code.StartsWith("Pre_Menu", StringComparison.OrdinalIgnoreCase)) return true;
            return code == "PermaDeath" || code == "End_Credits" || code == "Quit_To_Menu"
                || code == "Cinematic_Ending_A" || code == "Cinematic_Ending_B" || code == "Cinematic_Stag_Travel";
        }

        private static RectTransform MakeCursorPart(string name, Transform parent, float size, Color c)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return rt;
        }

        private void OnClearClicked()
        {
            if (pendingClear == "yes")
            {
                ClearCurrentView();
                pendingClear = null;
                if (txtClear != null) txtClear.text = "清空";
            }
            else
            {
                pendingClear = "yes";
                if (txtClear != null) txtClear.text = "确认清空?";
            }
        }

        private void RefreshUI()
        {
            if (uiRoot == null) return;

            ClearDynamic();

            // 配置文件在外面被改过时，按钮文字也得跟着变
            if (markerToggleLabel != null) markerToggleLabel.text = MarkerButtonText();

            // 聚合当前视图数据（只按死因、按场景各聚合一份，不做两两组合）
            Dictionary<string, int> byKiller = new Dictionary<string, int>();
            Dictionary<string, int> byScene = new Dictionary<string, int>();
            int total = 0;

            foreach (KeyValuePair<int, SlotStats> kv in slots)
            {
                if (viewSlot != 0 && kv.Key != viewSlot) continue;
                SlotStats st = kv.Value;
                total += st.Records.Count;
                MergeInto(byKiller, st.ByKiller);
                MergeInto(byScene, st.ByScene);
            }

            // 右栏按大地名合并：同属白色宫殿的 19 个房间算一行。
            // 这一步只影响显示，存档里存的仍然是原始 sceneName。
            byScene = MergeScenesByArea(byScene);

            string scope = viewSlot == 0 ? "全部槽位" : "槽位 " + viewSlot;
            if (txtSubtitle != null)
                txtSubtitle.text = scope + " · 累计死亡 " + total + " 次";

            // 底色和字色交给 UpdateHover 每帧统一算（焦点项=金底黑字，当前槽=暗金底金字），
            // 这里只维护「当前槽位」底部那条金线，避免两处各写一套颜色互相打架。
            for (int i = 0; i < 5; i++)
            {
                if (tabUnder[i] != null) tabUnder[i].gameObject.SetActive(i == viewSlot);
            }

            if (total == 0)
            {
                ResetScroll();
                maxScrollLeft = 0;
                maxScrollRight = 0;
                DynText(uiPanel, "Empty", "这个槽位还没有死亡记录，死一次就出现了。", 20,
                    new Color(0.5f, 0.52f, 0.55f), TextAnchor.MiddleLeft, 44f, 246f, 1200f, 28f);
                return;
            }

            // 两张独立排行：死因一张、场景一张，不做两两组合
            const float TOP = 234f;
            const float STEP = 30f;
            int rows = VISIBLE_ROWS;
            Color numColor = new Color(0.85f, 0.78f, 0.55f);
            Color pctColor = new Color(0.6f, 0.62f, 0.66f);

            // 两栏各自独立滚动：↑↓ 只作用于 activeCol 指向的那一栏
            maxScrollLeft = Math.Max(0, byKiller.Count - rows);
            maxScrollRight = Math.Max(0, byScene.Count - rows);
            scrollLeft = Mathf.Clamp(scrollLeft, 0, maxScrollLeft);
            scrollRight = Mathf.Clamp(scrollRight, 0, maxScrollRight);

            int lSkip = scrollLeft;
            int rSkip = scrollRight;

            // 表头只在「焦点真的在栏区里」时才变亮，避免顶部选标签时表头亮着、
            // 让人以为 ↑↓ 这会儿是滚动。
            Color headActive = new Color(0.93f, 0.8f, 0.5f);
            Color headIdle = new Color(0.5f, 0.52f, 0.55f);
            bool inCol = focusRow == 1;
            for (int i = 0; i < 3; i++)
            {
                if (hKiller[i] != null) hKiller[i].color = (inCol && activeCol == 0) ? headActive : headIdle;
                if (hScene[i] != null) hScene[i].color = (inCol && activeCol == 1) ? headActive : headIdle;
            }
            if (underLeft != null) underLeft.color = (inCol && activeCol == 0) ? headActive : new Color(0f, 0f, 0f, 0f);
            if (underRight != null) underRight.color = (inCol && activeCol == 1) ? headActive : new Color(0f, 0f, 0f, 0f);

            List<KeyValuePair<string, int>> killers = TopN(byKiller, lSkip + rows);
            for (int i = lSkip; i < killers.Count; i++)
            {
                float y = TOP + (i - lSkip) * STEP;
                DynText(uiPanel, "K" + i, Fit(killers[i].Key, 18), 22, Color.white, TextAnchor.MiddleLeft, 44f, y, 440f, 26f);
                DynText(uiPanel, "KN" + i, killers[i].Value.ToString(), 22, numColor, TextAnchor.MiddleRight, 500f, y, 100f, 26f);
                DynText(uiPanel, "KP" + i, Percent(killers[i].Value, total), 22, pctColor, TextAnchor.MiddleRight, 610f, y, 180f, 26f);
            }

            List<KeyValuePair<string, int>> scenes = TopN(byScene, rSkip + rows);
            // byScene 的 key 已经是合并后的地名了，直接拿来当标签，不用再做一次映射
            for (int i = rSkip; i < scenes.Count; i++)
            {
                float y = TOP + (i - rSkip) * STEP;
                DynText(uiPanel, "S" + i, Fit(scenes[i].Key, 26), 22, Color.white, TextAnchor.MiddleLeft, 856f, y, 440f, 26f);
                DynText(uiPanel, "SN" + i, scenes[i].Value.ToString(), 22, numColor, TextAnchor.MiddleRight, 1312f, y, 100f, 26f);
                DynText(uiPanel, "SP" + i, Percent(scenes[i].Value, total), 22, pctColor, TextAnchor.MiddleRight, 1422f, y, 180f, 26f);
            }

            // 底部：总量 + 当前激活栏的页码
            int colTotal = activeCol == 0 ? byKiller.Count : byScene.Count;
            int colScroll = activeCol == 0 ? scrollLeft : scrollRight;
            int colMax = activeCol == 0 ? maxScrollLeft : maxScrollRight;
            string colName = activeCol == 0 ? "死因栏" : "场景栏";

            string summary = "共 " + byKiller.Count + " 种死因 · " + byScene.Count + " 个场景";
            if (focusRow == 1 && colMax > 0)
            {
                summary += "  |  " + colName + " 第 " + (colScroll + 1) + "-"
                    + Math.Min(colScroll + rows, colTotal) + " 项 / 共 " + colTotal + " 项";
            }
            else if (maxScrollLeft > 0 || maxScrollRight > 0)
            {
                summary += "  |  " + colName + " 可滚动";
            }
            DynText(uiPanel, "Summary", summary, 17, new Color(0.45f, 0.47f, 0.5f),
                TextAnchor.MiddleLeft, 44f, TOP + rows * STEP + 6f, 1592f, 26f);

            // 刷新完顺手把焦点框摆好（列数变化不影响，框是跟着控件 RectTransform 走的）
            UpdateFocusFrame();
        }

        /// <summary>占比文本，保留一位小数</summary>
        private static string Percent(int part, int whole)
        {
            if (whole <= 0) return "0.0%";
            float v = part * 100f / whole;
            return v.ToString("0.0") + "%";
        }

        /// <summary>过长名称截断，避免压到次数列（完整名称见「输出日志」）</summary>
        private static string Fit(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max - 3) + "...";
        }

        private void ClearDynamic()
        {
            for (int i = 0; i < dynamicRows.Count; i++)
                if (dynamicRows[i] != null) Destroy(dynamicRows[i]);
            dynamicRows.Clear();
        }

        // ---- UI 构造工具 ----

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.localScale = Vector3.one;
            return rt;
        }

        private static Image MakeRectImage(Transform parent, string name, float x, float y, float w, float h, Color color)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        private Text MakeText(Transform parent, string name, string content, int size, Color color,
            TextAnchor anchor, float x, float y, float w, float h, FontStyle style)
        {
            return MakeText(parent, name, content, size, color, anchor, 0f, 0f, 0f, 0f, style, null, x, y, w, h);
        }

        private Text MakeText(Transform parent, string name, string content, int size, Color color, TextAnchor anchor,
            float ax, float ay, float aw, float ah, FontStyle style, Font f, float x, float y, float w, float h)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            Text txt = rt.gameObject.AddComponent<Text>();
            txt.font = f != null ? f : uiFont;
            txt.text = content;
            txt.fontSize = size;
            txt.color = color;
            txt.alignment = anchor;
            txt.fontStyle = style;
            txt.raycastTarget = false;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            return txt;
        }

        /// <summary>动态行：登记后可在下次刷新时整体销毁</summary>
        private Text DynText(Transform parent, string name, string content, int size, Color color,
            TextAnchor anchor, float x, float y, float w, float h)
        {
            Text t = MakeText(parent, name, content, size, color, anchor, x, y, w, h, FontStyle.Normal);
            dynamicRows.Add(t.gameObject);
            return t;
        }

        private ClickTarget MakeButton(Transform parent, string label, float x, float y, float w, float h,
            Action onClick, out Text labelText)
        {
            Color baseCol = new Color(0.16f, 0.17f, 0.19f);
            Image bg = MakeRectImage(parent, "Btn_" + label, x, y, w, h, baseCol);
            ClickTarget ct = RegisterClick(bg.rectTransform, onClick, -1);
            Text lbl = MakeText(bg.transform, "L", label, 18, new Color(0.9f, 0.9f, 0.88f), TextAnchor.MiddleCenter, 0f, 0f, w, h, FontStyle.Normal);
            labelText = lbl;
            if (ct != null)
            {
                ct.bg = bg;
                ct.baseColor = delegate { return baseCol; };
                ct.label = lbl;
                ct.labelColor = delegate { return new Color(0.9f, 0.9f, 0.88f); };
            }
            return ct;
        }

        /// <summary>
        /// 描边开关（只给「死亡标记」用）：外圈 2px 状态色描边 + 内层深色填充。
        /// 不用实心金底，因为实心金底在这个面板里是「键盘焦点」的专属语义，
        /// 一个不相关的开关顶着金底坐在标签页旁边，就会看成标签页渲染坏了。
        /// </summary>
        private ClickTarget MakeSwitchButton(Transform parent, string name, float x, float y, float w, float h,
            Action onClick, out Text labelText)
        {
            bool on = MarkersOn();
            Image edge = MakeRectImage(parent, name, x, y, w, h, on ? SwitchEdgeOn : SwitchEdgeOff);
            // 内层比外层小 2px，露出来的那圈就是描边
            Image fill = MakeRectImage(edge.transform, "Fill", 2f, 2f, w - 4f, h - 4f, SwitchFillIdle);
            // 命中区用外层，这样鼠标压到描边上也算压到按钮，焦点框也照着整块走
            ClickTarget ct = RegisterClick(edge.rectTransform, onClick, -1);
            Text lbl = MakeText(fill.transform, "L", MarkerButtonText(), 18,
                on ? SwitchTextOn : SwitchTextOff, TextAnchor.MiddleCenter, 0f, 0f, w - 4f, h - 4f, FontStyle.Normal);
            labelText = lbl;
            if (ct != null)
            {
                ct.bg = fill;
                ct.edge = edge;
                ct.baseColor = delegate { return SwitchFillIdle; };
                ct.edgeColor = delegate { return MarkersOn() ? SwitchEdgeOn : SwitchEdgeOff; };
                ct.label = lbl;
                ct.labelColor = delegate { return MarkersOn() ? SwitchTextOn : SwitchTextOff; };
            }
            return ct;
        }

        private bool MarkersOn()
        {
            return cfgShowMarkers == null || cfgShowMarkers.Value;
        }

        /// <summary>死亡标记开关上显示的文字</summary>
        private string MarkerButtonText()
        {
            return MarkersOn() ? "死亡标记 · 开" : "死亡标记 · 关";
        }

        /// <summary>点一下切换死亡标记，值会写进配置文件，下次进游戏保持</summary>
        private void ToggleMarkers()
        {
            if (cfgShowMarkers == null) return;
            cfgShowMarkers.Value = !cfgShowMarkers.Value;
            markerScene = null;               // 强制下一帧重建（关掉则清空）
            if (markerToggleLabel != null) markerToggleLabel.text = MarkerButtonText();
            if (L != null) L.LogInfo("[死亡统计] 死亡标记 " + (cfgShowMarkers.Value ? "已开启" : "已关闭"));
        }

        /// <summary>槽位切换回调（闭包捕获槽位号）</summary>
        private Action MakeSlotSwitch(int slot)
        {
            return delegate { viewSlot = slot; ResetScroll(); uiDirty = true; };
        }

        /// <summary>切「↑↓ 滚哪一栏」。只换目标，不动数据和焦点</summary>
        private Action MakeColSwitch(int col)
        {
            return delegate
            {
                if (col < 0 || col > 1) return;
                if (activeCol == col) return;
                activeCol = col;
                uiDirty = true;
            };
        }

        /// <summary>登记一个可点击区域（不依赖 EventSystem）；tabIndex >= 0 表示槽位标签页</summary>
        private ClickTarget RegisterClick(RectTransform rt, Action action, int tabIndex)
        {
            return RegisterClick(rt, action, tabIndex, -1);
        }

        /// <summary>colIndex >= 0 表示这是排行栏：焦点移上去就把 ↑↓ 的滚动目标切到这一栏</summary>
        private ClickTarget RegisterClick(RectTransform rt, Action action, int tabIndex, int colIndex)
        {
            if (rt == null || action == null) return null;
            ClickTarget ct = new ClickTarget();
            ct.rt = rt;
            ct.action = action;
            ct.tabIndex = tabIndex;
            ct.colIndex = colIndex;
            clickTargets.Add(ct);
            return ct;
        }

        /// <summary>自己处理点击：鼠标左键按下时，找最上层命中的可点击区域</summary>
        private void HandleClicks()
        {
            if (!uiVisible || clickTargets.Count == 0) return;
            if (!Input.GetMouseButtonDown(0)) return;

            Vector2 mp = cursorPos;
            for (int i = clickTargets.Count - 1; i >= 0; i--)
            {
                ClickTarget t = clickTargets[i];
                if (t.rt == null || !t.rt.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(t.rt, mp, null)) continue;

                try { t.action(); }
                catch (Exception e) { if (L != null) L.LogWarning("[死亡统计] 按钮回调异常: " + e.Message); }

                // 鼠标点哪儿，键盘焦点就跟到哪儿，两种操作不会打架
                for (int k = 0; k < focusItems.Count; k++)
                {
                    if (focusItems[k] != t) continue;
                    focusIndex = k;
                    if (t.colIndex >= 0) { focusRow = 1; activeCol = t.colIndex; }
                    else { focusRow = 0; topFocusMemory = k; }
                    uiDirty = true;
                    break;
                }
                return;   // 只响应最上面那一个
            }
        }

        private void DumpToLog()
        {
            L.LogInfo("[死亡统计] ===== 导出开始 =====");
            foreach (KeyValuePair<int, SlotStats> kv in slots)
            {
                SlotStats st = kv.Value;
                L.LogInfo("[死亡统计] 槽位" + kv.Key + " 共 " + st.Records.Count + " 次");

                List<KeyValuePair<string, int>> byKiller = TopN(st.ByKiller, 999);
                L.LogInfo("[死亡统计] -- 按死因 (" + byKiller.Count + " 种) --");
                foreach (KeyValuePair<string, int> e in byKiller)
                    L.LogInfo("[死亡统计]   " + e.Key + " = " + e.Value);

                List<KeyValuePair<string, int>> byScene = TopN(st.ByScene, 999);
                L.LogInfo("[死亡统计] -- 按场景 (" + byScene.Count + " 个) --");
                foreach (KeyValuePair<string, int> e in byScene)
                    L.LogInfo("[死亡统计]   " + SceneCnName(e.Key) + "（" + e.Key + "） = " + e.Value);
            }
            L.LogInfo("[死亡统计] ===== 导出结束 =====");
        }

        /// <summary>
        /// 往当前视图的槽位灌一批假记录，用来测滚动、排版和占比。
        /// 走的是和真实统计同一条 SlotStats.Add，只是数据是编的，方便随时清掉重来。
        /// </summary>
        private void InjectTestData()
        {
            int slot = viewSlot != 0 ? viewSlot : CurrentSlot();
            if (slot == 0) slot = 1;

            string[] scenes =
            {
                "Forgotten_Crossroads", "Crossroads_04", "Greenpath", "Fungal_Wastes", "Mantis_Lords",
                "City_of_Tears", "Ruins1_05", "Ruins2_08", "Soul_Sanctum", "Crystal_Peak",
                "Resting_Grounds", "Deepnest_01b", "Deepnest_East_02", "Fungal_Core", "Queens_Gardens",
                "Ancient_Basin", "Abyss_05", "Kingdom_Edge", "Fog_Canyon", "Hive",
                "Waterways", "White_Palace_03_hub", "White_Palace_06", "GG_Atrium", "Town",
                "Room_Sly", "Grimm_Divine", "Elderhu", "Town_House", "Crossroads_46"
            };

            string[] enemyKillers =
            {
                "Husk Hornhead", "Vengefly", "Mantis Warrior", "Primal Aspid", "Soul Twister",
                "Crystal Hunter", "Hive Soldier", "Stalking Devout", "Great Hopper", "Watcher Knight",
                "White Palace Fly", "Dung Defender", "Brooding Mawlek", "Sibling"
            };

            string[] trapKillers = { "地形/陷阱", "酸水", "岩浆", "深渊", "电锯" };

            SlotStats st = GetSlot(slot, true);
            System.Random rnd = new System.Random();
            const int addCount = 60;

            // 注入的数据也带坐标：前 8 条直接落在当前房间里，点完就能看到图钉，
            // 不用真去死一趟才知道效果。
            Vector3 basePos = Vector3.zero;
            bool hasBase = false;
            object heroObj = GetHero();
            Component heroComp = heroObj as Component;
            if (heroComp != null) { basePos = heroComp.transform.position; hasBase = true; }
            string nowScene = CurrentScene();

            for (int i = 0; i < addCount; i++)
            {
                string killer;
                string detail;
                int kind = rnd.Next(10);
                if (kind < 6) { killer = enemyKillers[rnd.Next(enemyKillers.Length)]; detail = "敌人"; }
                else if (kind < 9) { killer = trapKillers[rnd.Next(trapKillers.Length)]; detail = "陷阱"; }
                else { killer = "掉落/出界"; detail = "非伤害致死"; }

                string sceneName = scenes[rnd.Next(scenes.Length)];
                float ox = (float)(rnd.NextDouble() * 18.0 - 9.0);
                float oy = (float)(rnd.NextDouble() * 10.0 - 5.0);
                if (hasBase && i < 8)
                {
                    sceneName = nowScene;
                    ox = (float)(rnd.NextDouble() * 10.0 - 5.0);
                    oy = (float)(rnd.NextDouble() * 6.0 - 3.0);
                }

                st.Add(new DeathRecord
                {
                    Time = DateTime.Now.AddMinutes(-rnd.Next(1, 6000))
                        .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Scene = sceneName,
                    Killer = killer,
                    Detail = detail,
                    X = basePos.x + ox,
                    Y = basePos.y + oy,
                    Z = basePos.z,
                    HasPos = hasBase
                });
            }

            SaveSlot(slot);
            ResetScroll();
            uiDirty = true;
            markerScene = null;      // 立刻重建，本房间会冒出 8 个测试图钉

            L.LogInfo("[死亡统计] 已向槽位 " + slot + " 注入 " + addCount + " 条测试记录（该槽现有 "
                + st.Records.Count + " 条）");
        }

        private static void MergeInto(Dictionary<string, int> target, Dictionary<string, int> src)
        {
            foreach (KeyValuePair<string, int> kv in src)
            {
                int v;
                target.TryGetValue(kv.Key, out v);
                target[kv.Key] = v + kv.Value;
            }
        }

        private static List<KeyValuePair<string, int>> TopN(Dictionary<string, int> dict, int n)
        {
            List<KeyValuePair<string, int>> all = new List<KeyValuePair<string, int>>(dict);
            all.Sort(delegate (KeyValuePair<string, int> a, KeyValuePair<string, int> b)
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : string.Compare(a.Key, b.Key, StringComparison.Ordinal);
            });
            if (all.Count > n) all.RemoveRange(n, all.Count - n);
            return all;
        }

        // ================= 反射工具 =================

        private static string ParamSig(MethodInfo m)
        {
            ParameterInfo[] ps = m.GetParameters();
            string[] parts = new string[ps.Length];
            for (int i = 0; i < ps.Length; i++) parts[i] = ps[i].ParameterType.Name;
            return string.Join(", ", parts);
        }

        private static readonly Dictionary<string, Type> sTypeCache = new Dictionary<string, Type>();

        /// <summary>
        /// 按名字找游戏类型。先按完整名找，再按短名兜底——
        /// 游戏的 HazardType 在 GlobalEnums 命名空间下，只按完整名找会漏。
        /// </summary>
        private static Type FindType(string name)
        {
            Type cached;
            if (sTypeCache.TryGetValue(name, out cached)) return cached;

            Type found = null;
            Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();

            foreach (Assembly asm in asms)
            {
                try
                {
                    found = asm.GetType(name, false);
                    if (found != null) break;
                }
                catch { }
            }

            if (found == null)
            {
                foreach (Assembly asm in asms)
                {
                    Type[] all;
                    try { all = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { all = e.Types; }
                    catch { continue; }
                    if (all == null) continue;

                    foreach (Type t in all)
                    {
                        if (t == null) continue;
                        if (t.Name == name) { found = t; break; }
                    }
                    if (found != null) break;
                }
            }

            if (found != null) sTypeCache[name] = found;
            return found;
        }

        private static MethodInfo FindMethod(Type t, string name)
        {
            foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                if (m.Name == name) return m;
            return null;
        }

        private static Component FindComponent(GameObject go, Type componentType)
        {
            try
            {
                MethodInfo m = typeof(GameObject).GetMethod("GetComponentInParent", new Type[] { typeof(Type) });
                if (m != null) return m.Invoke(go, new object[] { componentType }) as Component;
                m = typeof(GameObject).GetMethod("GetComponent", new Type[] { typeof(Type) });
                if (m != null) return m.Invoke(go, new object[] { componentType }) as Component;
            }
            catch { }
            return null;
        }

        private static object GetMember(object obj, params string[] names)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            foreach (string n in names)
            {
                FieldInfo f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { try { return f.GetValue(obj); } catch { } }
                PropertyInfo p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) { try { return p.GetValue(obj, null); } catch { } }
            }
            return null;
        }

        private static object GetStaticMember(Type t, params string[] names)
        {
            foreach (string n in names)
            {
                PropertyInfo p = t.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (p != null) { try { return p.GetValue(null, null); } catch { } }
                FieldInfo f = t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) { try { return f.GetValue(null); } catch { } }
            }
            return null;
        }
    }

    // ================= 数据结构 =================

    internal class DeathRecord
    {
        public string Time;
        public string Scene;
        public string Killer;
        public string Detail;
        // 死亡坐标（场景内世界坐标）。老存档没有这两列，HasPos 就是 false，
        // 只影响能不能在那个房间画出图钉，不影响任何统计。
        public float X;
        public float Y;
        public float Z;
        public bool HasPos;
        // 落点算法版本：2 = 按「身体从下往上 1/3」算（v0.9.13 起）；
        // 0/1 = 老算法，当时直接存 transform.position，图钉会贴在头顶。
        // 显示时对老记录做一次等量下移，不必清空重记。
        public int AnchorVer;
    }

    internal class SlotStats
    {
        public readonly int Slot;
        public readonly List<DeathRecord> Records = new List<DeathRecord>();
        public readonly Dictionary<string, int> ByKiller = new Dictionary<string, int>();
        public readonly Dictionary<string, int> ByScene = new Dictionary<string, int>();
        public float MaxPlayTime;

        public SlotStats(int slot) { Slot = slot; }

        public void Add(DeathRecord r)
        {
            Records.Add(r);
            Bump(ByKiller, r.Killer);
            Bump(ByScene, r.Scene);
        }

        public void Clear()
        {
            Records.Clear();
            ByKiller.Clear();
            ByScene.Clear();
            MaxPlayTime = 0f;
        }

        private static void Bump(Dictionary<string, int> dict, string key)
        {
            int v;
            dict.TryGetValue(key, out v);
            dict[key] = v + 1;
        }
    }
}
