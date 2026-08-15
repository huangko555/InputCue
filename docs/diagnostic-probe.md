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

## 当前已知边界

- UIA Caret 优先使用由 CsWin32 从 Windows SDK 元数据生成的原生 COM `IUIAutomationTextPattern2.GetCaretRange`，不支持时再降级到托管 `TextPattern` 的折叠选区矩形。
- 已同时采集 Win32 `GetGUIThreadInfo` 和 MSAA `OBJID_CARET` 作为对照，但它们只能提供 Anchor，不能证明目标可编辑。
- UIA 焦点和文本选区变化会触发重新观察；事件缺失时每秒进行一次兜底观察。事件回调只发送信号，不读取目标内容。
- 单次 UIA 观察有 250ms 上限。超时期间不会并发创建更多查询线程，迟到结果丢弃；线程退出并经过冷却时间后才尝试恢复。
- 发布结果前会再次核对前台窗口、Win32 焦点窗口和 UIA RuntimeId；快速切换过程中拼接出的跨目标证据统一按 `ConflictingEvidence` 隐藏。
- 来自 Edge、Chrome 和系统对话框的黄金 Trace 采集仍属于阶段 1 后续工作。
- 当前输入状态保持 `Unknown`，微软拼音和 Caps Lock 在阶段 3 接入。
