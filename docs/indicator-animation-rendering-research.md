# 指示器出现动画与 WPF 合成时序研究

更新时间：2026-09-12

本文只研究 InputCue 指示器窗口的显示、移动和翻转动画时序。资料范围限定为 Microsoft Learn、Windows 官方文档，以及 `dotnet/wpf` 官方源码；不把社区经验当作框架保证。

证据等级：

- **A：公开 API 或平台文档明确保证的行为**；
- **A-src：`dotnet/wpf` 官方源码所显示的当前实现**，可解释行为，但不是永久不变的公共 API 合约；
- **B：本仓库当前实现的事实**；
- **C：从 A/A-src/B 推导出的工程建议**，需要本项目测试验证。

## 结论摘要

当前闪烁不能再靠“多延迟一次”或“多设一次 `Opacity=0`”解决。WPF 的属性写入、场景图编译、合成通道提交和 DWM 实际呈现是不同阶段；把 `Opacity` 或 `ScaleX` 设为 0，只表示托管属性值已经改变，不表示用户已经看到了一帧 0。`CompositionTarget.Rendering` 也发生在实际渲染前，并不是 DWM 呈现确认。[CompositionTarget.Rendering 官方说明](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)（A）；[`MediaContext.RenderMessageHandlerCore` 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs#L1615-L1700)（A-src）。

推荐方案是把原生 HWND 生命周期与逻辑显示状态分开：窗口只 `Show()` 一次并长期保留，逻辑隐藏使用稳定的透明/收缩状态。同一编辑目标内移动插入点只更新位置；已经确认切换到另一个编辑目标时，旧位置立即不可见，新位置执行一次 `Priming -> Appearing` 的 `0 -> 1` 展开。两者必须由稳定的语义目标身份区分，不能直接使用 UIA RuntimeId 或 Generation。（C）

要得到纯粹的 `0 -> 1`，必须先取消旧动画，把基值固定在 0，并让这个 priming 状态至少经过一个完整 WPF render pass，再启动展开。即使如此，WPF 公共 API 仍不能证明 DWM 已经把该 0 帧物理显示到屏幕；因此真正的防闪基础不是“等待得更久”，而是让窗口在隐藏期本来就长期停留于透明/收缩状态，并禁止旧回调重新写入状态。（C）

## 官方行为边界

### 1. `AllowsTransparency` 是顶层 HWND 的逐像素透明合成

WPF 文档说明，每个 `Window` 最终都是 HWND；顶层透明窗口通过 layered-window/per-pixel alpha 能力与桌面上的其他 HWND 合成。WPF 还明确指出 layered window 的能力受平台和 DirectX/GDI 渲染路径影响。[Technology Regions Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/technology-regions-overview)（A）。

`AllowsTransparency=true` 在 WPF `Window` 源码中会传给 `HwndSourceParameters.UsesPerPixelOpacity`；官方 API 文档说明这个值决定顶层窗口内容的逐像素不透明度是否在最终桌面绘制阶段生效。[`Window.CreateHwndSourceParameters` 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs#L2391-L2402)（A-src）；[`HwndSourceParameters.UsesPerPixelOpacity`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.hwndsourceparameters.usesperpixelopacity?view=windowsdesktop-10.0)（A）。

WPF 不支持直接给 `Window` 本身设置任意 `RenderTransform`：`Window` 在类型元数据中对该属性进行了专门的强制处理。因此缩放动画应作用于窗口内部的根视觉或徽标视觉，而不是顶层 `Window.RenderTransform`。[`Window` 静态初始化源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs#L38-L64)（A-src）。

### 2. `Hide/Show` 是 HWND 可见性切换，不是帧屏障

`Window.Hide()` 的公开契约是让窗口不可见，并把 `Visibility` 设为 `Hidden`；它不会关闭窗口。[`Window.Hide`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.hide?view=windowsdesktop-10.0)（A）。

源码进一步显示，`Show()`/`Hide()` 最终进入 `ShowHelper`，并调用 Win32 `ShowWindow`（或特定 Topmost 情况下调用带 `SWP_SHOWWINDOW` 的 `SetWindowPos`）。因此它们会改变原生窗口的可见性/Z 序相关状态，而不是只改变一棵 WPF 视觉树中的一个像素属性。[`Window.Show`/`Hide` 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs#L134-L178)、[`ShowHelper` 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Window.cs#L4984-L5089)（A-src）。

`Show()` 同步返回只保证 `Loaded` 已触发；而设置 `Visibility=Visible` 是异步的。公开文档没有把两者中的任何一个定义成“DWM 已经呈现目标像素”的等待点。[`Window.Show`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.show?view=windowsdesktop-10.0)（A）。

所以，对一个每秒可能多次移动、出现和消失的小型 overlay，反复 `Hide/Show` 会额外引入 HWND 可见性和 Z 序转换。官方并没有说 `Hide/Show` 本身一定闪烁；“应避免反复调用”是针对本项目的工程判断，不是 WPF 的普遍禁令。（C）

### 3. `Opacity=0` 与 `ScaleX=0` 只改变渲染输入

WPF 的 `Opacity` 是依赖属性，0 表示元素完全透明；其值应用到渲染内容，但不等于一次呈现确认。[`UIElement.Opacity`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.opacity?view=windowsdesktop-10.0)（A）；[`UIElement.pushOpacity` 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs#L2435-L2467)（A-src）。

透明元素默认仍可能参与命中测试；如果 overlay 依赖 WPF 命中测试而不是纯 Win32 `WS_EX_TRANSPARENT`，逻辑隐藏时还应明确关闭命中测试。[`UIElement.IsHitTestVisible`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.ishittestvisible?view=windowsdesktop-10.0)（A）。

`RenderTransform` 不参与父容器的布局尺寸计算，官方将其定位为动画和临时效果的合适工具；性能指南还建议更新已有 `Transform`，避免反复替换整个 `RenderTransform` 而触发不必要的计算。[`UIElement.RenderTransform`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.rendertransform?view=windowsdesktop-10.0)、[WPF Layout and Design Performance](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/optimizing-performance-layout-and-design#update-rather-than-replace-a-rendertransform)（A）。

这支持继续复用一个 `ScaleTransform` 并动画 `ScaleX`，而不是动画 `Width`、重新建视觉树或替换整个 Transform。（C）

### 4. `BeginAnimation` 会引入动画值层，不能只看基值

WPF 动画系统通过动画依赖属性生成随时间变化的有效值；`BeginAnimation` 的公开文档特别说明，属性在渲染出超过非动画起点的第一帧时才被认为已经开始动画。使用 `HandoffBehavior.SnapshotAndReplace` 可以替换同属性上正在运行的动画。[WPF Animation Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/animation-overview)、[`UIElement.BeginAnimation`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.beginanimation?view=windowsdesktop-10.0)（A）。

因此存在两个不同的值：依赖属性的基值与动画计算后的有效值。若旧动画仍以 `FillBehavior.HoldEnd` 持有 1，只写 `ScaleX=0` 不足以证明当前呈现值已经是 0；必须先移除/替换旧动画，再设置基值，且所有旧 `Completed` 回调都必须失效。（C）

### 5. `CompositionTarget.Rendering` 是渲染前回调，不是呈现完成

Microsoft Learn 说明，`CompositionTarget.Rendering` 每帧调用，发生在布局完成后；回调中修改布局还会让布局在真正渲染前再计算一次。[Per-frame Rendering](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)（A）。

官方源码给出了更精确的顺序：时间系统 tick -> layout/render callbacks -> `Rendering` 事件 -> 再处理一次可能由事件引发的布局 -> `Render` 场景 -> 更新资源 -> 关闭 batch -> `CommitChannel`。源码还明确区分了“render + commit”与“已经 presented 到屏幕”。[`MediaContext.RenderMessageHandlerCore`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs#L1637-L1700)、[`MediaContext.Render`/`CommitChannel`](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs#L1825-L1989)（A-src）。

直接后果是：

- 在第一次 `Rendering` 回调里立刻启动 `0 -> 1`，WPF 允许该变化进入同一个即将提交的 render pass，不能证明 0 曾单独成帧；
- 让第一次 `Rendering` 只作为 priming 边界，并在该次 render 返回后再启动动画，至少能把“准备 0”与“展开”分到不同的 WPF render pass；
- 这仍然只证明 WPF 处理/提交顺序，不证明 DWM 已经把 0 帧显示出来。（C）

Windows 提供的 `DwmFlush` 会阻塞，直到调用方当前 outstanding 的 DirectX surface 更新在下一次 Present 时完成。[`DwmFlush`](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmflush)（A）。但 WPF 没有把它公开为自己的视觉树呈现屏障，也没有承诺在 UI 线程调用它能精确对应某个 WPF 属性提交；不应在每次指示器出现时把它当作常规修复手段。（C）

### 6. `DispatcherPriority` 只定义队列优先级

`DispatcherPriority.Render` 表示与渲染同优先级，`ContextIdle` 表示低于 Background 的一个调度级别。更重要的是，官方文档明确说当前 WPF 中不存在与 `ApplicationIdle` 或 `ContextIdle` 对应的特定系统“空闲状态”。[`DispatcherPriority`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.threading.dispatcherpriority?view=windowsdesktop-10.0)（A）。

所以 `Dispatcher.BeginInvoke(ContextIdle, ...)` 只能表达“以后按这个优先级执行”，不能表达“前一个透明帧已由 WPF commit”，更不能表达“DWM 已显示”。本仓库当前将 `ContextIdle` 用于延迟输入框切换后的 appearance，这不能作为可靠帧协议。（B/C）

## 对五个核心问题的回答

### 如何保证隐藏到出现只有 `0 -> 1`

在 WPF 能保证的范围内，推荐以下流程：（C）

1. overlay HWND 首次初始化后只 `Show()` 一次；初始化时内部视觉保持 `Opacity=0`、`ScaleX=0`，并保持非激活/鼠标穿透。
2. 收到真正的逻辑显示请求时，递增 presentation epoch，取消同属性的旧动画，并使旧完成回调因 epoch 不匹配而失效。
3. 在仍不可见的状态下原子更新内容、位置、DPI、目标 opacity；把内部视觉基值固定为 `ScaleX=0`。
4. 进入 `Priming`，至少跨过一个完整 WPF render pass。不能在第一次 `CompositionTarget.Rendering` 里直接启动动画；可以在这个回调中仅登记一个回调，让当前 render 返回后再启动，也可以等待下一次 `Rendering`。前者减少额外等待，后者更直观。
5. 再次校验 epoch、目标仍应显示且窗口仍处于 `Priming` 后，设置目标 opacity，并以 `SnapshotAndReplace` 启动唯一一条 `ScaleX: 0 -> 1` 动画。
6. 完成时只允许当前 epoch 将状态提交为 `Visible`；旧的 Dispatcher、Rendering 和 animation callback 全部 no-op。

这套流程保证状态机和 WPF render-pass 顺序中不存在 `1 -> 0 -> 1`。它不能形成 DWM 物理呈现的数学保证；避免旧完整帧的关键是隐藏期本来就稳定保持透明/收缩，而不是每次显示前临时抢救。（C）

### 为什么同窗口移动时不应重播出现动画

`RenderTransform` 只描述视觉如何渲染，窗口坐标改变是另一类状态；“可见性”和“目标位置”在模型上正交。[`RenderTransform`](https://learn.microsoft.com/en-us/dotnet/api/system.windows.uielement.rendertransform?view=windowsdesktop-10.0)（A）。

在同一 VSCode 文档里点击到另一个插入点时，用户看到的是同一个持续存在的指示器跟随光标，而不是一个指示器消失、另一个指示器诞生。因此该事件应映射成 `Visible(target A, caret 1) -> Visible(target A, caret 2)`：只更新位置，必要时更新内容；不得设置 0、不得修改窗口 opacity、不得取消后重启出现动画，也不得为了移动而重排 Z 序。（C）

两个都满足显示条件的输入框之间直接切换属于确认的目标 handoff：旧位置立即不可见，在不可见状态下更新目标与坐标，然后只在新位置播放一次 `0 -> 1` 展开。若新目标身份或锚点尚未就绪，应保留上一稳定判断，不能把探测过程中的短暂 `PositionUnknown` 或 `NoEditableFocus` 先翻译成一次普通隐藏、随后再翻译成一次无条件重现。（C）

### 属性已设为 0 是否等于一帧已提交

不等于。设值只完成托管对象/视觉资源的状态改变；WPF 随后才在 render pass 中编译场景、更新资源和 commit 通道。`CompositionTarget.Rendering` 甚至位于这些操作之前。[`UIElement` opacity 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/UIElement.cs#L2435-L2467)、[`MediaContext` 渲染顺序](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs#L1637-L1700)（A-src）。

“WPF 已 commit”也不等于“DWM 已物理显示”。官方源码有单独的 present notification/interlock 状态，正说明 commit 与 present 是不同阶段。[`MediaContext` interlock 状态源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs#L2491-L2602)（A-src）。

### 是否应避免 `Hide/Show`

应在这个 overlay 的高频运行路径中避免，但不是因为 WPF 宣称 `Hide/Show` 有缺陷。理由是：（C）

- 它们改变原生 HWND 的 visible 状态，并可能走 `ShowWindow`/`SetWindowPos`；
- 当前需求只需要“像素不可见”，不需要销毁或移除 HWND；
- 保持 HWND 存活能让 Z 序、Owner/no-activate、透明合成 surface 和动画时钟的生命周期更单一；
- 隐藏状态长期为透明/收缩，下一次出现就没有一个临时的旧完整 surface 需要先被覆盖。

仍可在应用退出、显示器会话重建或不可恢复的 HWND 生命周期事件中真正 Hide/Close；它不应成为每一次 caret/输入框切换的呈现动作。（C）

### 可靠的状态机边界是什么

建议只保留以下呈现状态：（C）

| 状态 | 不变量 | 允许进入方式 |
|---|---|---|
| `Hidden` | HWND 已创建且保持显示；内部视觉 `Opacity=0`、`ScaleX=0`；无运行中动画 | 明确、稳定的 hide policy |
| `Priming` | 最新内容与位置已写入；旧回调失效；`ScaleX=0`；仍不可见 | `Hidden -> visible request`，或已确认的不同目标 handoff |
| `Appearing` | 仅一条当前 epoch 的 `0 -> 1` 展开动画 | 仅 priming render pass 之后 |
| `Visible` | 目标 opacity；`ScaleX=1`；无 appearance 回调 | appearance 完成或动画关闭 |
| `Switching` | 完整 `1 -> 0 -> 1` 只用于已显示时真正的输入状态/图形切换 | 仅 `Visible` 中内容语义切换 |

事件规则：（C）

| 输入事件 | 动作 |
|---|---|
| `Visible` 中 caret/窗口位置变化 | 只 move；保持 opacity、scale、动画状态和 Z 序 |
| `Visible` 中切到另一个已确认的可显示输入框 | 旧位置立即透明；原子更新 target + move；新位置只播一次 `0 -> 1` appearance |
| `Priming/Appearing` 中位置更新 | 更新目标坐标；不重启 epoch/appearance |
| `Priming/Appearing` 中输入状态修正 | 更新将呈现的内容；继续当前 `0 -> 1`，不先收缩 |
| `Visible` 中中文/英文等真实状态变化 | 播完整 flip，且用 epoch 取消旧 flip |
| 明确隐藏（输入中、焦点离开、设置关闭） | epoch++，取消全部动画/回调，进入稳定 Hidden |
| 过期探测结果或过期回调 | no-op |

渲染层应接收一次性的“期望呈现快照”，而不是对焦点探测、锚点探测、语言探测的每一个中间结果分别执行副作用。一个 Dispatcher 周期内的多个观察应按版本取最新值；只有稳定策略明确要求隐藏时才发出 `Hide`。这能把“短暂未知”与“用户确实离开输入环境”分开。（C）

## 测试策略

### 1. 纯状态机测试

将 reducer/transition policy 与 WPF `Window` 分开测试，输入离散事件，记录：

`epoch / state / target / visible / opacity / scaleX / position / animation command / z-order command`

必须覆盖：（C）

- `Hidden -> visible` 只能产生一次 `Prime` 和一次 `Expand(0,1)`；
- VSCode 同一文档连续 100 次不同 caret 坐标，只产生 Move；
- 两个输入框连续切换 100 次，每次只产生一次旧位置隐藏、一次 Prime 和一次 `0 -> 1` appearance；
- `Priming` 和 `Appearing` 期间 target/position/state correction 不重启动画；
- 任意过期 epoch 的 Dispatcher、Rendering、animation-completed 回调不改变状态；
- `Visible -> transient unknown -> visible` 在 handoff 宽限/同批合并后不落入 Hidden；
- 真正 hide 后快速 show，只有最新 epoch 能启动展开。

关键不变量是：同一语义目标内永远不能出现 `Opacity: target -> 0 -> target` 或重启 appearance；只有已确认的不同目标 handoff 才允许旧位置立即隐藏，并且新位置只能出现单调的 `ScaleX: 0 -> 1`。（C）

### 2. WPF STA 窗口测试

STA 测试用于验证动画层和回调版本控制：（C）

- 取消动画后读取基值与有效值，确认旧 `HoldEnd` 不再控制属性；
- 验证第一次 Rendering 回调不会启动展开；
- 验证 render-pass 后只有当前 epoch 创建一条展开动画；
- 位置更新期间 opacity/scale 的有效值不改变；
- 关闭窗口或测试结束时总是退订静态 `CompositionTarget.Rendering`，避免跨测试泄漏。

这类测试只能证明 WPF 对象状态和回调顺序，不能证明桌面实际像素不闪，因为 `Rendering` 是渲染前事件。（A/C）

### 3. 实际桌面逐帧测试

必须增加 Windows 实机集成测试或人工录像回放，才能覆盖 HWND、layered window 与 DWM：（C）

- 在 60 Hz 和可用的高刷新率环境下，以至少显示刷新率的 2 倍捕获 overlay 区域；
- 执行 VSCode 同文档 100 次点击换 caret、同应用两个输入框 100 次 handoff、显示中立刻换目标、隐藏后快速重新显示；
- 对每帧计算非透明/非背景像素的水平包围盒宽度，appearance 期间宽度必须单调不减；
- 同目标 caret move 不允许出现全透明帧；跨目标 handoff 允许旧位置立即透明，但新位置宽度只能从零单调增加；
- 同时保存状态机结构化事件日志，以区分“逻辑错误发出了 Hide”与“正确命令在 DWM 侧呈现异常”。

`RenderTargetBitmap` 等离屏 WPF 测试可验证视觉内容，却绕过顶层 HWND 与桌面合成，不能替代上述桌面逐帧验证。（C）

## 对当前实现的直接判断

当前 `IndicatorOverlayWindow` 已经尝试长期保留窗口、用 opacity 逻辑隐藏，并用 transition version 取消旧动画，这是正确方向。（B）

### 根因分层

**第一层：overlay 明确制造了 `Visible -> transparent -> appearance`。** [`IndicatorOverlayWindow.Render`](../src/InputCue.Overlay/IndicatorOverlayWindow.xaml.cs#L129-L152) 在 `contextActivated && 当前已显示 && positionedGeneration != state.Generation` 时进入 `BeginDeferredContextAppearance`；后者先取消动画、把 `ScaleX=0`、`Opacity=0`，再通过 `DispatcherPriority.ContextIdle` 恢复 opacity 并播放 `0 -> 1`。[`BeginDeferredContextAppearance`](../src/InputCue.Overlay/IndicatorOverlayWindow.xaml.cs#L284-L330)（B）。这条路径与用户看到的“先出现一个，突然消失，再展开”完全同形；它不是偶发 DWM 噪声，而是当前逻辑允许并主动产生的状态序列。（C）

**第二层：旧测试没有区分“同目标 caret move”和“不同目标 handoff”。** 原测试只凭 Generation 和点击激活就要求立即 `Opacity=0`，因此会把 VSCode 同一编辑器内的身份抖动误当成输入框切换。测试应改为显式提供稳定的目标切换判定：同目标即使 Generation 修正也全程可见；不同目标则验证旧位置立即透明、新位置只执行一次单调展开。（C）

**第三层：Core 会把瞬时不可编辑观察立即转成 Hide。** [`IndicatorSession.ObserveCore`](../src/InputCue.Core/Indicator/IndicatorSession.cs#L82-L117) 对任何 `GetHiddenReason` 非空的 snapshot 立即调用 `Hide`；`NoEditableFocus` 会落入 `ContextIneligible`。[`GetHiddenReason`](../src/InputCue.Core/Indicator/IndicatorSession.cs#L216-L238)（B）。`suppressContextReplay` 又只在 `Transient` 模式参与抑制；`IdlePersistent` 中重新成为 eligible 时仍按 context established/activation 重新 Show。[旧版 `IndicatorSession` replay 分支](../src/InputCue.Core/Indicator/IndicatorSession.cs#L100-L119)（B）。因此一次点击过程中若 Provider 短暂报告 `NoEditableFocus`，当前系统会真实地产生 `Visible -> Hidden -> Visible`，overlay 再忠实播放一次 appearance。（C）

**第四层：Generation 不是稳定的“用户语义输入上下文”。** [`WindowsObservationIdentity.AutomationIdentity`](../src/InputCue.Windows/InputContext/WindowsObservationIdentity.cs#L43-L51) 对 UIA `AutomationElement.GetRuntimeId()` 求哈希；[`RawInputContextObservation.Fingerprint`](../src/InputCue.Windows/InputContext/RawInputContextObservation.cs#L32-L43) 把这个哈希纳入 fingerprint；[`InputContextEngine`](../src/InputCue.Windows/InputContext/InputContextEngine.cs#L123-L168) 在 fingerprint 变化时递增 Generation（B）。Chromium/VSCode Provider 若在同一编辑器内重建或替换 AutomationElement，语义上只是移动 caret，却会成为新 Generation。于是“Generation 变化 -> 新输入框 -> appearance”这个等式本身不成立。（C）

**第五层：`ContextIdle` 只是在错误状态序列中改变等待位置。** 即便它偶尔让透明状态进入一个 WPF render pass，也无法修复第一至第四层制造的 `Hide/Show` 语义往返；而且如前文所述，它不是 commit/present 契约。（A/B/C）

### 应删除或重写的补丁

以下不是继续保留再加条件的兼容路径，而应从新状态机中移除：（C）

- 删除 `contextActivated && generation changed -> BeginDeferredContextAppearance` 这条不稳定触发规则；
- 删除 `DeferredContextAppearance` 及其 `ContextIdle` 伪帧屏障，改由显式的初次出现或确认目标 handoff 进入 `Priming`；
- 禁止 `positionedGeneration`、anchor 变化或 pointer activation 单独决定 appearance；必须先经过语义目标连续性判断；
- 重写 `IdlePersistent` 的短暂 `NoEditableFocus` 处理：观察层先合并/稳定 handoff，或 session 维持上一可见呈现直到获得新 eligible target/明确稳定 hide，避免把探测中间态暴露给渲染层；
- 将 Generation 降为并发版本号，而非 appearance 的产品语义。是否出现只取决于上一“已提交的语义显示状态”是否为 Hidden。

因此下一次实现不应继续给 `BeginDeferredContextAppearance` 增加额外条件或额外 Dispatcher 延迟。应把“是否真正从 Hidden 进入可见”作为唯一 appearance 判据，并用一套 epoch 驱动的状态机统一 Render、Hide、position、content correction 和 animation completion。（C）

## 实施优先级

1. 先写目标连续性与序列回归，明确同目标 caret move 永不 appearance；不同目标 handoff 只 appearance 一次。
2. 移除 context/generation 对 appearance 的直接触发；generation 只用于丢弃过期异步结果。
3. 重写 `IdlePersistent` 的 target handoff：短暂 `NoEditableFocus` 不得穿透成一次用户可见 Hide；真正离开编辑环境仍必须及时隐藏。
4. 保持单一 HWND，逻辑隐藏归一成稳定的透明/收缩状态。
5. 用明确 priming render pass 替代 `ContextIdle` 伪屏障。
6. 重写现有透明帧测试，并增加 Generation 波动、瞬时 `NoEditableFocus` 与双输入框 handoff 的联合矩阵。
7. 最后做桌面逐帧采集；若状态日志正确但仍有物理闪烁，再评估是否禁用 appearance 或迁移到更接近 Windows Composition 的实现。

这套顺序先消除确定存在的逻辑往返，再处理框架与 DWM 的帧时序，能避免继续把状态机错误误判为“动画太快”或“DWM 缓存”。
