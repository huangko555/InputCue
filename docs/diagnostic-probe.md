# 诊断探针使用与验收

## 用途

当前程序不会显示输入法状态提示，也不会修改输入法。它只观察外部前台应用的焦点、可编辑语义、选区和 Caret，帮助我们在实现提示层前确认判断是否可靠。

探针刻意忽略 InputCue 自己。切换到目标应用进行操作，再返回 InputCue，即可查看最后一条外部观察。

## 运行

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj
```

窗口中的主要字段：

- `Eligibility`：最终语义分类；
- `InputState`：输入状态；稳定的非 IME 布局标为 `English`，中文 IME 仅在开关状态和转换模式证据一致时标为 `Chinese`/`English`，其余为 `Unknown`；
- `InputLanguage`：前台输入线程 HKL 的语言 ID，仅用于兼容诊断；
- `IsIME / HasIMEContext / IMEOpen / ConversionMode`：IMM32 实验事实；只有中文语言布局且证据一致时才参与状态归类；
- `HasDefaultIMEWnd / WindowOpenStatus / WindowConvMode`：默认 IME 窗口消息路径的实验事实；成功返回的 `0` 与查询失败严格区分；
- `EvidenceGrade`：证据是已确认、已降级还是未知；
- `ReasonCode`：为什么显示、隐藏或降级；
- `IsReadOnly`：目标是否只读；
- `HasSelection`：是否存在非折叠选区；
- `UIA/Win32/MSAA Caret`：三条坐标来源的对照；
- `Generation`：焦点元素或选区变化后应推进；
- `Duration`：单次跨进程观察耗时。

## 五个黄金场景

### 1. 浏览器输入框

1. 在 Edge 或 Chrome 打开普通网页输入框；
2. 点击输入框并输入几个字符；
3. 返回 InputCue。

期望：`EditableCaret`。如果可编辑已确认但所有 Caret 来源都不可用，可以暂时是 `PositionUnknown`，但必须记录下来，不能用鼠标位置伪装成功。

### 1.1 可编辑输入框全选

1. 在地址栏或普通输入框中按 `Ctrl+A`；
2. 切换一次中英文输入状态；
3. 返回 InputCue。

期望：保持 `EditableSelection`。有可信 UIA、Win32 或 MSAA Caret 时 Anchor 非空并允许提示；没有可信锚点时 Anchor 为空并保持隐藏。

### 2. 静态网页拖选

1. 在同一网页拖选一段只读正文；
2. 松开鼠标；
3. 返回 InputCue。

期望：`ReadOnlySelection`，`HasSelection=true`，Anchor 为空。

### 3. 对话框标签或按钮悬停

1. 打开一个原生 Windows 对话框；
2. 只把鼠标移动到标签、说明文字或按钮上，不点击编辑框；
3. 返回 InputCue。

期望：鼠标移动本身不会建立新的可编辑上下文。结果应维持原焦点语义或为 `NoEditableFocus`，不能因为 I 型鼠标指针变成 `EditableCaret`。

### 4. 对话框编辑框

1. 点击对话框中的文本编辑框；
2. 返回 InputCue。

期望：`EditableCaret`，或在坐标缺失时安全降级为 `PositionUnknown`。

### 5. 快速切换焦点

1. 在两个应用或两个编辑框之间快速切换；
2. 返回 InputCue，观察 `Generation`；
3. 重复数次。

期望：焦点元素变化后 Generation 递增；最终结果属于最后一个目标，不出现旧目标进程或控件信息。

## 导出与隐私

“导出脱敏 JSON”只在用户主动选择文件后写入，最多包含最近 200 条观察。文件带有独立的 `SchemaVersion` 和导出时间，可由 `InputContextTraceReplay` 重新执行当前分类器，以发现算法修改造成的语义漂移。允许的字段包括进程基名、进程 ID、UI 框架、控件类型、控件类名、布尔语义、坐标、原因码和耗时。

不会记录：

- 输入或选中的文本；
- 按键；
- 窗口标题；
- 剪贴板；
- AutomationId；
- 完整程序路径或用户目录。

开发时可显式运行 `--capture-trace <文件>` 进行 20 秒无界面采集。该入口直接输出规范化 Trace：真实时间、进程 ID、坐标和耗时会被替换，只保留回放分类所需的语义。浏览器采集必须由人工确认真实窗口获得前台焦点；仅改变 DOM 的自动化操作不能作为 Edge/Chrome 兼容通过证据。

### WPS TSF 只读探针

可运行 `--probe-tsf <文件>` 进行 15 秒 TSF 可行性采样。启动后，在采样期间将 WPS 正文保持为前台并点击插入点；输出只包含状态、HRESULT、文本范围矩形、裁剪标记和耗时，不读取正文，也不改变提示层行为。`TextExtentReturned` 只能证明 TSF 返回了一个文档范围矩形，不能直接证明它是当前插入光标；`FocusContextUnavailable` 等结果应结合当前前台应用解释，不能单独作为 WPS 不支持的最终结论。

## 当前已知边界

- UIA Caret 优先使用由 CsWin32 从 Windows SDK 元数据生成的原生 COM `IUIAutomationTextPattern2.GetCaretRange`，不支持时再降级到托管 `TextPattern` 的折叠选区矩形。
- 已同时采集 Win32 `GetGUIThreadInfo` 和 MSAA `OBJID_CARET` 作为对照，但它们只能提供 Anchor，不能证明目标可编辑。
- WinEvent 前台/原生焦点事件以及 UIA 焦点和文本选区变化会触发重新观察；事件缺失时每 2 秒进行一次兜底观察。事件回调只发送信号，不读取目标内容。
- 已确认可编辑的上下文每 200ms 轻量刷新输入状态；完整 UIA 观察仍在有超时隔离的查询线程内执行。
- 单次 UIA 观察有 250ms 上限。超时期间不会并发创建更多查询线程，迟到结果丢弃；线程退出并经过冷却时间后才尝试恢复。
- 发布结果前会再次核对前台窗口、Win32 焦点窗口和 UIA RuntimeId；快速切换过程中拼接出的跨目标证据统一按 `ConflictingEvidence` 隐藏。
- 只有 UIA `Edit` 和 `Document` 控件可以建立文本编辑资格；单选按钮等非文本控件即使报告可写 Value 或残留 MSAA Caret，也必须判定为 `NoEditableFocus`。
- Edge、Chrome、系统对话框和快速焦点切换黄金 Trace 已建立，阶段 1 的五个首批场景均已完成独立人工验收。
- 输入状态与 Caret 在同一次观察中采样，并在发布前复核目标线程与 HKL；布局变化时旧结果按 `ConflictingEvidence` 隐藏。
- IMM32 模式也会前后复核；采样期间模式变化时整条观察按 `ConflictingEvidence` 丢弃。
- 默认 IME 窗口查询使用 25ms 短超时，并启用挂起和窗口退出保护；消息失败不会伪装成英文状态。
- 诊断历史会合并连续且语义完全相同的观察，只保留最新时间和耗时；Generation、输入状态或任一 IME 证据变化都会保留为独立记录。
- 当前支持一个受限的中文 IME 画像候选：中文语言布局的 `IME_CMODE_NATIVE` 表示中文，未设置表示英文；直接 IMM 与默认 IME 窗口的开关/模式证据冲突时保持 `Unknown`。这不是对所有 IME 的兼容承诺，仍需按应用和输入法版本扩充实机兼容矩阵；技术依据见 `windows-input-state-research.md`。
- WebView2 桌面应用允许 UIA 焦点位于前台宿主的后代进程，但必须由系统进程父链证明归属；不同宿主的同名 `msedgewebview2.exe` 不能互相兼容。已确认的 ProseMirror 可编辑画像要求 Chrome UIA 框架、聚焦类标记、TextPattern 和明确的可写状态。
- 资源管理器地址栏当前可确认可编辑但没有可信 Caret 坐标，按 `PositionUnknown` 隐藏；开始菜单搜索框已通过 `SearchHost + XAML + RichEditBox` 的 UIA Caret 路径，普通开始菜单区域不会建立可编辑上下文。
