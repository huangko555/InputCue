# 飞书兼容性调研

更新时间：2026-08-19

本文只讨论 InputCue 在飞书云文档网页版和飞书 Windows 客户端中的输入框识别、选区/插入光标定位，以及如何用 Trace 决定是否适配。没有把第三方页面结构或进程名直接写成产品规则。

证据等级：

- **A：一手规范或官方实现文档**，包括飞书开放平台、Chromium 源码文档和 Microsoft UI Automation 文档；
- **B：本项目实测或已有黄金 Trace**，只对采集时的应用版本、Windows、浏览器和输入法组合有效；
- **C：待验证推断**，用于设计采集步骤，不能直接进入产品规则。

## 结论摘要

目前不能仅凭公开资料断言飞书 Windows 客户端使用 Electron、WebView2 或某一种 UI 框架。飞书官方开发文档公开的是云文档内容和服务端 API，不是桌面客户端内部渲染架构。因此客户端必须先做现场 Trace：确认前台宿主进程、焦点元素所属进程、`FrameworkId`、控件类型、TextPattern/Selection/Caret 来源，再决定通用修复或窄范围画像。

飞书云文档网页版运行在 Chromium/Edge 时，最值得优先验证的是浏览器生成的平台无障碍树：正文可能是 `Document`、`Group` 或其他可编辑容器，实际插入点未必暴露为普通 `Edit`。Chromium 的无障碍树会随页面状态更新；网页重渲染可能重建自动化节点。因此发送、协同更新、工具栏操作后的焦点回归不能仅凭元素身份变化就等同于用户切换了输入框。节点类型和是否重建均是 **C 级推断**，必须由飞书现场 Trace 确认。

首阶段建议只做 Trace 和回放，不写飞书进程名白名单。若正文满足“当前键盘焦点 + 明确可编辑 + 当前 Generation 内有 UIA TextPattern/合法 Caret 矩形”，可沿用通用 `EditableCaret`；若只有可编辑语义而没有可靠锚点，继续 `PositionUnknown` 并隐藏。

## 官方事实

### 飞书云文档与服务端文档

- 飞书开放平台把云文档能力作为服务端文档 API 提供，入口为[云文档 API 概览](https://open.feishu.cn/document/server-docs/docs/docs/overview)。这是内容读取、创建、更新等服务接口，不是浏览器编辑器的 UI Automation 合约。
- 飞书开放平台的[文档内容管理](https://open.feishu.cn/document/server-docs/docs/docs/content-management)描述文档块、内容和权限等服务模型；它不能证明网页正文对应哪个 UIA `ControlType`，也不能提供插入光标屏幕坐标。
- 飞书官方[开放平台文档](https://open.feishu.cn/document/home/index)公开了 Web/API 集成边界，但未公开飞书 Windows 客户端的 UI 框架、无障碍 Provider 实现或版本稳定的桌面控件树。

事实边界：以上资料可用于确认“云文档是网页/API 产品能力”，不能用来推断客户端是 Electron、WebView2、Chromium Embedded Framework 或原生控件。客户端框架结论必须来自实际进程/窗口/Provider Trace 或飞书官方另行声明。

仓库 `development-plan.md` 已记录一次 DOM 侧观察：飞书云文档编辑区出现 Slate 属性以及不可见的辅助 `textarea`。这是 **B 级现场线索**，不是飞书官方稳定契约；InputCue 也不读取 DOM，所以实现仍只能依据 Windows UIA/Win32/MSAA Trace，不能把 Slate 属性或辅助 `textarea` 直接写成运行时规则。

### Chromium/Edge 无障碍与网页编辑

- Chromium 官方的[Accessibility 概览](https://chromium.googlesource.com/chromium/src/+/main/docs/accessibility/overview.md)说明浏览器将网页语义暴露给平台无障碍 API；平台客户端看到的是浏览器生成的无障碍表示，不是 DOM 本身。
- Chromium 官方的[Accessibility Tree 文档](https://chromium.googlesource.com/chromium/src/+/main/docs/accessibility/browser/accessibility_tree.md)说明无障碍树由渲染器/浏览器维护，并会随页面状态变化更新。网页编辑器重渲染时，自动化节点身份和树路径可能变化。
- Microsoft 的[UI Automation Tree Overview](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-tree-overview)明确指出自动化树取决于 Provider 和控件实现，树结构不是所有框架统一固定的。

这些资料支持的推断是：飞书网页正文不保证暴露为 `Edit`；必须记录实际 `ControlType`、`ClassName`、`FrameworkId`、`IsReadOnly` 和 Pattern 能力。它们不能证明飞书具体页面一定采用 `Document`、`Group` 或任何其他节点类型。

### Selection/Caret 的官方能力边界

- Microsoft 的[TextPattern](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-textpattern-overview)说明客户端可读取 Provider 暴露的文本范围和选区语义；它不保证每个控件都实现该 Pattern。
- Microsoft 的[`IUIAutomationTextPattern2::GetCaretRange`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern2-getcaretrange)说明活动插入点需要目标实现 TextPattern2；没有该 Pattern 就不能从该接口取得 Caret Range。
- Microsoft 的[`IUIAutomationTextRange::GetBoundingRectangles`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-getboundingrectangles)说明文本范围可请求屏幕矩形，但空范围、不可见范围和 Provider 不支持时可能没有可用矩形。
- Microsoft 的[UI Automation Events](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-events-overview)说明客户端可以接收焦点、属性和结构变化事件，但事件覆盖范围依赖 Provider；结构变化不等于用户语义上的输入框切换。
- Microsoft 的[UI Automation Threading Issues](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues)要求跨进程 UIA 客户端避免在拥有 UI 窗口的线程上进行不当调用。仓库现有 MTA 查询线程与该约束一致。

因此适配顺序应为 UIA TextPattern/TextPattern2 和 Selection，再用 Win32/MSAA 作为已验证的旧控件补充；不能用鼠标位置、I 型指针或旧 Generation 的旧 Caret 假造定位。

## 对仓库现有架构的映射

现有 `InputContextEngine` 将前台窗口、焦点元素、可编辑资格、选区、Caret、输入状态和 Generation 合并成不可变快照；`InputContextClassifier` 将无锚点的可编辑目标降为 `PositionUnknown`；`IndicatorSession` 负责显示时长和重播。飞书适配应先进入 Windows Adapter 的观察/画像层，不改变 Core 的显示状态机。

当前可复用的浏览器基线是 `tests/traces/edge-basic.json` 与 `chrome-basic.json`：它们证明项目已处理普通 Chromium `Edit` 的 UIA TextPattern 或 MSAA Caret，同时要求静态 `Document` 选区和 RadioButton 隐藏。该结论属于 **B 级项目证据**，不等于 Chromium 对所有网页编辑器的兼容承诺。飞书云文档正文若是 `Document`/`Group`，不能因为类型不同就放宽；需用飞书自己的正例和负例确认其 `IsReadOnly`、Selection、TextPattern 与 Caret 证据。

客户端若出现宿主进程与渲染子进程分离，应沿用仓库已有的进程一致性/父链验证思路：接受“焦点元素进程可追溯到当前前台宿主”的事实，不接受仅凭 `ProcessName` 相同或固定子进程名的放宽。当前没有官方资料证明飞书客户端的宿主/渲染进程关系，需实测。

## 2026-08-19 网页端实机采集

在 Edge 的可编辑飞书云文档正文中完成了一次 20 秒脱敏 Trace 和一次只读 UIA 能力采样。原始 Trace 保存在 `.local/feishu-web-capture.json`，不进入黄金 Trace。

- 正文焦点元素稳定暴露为 `ProcessName=msedge`、`FrameworkId=Chrome`、`ControlType.Group`、`ClassName=page-block root-block`；10 条观察保持同一 Generation 和同一控件形态。
- 该元素报告 `HasKeyboardFocus=true`、`IsReadOnly=false`，并支持 `TextPattern` 与 `ScrollItemPattern`。
- `TextPattern.GetSelection()` 返回一个范围，该范围返回一个边界矩形；本次尚未完成矩形尺寸、随方向键移动以及折叠/非折叠选区的对照验证。
- 现有通用策略不接受该 `Group`，所以正式探针将它分类为 `NoEditableFocus`，没有继续运行 TextPattern2/Caret 路径。这与用户观察到的正文无提示一致。

这组证据形成了 `Chrome + Group + page-block root-block + 键盘焦点 + 明确可写 + TextPattern` 的候选画像，但尚不能进入产品规则。实施前仍需取得只读文档、侧栏/工具栏、标题和隐藏辅助输入框的近似负例，并确认折叠选区矩形是随当前插入点移动的合法 Caret，而不是正文块外框或旧范围矩形。

后续补采样已完成上述验证：折叠范围返回 `1x20` 的矩形，左右方向键移动时矩形 X 坐标随插入点变化；拖选返回非折叠范围，矩形宽度随选区变化。标题/侧栏近似负例暴露为 `Hyperlink`；飞书还会暴露 `Edit + docx-selection-hidden-textarea` 的不可见辅助选区控件，不能按通用 `Edit` 接受。网页端因此具备进入实现的证据基础：候选条件为 `FrameworkId=Chrome + ControlType.Group + ClassName=page-block root-block + HasKeyboardFocus + IsReadOnly=false + TextPattern`，并排除 `docx-selection-hidden-textarea`；仍需用黄金 Trace 覆盖正文、标题/侧栏、辅助 textarea、只读文档和工具栏按钮。

## 分阶段方案

### 阶段 0：环境和版本记录

分别记录飞书网页版的浏览器名称/版本、飞书桌面客户端版本、Windows 版本、输入法和 DPI。版本只进入兼容性记录和测试元数据，不作为未经验证的运行时匹配条件。不得记录文档标题、正文、窗口标题、按键或完整路径。网页与客户端必须分开采集，避免把一个应用的 Provider 结论迁移到另一个应用。

### 阶段 1：脱敏 Trace

每个目标至少采集以下场景：

| 场景 | 预期用途 | 必须观察 |
|---|---|---|
| 云文档正文点击后折叠插入点 | 正例 | 焦点、可编辑、Selection、TextPattern/TextPattern2、Caret 来源 |
| 正文拖选 | 选区正例 | 非折叠 Selection 是否有合法矩形；不读取文本 |
| 标题、评论/搜索输入框 | 标准或近标准输入正例 | ControlType、只读状态、Generation、Anchor |
| 侧栏、面包屑、工具栏 | 非编辑负例 | 不因可聚焦、鼠标悬停或残留 Caret 显示 |
| 静态正文拖选/工具栏按钮 | 负例 | `ReadOnlySelection` 或 `NoEditableFocus` 不显示 |
| 输入、撤销、协同更新 | 节点稳定性 | UIA 元素身份、StructureChanged、Caret 变化 |
| 回车发送/点击工具栏后回到正文 | 重播过滤 | `Edit/Document/Group -> non-edit -> same target` 的实际时序 |
| 前后台切换、切换文档 | Generation | 旧结果不能跨文档复用 |

飞书 Windows 客户端重复同一矩阵，并额外记录顶层窗口、焦点元素进程及父子进程关系。每个场景至少保存一个正例和一个最接近的负例；Trace 进入黄金目录前须规范化时间、PID、坐标和耗时。

### 阶段 2：通用策略验收

优先验证现有通用策略是否已经足够：焦点目标明确可编辑、非折叠选区可识别、Caret 矩形属于当前目标且在屏幕范围内、输入状态和 Generation 一致。仅当 Trace 显示某一通用假设错误时，修改 `EditableControlPolicy`、Caret 采样或 Generation 去抖，并补 Core/Windows 回放测试。

### 阶段 3：窄范围画像（有证据才进入）

只有在存在稳定 Trace、版本边界和自动回归时，才增加画像。运行时画像键应组合 `FrameworkId`、`ControlType`、可审计的 Class/automation properties，以及必要时经系统证明的宿主/父链关系；不能只用 `feishu.exe`、浏览器进程名或 URL。应用版本属于画像的适用范围和回归元数据，不应在缺少稳定版本读取方式时硬塞进运行时匹配键。画像不得绕过 `ObservationValidator`、权限超时和 Anchor 合法性检查。

## 初始验收矩阵

| 目标 | 正例通过条件 | 必须保持隐藏的负例 | 当前结论 |
|---|---|---|---|
| 云文档网页正文 | 当前焦点、可编辑、Caret 来自 UIA TextPattern/TextPattern2 或经验证 MSAA；矩形属于目标 | 静态正文选区、只读块、工具栏按钮 | 未知，需实测 |
| 云文档网页标题/评论输入框 | 同上；若是标准 Chromium Edit 可复用浏览器路径 | 仅 I 型鼠标指针、无 Caret 的可写容器 | 未知，需实测 |
| 网页回车/协同重渲染 | 同一语义目标回归时不重复提示；真正切换文档/输入框时重播 | 不同窗口、不同目标、输入状态真实变化 | 未知，需 Trace |
| 飞书 Windows 原生编辑控件 | UIA/Win32/MSAA 有一致 Caret，进程/窗口校验通过 | 可编辑资格成立但没有安全 Anchor | 未知，需实测 |
| 飞书 Windows 网页容器 | 焦点子进程沿父链属于宿主，Pattern/Caret 可复核 | 仅凭子进程名接受，或使用旧坐标 | 未知，需实测 |

单个场景通过还不算适配完成。最低验收要求是：同一版本连续重复 10 次无漏报或误报；正文正例与最接近的只读/按钮负例成对通过；中文、英文和 Caps Lock 状态变化可见；100% 与一个非 100% DPI 下 Anchor 不明显漂移；快速切换文档或窗口时旧 Generation 不回放。若任何正例只能得到可编辑语义而没有可信 Caret，应明确记录为 `PositionUnknown`，而不是以控件外框或鼠标位置代替。

## 不能从当前资料得出的结论

1. 飞书 Windows 客户端是否 Electron、WebView2、CEF 或原生混合架构：官方公开资料未确认。
2. 飞书云文档正文的稳定 UIA `ControlType`、ClassName、AutomationId、FrameworkId：只能通过当前版本现场 Trace 确认。
3. 网页协同编辑、发送、切换文档时是否提供可靠 `StructureChanged`/FocusChanged/Caret 事件：需实际采集，不能从 API 文档推断。
4. 是否存在飞书专用 Caret API 或可审计的桌面扩展：当前范围内未找到官方公开接口。没有标准 Anchor 时应保持隐藏。

## 最小下一步

先采集两组 20 秒脱敏 Trace：A 组为浏览器中的飞书云文档，B 组为飞书 Windows 客户端。每次采集只做一个可复述动作，按“正文折叠插入点 -> 正文拖选 -> 标题/评论输入 -> 按钮负例 -> 切换文档”的顺序分别导出，避免把多段焦点流转混在同一结论中。分析前先确认 Trace 中的 `ProcessName`、前台窗口和焦点进程确实对应目标。

决策顺序固定为：现有通用规则已经通过，则只新增黄金 Trace；通用假设对多种 Chromium 编辑器都不成立，则做通用修复；只有飞书具有稳定且可区分的额外证据时才增加窄画像。若只能确认可编辑而无 Caret，产品体验应显示为不提示，而不是猜测位置。
