# 架构设计

## 设计目标

Windows UIA、Win32、MSAA、IME 和 DPI 的差异必须被封装在少数深 Module 中。上层只消费稳定的语义状态，不理解窗口句柄、COM Pattern、IME 状态码或坐标来源。

核心数据流：

```text
系统事件与定时采样
        │
        ▼
InputContextEngine ──► InputContextSnapshot
        │                        │
        │                        ▼
        │                IndicatorSession
        │                        │
        ▼                        ▼
脱敏诊断事件               IndicatorViewState
                                 │
                                 ▼
                           OverlayRenderer
```

## 深 Module 与 Interface

### InputContextEngine

职责：把多个 Windows 信号合成为连续、可解释的输入上下文快照。

Interface 只暴露启动、停止和快照流；Windows API 细节属于 Implementation。快照至少包含：

- Generation；
- Eligibility；
- Input State；
- 可选 Anchor；
- Evidence Grade；
- Reason Code；
- 不包含文本内容的目标身份。

这是项目最深的 Module，也是主要测试表面。外部调用者不能选择“用 UIA 还是 Win32”。

### IndicatorSession

职责：把输入上下文、用户设置和时间转换成纯粹的显示状态，包括显示、隐藏、淡出和状态变化重播。

Interface 通过构造策略、`Observe(snapshot)` 和 `Advance(now)` 接受不可变输入并返回 `IndicatorViewState`。Module 不读取系统时钟，也不依赖 Windows 或 WPF；调用方只负责在同一串行执行路径中转交观察和时间。计时、状态优先级、旧 Generation 淘汰和重复观察去抖全部在这里完成，避免散落到窗口代码。

### OverlayRenderer

职责：将 `IndicatorViewState` 映射为不抢焦点、鼠标穿透、适配 DPI 的提示层。

Interface 只保留 `Apply(viewState)` 和生命周期操作。它不查询输入法、不判断选区。

### SettingsStore

职责：加载、验证、迁移并原子保存带版本号的设置。

损坏配置要回退到默认值并保留可诊断副本，不能阻止程序启动。

### Diagnostics

职责：记录结构化原因码、耗时和来源健康度。默认使用内存环形缓冲区；只有用户主动导出时才写文件。导出格式带独立 schema 版本，并能重新运行分类器以检查语义漂移。

不得记录按键、文本、窗口标题、完整文件路径或剪贴板内容。

## InputContextEngine 的内部 Seam

这些 Seam 默认保持 `internal`，不为了测试公开：

- `SystemEventSource`：前台窗口、焦点、UIA 结构变化和自适应采样；
- `EditableTargetProbe`：确认焦点目标是否可写；
- `SelectionProbe`：判断选区是否非折叠，不读取文本；
- `CaretProbe`：获取并验证插入光标矩形；
- `InputStateProbe`：读取中文、英文和 Caps Lock 状态；
- `AppProfileCatalog`：只承载有回归证据的兼容差异；
- `ObservationValidator`：校验进程、窗口、焦点、坐标和 Generation 一致性。

引擎运行时 Seam 有两个 Adapter：真实 Windows Adapter 负责事件订阅和跨进程观察，可控内存 Adapter 负责确定性验证取消、去抖、Generation 和有界发布。脱敏 Trace 通过 Core 回放模块验证分类与显示语义，不伪装成实时系统事件。其他 Probe 先保留为内部实现；只有出现第二个真实 Adapter 时再提升抽象。

## 可显示状态模型

| 状态 | 含义 | V1 行为 |
|---|---|---|
| `EditableCaret` | 已确认可写，选区折叠，存在有效插入点 | 显示 |
| `EditableSelection` | 可写目标中存在非折叠选区 | 有可靠锚点和已知输入状态时显示，否则隐藏 |
| `ReadOnlySelection` | 只读内容中存在非折叠选区 | 隐藏 |
| `NoEditableFocus` | 当前焦点不是可编辑目标 | 隐藏 |
| `PositionUnknown` | 可编辑已确认，但锚点不可用 | 默认隐藏；仅允许显式受控降级 |
| `Unknown` | 证据冲突、超时或权限不足 | 隐藏 |

输入法组合期不应被普通选区逻辑误判。组合窗口和候选窗口属于输入状态的附属 UI，不自动成为新的输入目标。

## 单次观察顺序

1. 捕获前台窗口、焦点目标和新的 Generation。
2. 确认焦点目标的进程归属和可编辑语义。
3. 判断是否存在非折叠选区；只读选区直接隐藏，可编辑选区继续探测活动 Caret 或安全边界。
4. 仅当可编辑资格成立时读取输入状态和 Caret。
5. 验证 Caret 属于当前目标，矩形位于合理屏幕范围。
6. 再次核对前台窗口、焦点和 Generation。
7. 只有全部一致才发布快照；否则丢弃，不用旧结果补齐。

## 不变量

- 鼠标位置和 I 型指针永远不能建立 Eligibility；
- Anchor 缺失不能反向改变目标为可编辑；
- 旧 Generation 的异步结果永不发布；
- 不同窗口或进程的事实不能组成同一个 Observation；
- `Unknown`、超时、权限不足和来源冲突均隐藏；
- 应用画像不能绕过隐私约束和一致性校验；
- 提示层自身不能获得输入焦点或参与目标探测。

## Windows Adapter 策略

建议按以下优先级收集语义，但最终由状态机统一裁决：

1. UIA 焦点元素、`ValuePattern.IsReadOnly`、Text Pattern 与 Caret/Selection 能力；
2. Win32 `GetGUIThreadInfo` 等线程焦点和 Caret 信息；
3. MSAA `OBJID_CARET` 作为旧控件补充；
4. 仅在“可编辑已确认”后，允许应用画像提供较弱 Anchor；
5. 鼠标位置只能作为最后的坐标降级，且默认关闭。

不能保证所有框架都完整暴露 UIA，因此目标是“可解释降级”，不是伪造统一能力。

## 并发与超时

- COM/UIA 查询运行在不拥有窗口的专用 MTA 线程；回调只入队，不直接修改状态；
- 引擎状态在单一串行执行器上推进；
- 每类外部查询都有短超时和取消；
- 队列有上限，新焦点事件可淘汰旧待处理观察；
- 提示层 UI 线程不执行跨进程查询；
- UIA Adapter 卡死或持续超时后进入短暂熔断，期间隐藏并记录原因码。

## 事件与采样

- 前台窗口和焦点变化以 WinEvent/UIA 事件驱动为主；
- 只有处于已确认可编辑目标时，才对输入状态做短周期自适应采样；
- 空闲、全屏排除应用和来源熔断时降频或停止；
- 鼠标左键按下且提示可见时，可临时重新评估选区，鼠标释放后再确认；
- V1 不使用全局键盘 Hook。

轮询间隔在探针阶段实测决定，不把魔法数字固化为公共设置。

## DPI、坐标与提示层

- 进程声明 Per-Monitor DPI Awareness V2；
- 所有 Adapter 明确标注坐标空间，在模块边界统一为物理屏幕坐标；
- 发布 Anchor 前验证所属显示器、工作区和矩形尺寸；
- 提示层不激活、鼠标穿透、不可被任务切换选中；
- 显示器切换、缩放变化和远程桌面重连会使现有 Anchor 失效并推进 Generation。

## 配置与兼容策略

配置使用带 schema 版本的 JSON，保存采用临时文件加原子替换。迁移必须可从旧版本逐级执行。

应用画像的准入条件：

1. 有稳定复现步骤；
2. 有脱敏 Trace；
3. 有自动回放或明确的集成回归用例；
4. 标注应用版本、UI 框架和适用范围；
5. 通用修复不可行或风险更高。

## 建议代码布局

```text
src/
  InputCue.App/               WPF 启动、托盘、设置与组合根
  InputCue.Core/              状态模型、IndicatorSession、公共契约
  InputCue.Windows/           InputContextEngine 与 Windows Adapter
  InputCue.Overlay/           提示层渲染
tests/
  InputCue.Core.Tests/        纯状态机与属性测试
  InputCue.Windows.Tests/     Trace 回放与有界集成测试
  traces/                     脱敏、可审查的最小事件样本
  compatibility/             应用场景矩阵
docs/
  adr/                        难以逆转的设计决策
```

不要按每个 Windows API 拆独立公共项目。保持对外 Interface 小，让复杂 Implementation 留在 `InputCue.Windows` 内，以获得更高 Depth、Leverage 和 Locality。
