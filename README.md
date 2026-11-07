# 深空电视 · Probably Stolen Mod

把电脑上任意窗口的画面，投到《Probably Stolen》店里那台电视上播放。

**这是屏幕捕获，不是视频文件。** mod 用 Windows 自带的 Windows Graphics Capture 在游戏进程内直接抓窗口像素，再贴到电视的 RawImage 上。不需要 OBS，不启动任何外部 exe，任务管理器里不会多出进程。

- 创意工坊条目：[3835181563](https://steamcommunity.com/sharedfiles/filedetails/?id=3835181563)
- 游戏：Probably Stolen（Steam AppID `4348910`，Unity 2021.3.45f2 / Mono）
- 目标框架：.NET Framework 4.7.2（mod 被注入到游戏的 Mono 运行时里）
- 许可证：MIT

> 文件夹名、DLL 名、mod ID 一直是 `WindowTV` / `window_tv`，这些是存档和信任记录认的标识。改名会让已经买过的电视从存档里消失、权限要重新批。**显示名**（mod 列表、物品名、窗口标题、送货员台词）才叫「深空电视」。

## 功能

| 功能 | 说明 |
|---|---|
| 投屏 | 选一个窗口，实时抓帧贴到电视上，分辨率跟着目标窗口走 |
| 尺寸 | 右列上面的旋钮：小 / 中 / 大 / 特大，四档循环 |
| 换窗口 | 右列下面的旋钮：在可投窗口之间循环切换 |
| 挂锁 | 扣上以后 ESC 不会把电视收走，其他界面和游戏菜单照常响应 |
| 设置 | 右下拨杆：扫描线、雪花噪点、开机亮线、音效，四个开关 |
| 待机画面 | 没选窗口时是七色彩条测试卡，窗口掉线是雪花 + 「信号中断」，恢复后自动接回 |
| 送货 | 电视不是刷出来的：开门后第一个进店的客户就是送货员，聊完就放前台柜面上 |

所有贴图（旋钮、键帽、挂锁、彩条、雪花、扫描线）都是运行时逐像素生成的，仓库里只有物品图标的 48×48 png。

## 安装

**创意工坊**：订阅 → 游戏内 Mod 管理里启用并信任（它申请了 `native` 能力，因为要调系统捕获接口）→ 重启游戏。

**手动**：把 `WindowTV.dll`、`manifest.xml`、`Textures\` 放进

```
<游戏目录>\Mods\WindowTV\
├─ WindowTV.dll
├─ manifest.xml
└─ Textures\Items\television.png
```

然后同样要在 Mod 管理里信任一次。改过 DLL 之后信任记录会失效，需要再信任。

## 构建

```bash
dotnet build -c Release -p:GameManaged="D:\steam\steamapps\common\Probably Stolen\Probably Stolen_Data\Managed"
```

`GameManaged` 不传的话会按 Steam 默认安装路径猜。输出为 `bin\Release\WindowTV.dll`，连同 `manifest.xml`、`Textures\` 一起丢到 `Mods\WindowTV\` 即可。

依赖只有游戏自带的程序集（`Assembly-CSharp`、`UnityEngine.*`、`Unity.TextMeshPro`、`0Harmony`），全部 `Private=false`，不会被打包进 DLL。

## 限制

这些是机制决定的，不是 bug：

- **目标窗口不能最小化**：最小化后系统停止出帧。被别的窗口挡住没关系，游戏可以全屏。
- **受 DRM 保护的播放是黑屏**（Netflix 一类），系统层面拦掉。
- **最小化窗口、以及标了「禁止捕获」的窗口**（远程串流客户端、防录屏播放器）抓不到，列表里直接排除。
- **不投游戏自己**，列表里已经排除。
- **按目标窗口原始分辨率取帧，不缩放**：WGC 的帧池比捕获项小的时候系统会裁掉右下角而不是缩放，所以不能为省性能压输出尺寸。
- 需要 Windows 10 1903+，要能创建 D3D11 设备。

## 实现要点

```
捕获线程（后台，MTA）
  Windows.Graphics.Capture 出帧
  → D3D11 staging 纹理 → Map 直读 BGRA
  → 交换到最新帧槽位
Unity 主线程
  → Texture2D.LoadRawTextureData + Apply
  → RawImage（uvRect 做上下翻转）
```

几个踩过的坑，改代码前值得看一眼：

- 全部手写 `[ComImport]` / vtable 调用，**不依赖 CsWinRT、Vortice、SharpDX**。槽位顺序以 `WinMetadata\Windows.Graphics.winmd` 为准——投影接口里属性的书写顺序**不等于**真实 vtable 顺序，照抄会错位。
- `IDirect3DSurface` 是 WinRT 接口，要先 `QueryInterface` 到 `IDirect3DDxgiInterfaceAccess`，再调经典 COM 槽位 3。
- 后台线程必须先 `CoInitializeEx(COINIT_MULTITHREADED)`，否则 `RoGetActivationFactory` 返回 `CO_E_NOTINITIALIZED`。
- 隐藏鼠标指针：`QueryInterface` 到 `IGraphicsCaptureSession2` 调 `put_IsCursorCaptureEnabled`（槽位 7），WinRT 的 `boolean` 是单字节，委托参数要用 `byte`。IID 从 `Microsoft.Windows.SDK.NET.dll` 的 `GuidAttribute` 里读，别凭记忆写。
- **绝不要用 `System.Diagnostics.Process`**：官方 `ModScanner` 把它归为 `process-start` 硬红线，命中直接拒绝加载整个 mod。需要进程信息走 `kernel32!OpenProcess` + `QueryFullProcessImageNameW`，属于 `native` 能力。

## 关于 ESC 与挂锁

只把 `window.closeOnEscape` 设成 `false` 不够。电视窗口挂在 `CanvasPaperUI` 下，而 ESC 走 `StoreUIManager.CloseTopmostEscapeTarget` 时会把这一层的面板直接 `SetActive(false)`——窗口不是被「关闭」，是被「收走」了。

`TvEscapeDodge.cs` 的做法是挂 `CloseTopmostEscapeTarget` 的 prefix / postfix：锁着的时候先把自己藏起来，方法跑完立刻放回去。两次调用在同一个栈里，渲染在帧末，画面上看不出闪烁。它**不改返回值、不碰其他目标**，所以机器面板、纸片、游戏暂停菜单全都照常响应。

## 项目结构

| 文件 | 职责 |
|---|---|
| `ModEntry.cs` | mod 入口、钩子注册、调试命令 |
| `TvItem.cs` / `TvCourier.cs` / `TvRuntime.cs` | 物品定义、送货员、送到柜面的兜底逻辑 |
| `TvWindow.cs` | 主窗口：布局、旋钮、状态机、掉线重连 |
| `TvScreenFx.cs` | 彩条 / 雪花 / 扫描线 / 暗角 / 开机动画 / OSD |
| `TvKnob.cs` / `TvPanelArt.cs` / `TvLockArt.cs` | 程序化生成的旋钮、键帽、挂锁贴图 |
| `TvAudio.cs` | 现场合成音效（`AudioClip.Create` + `SetData`） |
| `TvSettings.cs` / `TvOptions.cs` | 设置页与配置读写 |
| `TvEscapeDodge.cs` | 挂锁的 ESC 避让 |
| `Capture/` | WGC 互操作、捕获宿主、窗口枚举 |
| `art/draw_tv_icon.py` | 物品图标生成脚本（纯 Pillow，改完跑一遍重新出图） |

## 调试命令

游戏调试控制台里：

| 命令 | 作用 |
|---|---|
| `深空电视: 拿一台` | 直接给一台电视 |
| `深空电视: 清掉所有` | 清空店里所有电视 |
| `深空电视: 捕获自检` | 只枚举可投窗口并写日志，不启动捕获 |

出问题看 `Player.log`（`%USERPROFILE%\AppData\LocalLow\Questing Goose Studio\Probably Stolen\`），搜 `[WindowTV]`。

## 许可证

MIT，见 [LICENSE](LICENSE)。随意修改、二次分发、打包进整合包都行，保留版权声明即可。
