# WPS 正文插入光标定位调研

调研日期：2026-08-16（Windows 桌面版 WPS，当前项目实测环境）

## 结论先行

目前没有找到一个公开、可审计、无需注入的 WPS Windows API，能够直接返回 `KxWpsView` 自绘正文插入光标的屏幕坐标。InputTip、ImTip 和 `GetCaretPosEx` 都没有 WPS 专用 provider；它们能成功的前提仍是目标窗口暴露 UIA、MSAA、Win32 caret 或 Java Access Bridge 等标准接口。

因此建议保持当前 `PositionUnknown` 安全降级，同时做一个只读 TSF 可行性原型。只有在 WPS 确实暴露 `ITfContextView`/`ITfRange` 后，才进入正式实现；不要用鼠标位置冒充 caret，也不要把远程线程注入作为 V1 默认方案。

## 证据等级

- **A：一手规范/官方源码**：Microsoft、Qt 文档，或上游项目实际源码。
- **B：成熟开源实现**：能证明某条通用探测链存在，但不证明 WPS 兼容。
- **C：未发现/社区线索**：检索结果只能说明截至调研日没有公开实现，不能证明 WPS 内部绝对没有私有接口。

## 已核实的标准接口

### UI Automation

Microsoft 的 [`IUIAutomationTextPattern2::GetCaretRange`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern2-getcaretrange) 只对目标控件实现了 TextPattern2 时有效；拿到 range 后可用 [`IUIAutomationTextRange::GetBoundingRectangles`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-getboundingrectangles) 取得屏幕矩形。当前 WPS Trace 中没有得到可用 TextPattern/TextPattern2 caret range，因此这条路线已验证为 WPS 当前版本不可用，仍应保留为版本回归检查。

### Win32 GUI thread info 与 MSAA

[`GUITHREADINFO.rcCaret`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-guithreadinfo) 是线程 GUI 队列报告的 caret 矩形，不是跨进程保证；[`AccessibleObjectFromWindow`](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-accessibleobjectfromwindow) 配合 `OBJID_CARET`/`IAccessible::accLocation` 也只有目标实现了该可访问对象时才有结果。WPS 当前实测 Win32 caret、MSAA caret 均不可用。不能因为画面看见自绘竖线，就推断这两个接口应该有数据。

### Qt Accessibility

Qt 的 [`QAccessibleTextInterface`](https://doc.qt.io/qt-6/qaccessibletextinterface.html) 可以表达光标位置、选区和文本范围，但这是**应用实现的可访问性接口**；Qt 自绘控件若未向平台可访问性桥接注册文本对象，外部客户端不会自动得到该接口。Qt 的 [`QInputMethod::cursorRectangle`](https://doc.qt.io/qt-6/qinputmethod.html#cursorRectangle-prop) 只描述当前应用内部输入法上下文的光标矩形，不能从 InputCue 跨进程读取 WPS 内部私有对象。WPS 暴露 `Qt/KxWpsView` 类名不等于它暴露 Qt Accessibility。

## IA2 与 Legacy IAccessible

Legacy IAccessible/MSAA 的 Windows 契约由 [`IAccessible::accLocation`](https://learn.microsoft.com/en-us/windows/win32/api/oleacc/nf-oleacc-iaccessible-acclocation) 等接口定义，WPS 当前实测没有提供可用的 `OBJID_CARET` 对象。IA2 是独立的跨平台无障碍扩展规范；其接口和属性可参考 [LinuxA11y/IAccessible2](https://github.com/LinuxA11y/IAccessible2)。该项目的规范不等于 WPS 实现承诺，且“有 IA2 文本对象”仍需目标提供 caret/selection 的屏幕 extents。当前没有 WPS `KxWpsView` IA2 实现或可复用 provider 的一手证据，故列为需要原型而不是现成方案。

## 最值得做的正规实验：TSF

Microsoft 的 [`ITfContextView::GetTextExt`](https://learn.microsoft.com/en-us/windows/win32/api/msctf/nf-msctf-itfcontextview-gettextext) 明确提供“指定文本范围在屏幕坐标中的包围框”；它还可能返回 `TS_E_NOLAYOUT`，表示布局尚未计算，或对不可见文本返回空矩形。这个 API 需要已有目标 `ITfDocumentMgr`/`ITfContext`/`ITfContextView` 和一个有效 `ITfRange`，不是给任意 HWND 查询 caret 的全局函数。

建议先做只读探针：在 WPS 正文获得焦点后，枚举当前线程的 TSF文档管理器/上下文，确认能否取得活动 context，再尝试当前插入点附近 range 的 `GetTextExt`。探针必须记录 HRESULT、是否取得 range、矩形是否为空和耗时，不读取正文文本。若 WPS 没有可访问 context，TSF 路线应明确标记为失败，不继续猜坐标。

## 开源项目实际处理

### InputTip

上游仓库：[abgox/InputTip](https://github.com/abgox/InputTip)，本次核对提交 `00998a53f21c8273779ed10ec6359dd566828d93`（访问日期 2026-08-16）。其 [`GetCaretPosEx`](https://github.com/abgox/InputTip/blob/00998a53f21c8273779ed10ec6359dd566828d93/src/core/var.ahk#L1087) 的通用顺序包含 GUIThreadInfo、UIA TextPattern/TextPattern2、MSAA、WPF、ACC，以及可选的 `HOOK_DLL`。

`HOOK_DLL` 路径会 `OpenProcess`，再用 `VirtualAllocEx`、`WriteProcessMemory`、`CreateRemoteThread` 在目标进程执行读取逻辑。这是远程进程代码执行，不是 WPS API；受进程位数、权限/UIPI、安全软件和目标进程稳定性影响。源码中没有 `WPS` 或 `KxWpsView` 专用分支（本地源码全文检索结果）。因此 InputTip 只能作为标准接口和失败边界的参考，不能证明 WPS 已被解决。

### ImTip

上游仓库：[aardio/ImTip](https://github.com/aardio/ImTip)，本次核对提交 `52fd522f0e16082cc87c70fe1d2026d443a6f72d`。其 [`README.md` 窗口兼容性说明](https://github.com/aardio/ImTip/blob/52fd522f0e16082cc87c70fe1d2026d443a6f72d/README.md#3-窗口兼容性) 明确写出：多种接口都不支持的窗口会退化为鼠标输入指针位置，并可通过手工配置窗口类名。该退化策略正是 WPS 场景应避免的误报来源。README 还说明访问管理员窗口时 ImTip 需要以管理员权限启动。

ImTip 仓库源码未找到 `WPS`、`KxWpsView` 或 WPS 专用 caret provider；`codes/filter.aardio` 只提供自定义 `imeBar.getCaretEx` 扩展点，未实现 WPS 算法。因此它是兼容性框架线索，不是现成 WPS 方案。

### GetCaretPosEx / Tebayaki

InputTip 的源码链接指向 [Tebayaki/AutoHotkeyScripts `GetCaretPosEx.ahk`](https://github.com/Tebayaki/AutoHotkeyScripts/blob/main/lib/GetCaretPosEx/GetCaretPosEx.ahk)。该项目提供通用多后端链（GUIThreadInfo、MSAA、UIA、可选 Hook），未发现 `KxWpsView` 分支。它适合用来复现“标准接口是否有数据”，不应被当作 WPS 适配器。

## WPS 官方插件/API现状

WPS 官方开发入口为 [`platform.wps.cn`](https://platform.wps.cn/)。截至调研日，公开可检索资料主要面向插件、文档自动化和在线能力；没有找到 Windows 桌面 `KxWpsView` 的 caret 屏幕坐标接口说明，也没有找到可调用该坐标的公开 COM/插件契约。这个结论为 **C 级“未发现”**，不是断言 WPS 内部没有私有接口。若未来找到版本绑定的 WPS SDK，应单独评估许可、签名、安装和版本兼容性。

## 路线分组

### 可直接试验

| 路线 | 目的 | 权限/注入 | 结论 |
|---|---|---|---|
| UIA TextPattern2/TextPattern | 检查 WPS 新版本是否恢复标准文本 range | 无注入；可能受 UIPI 限制 | 保留为回归探针；当前 Trace 无效 |
| MSAA `OBJID_CARET`、`GUITHREADINFO.rcCaret` | 检查 WPS 是否偶尔暴露系统 caret | 无注入；目标权限可能影响 | 当前失败，失败即隐藏 |
| TSF `ITfContextView::GetTextExt` | 验证 WPS 是否提供可访问文本上下文与布局 | 无注入；需要 COM/TSF 线程模型 | **下一项只读原型** |

### 需要原型

| 路线 | 主要未知 | 风险 |
|---|---|---|
| TSF 文档管理器/上下文枚举 | 外部进程如何取得 WPS 当前 context；WPS 是否注册可访问 context | API 关联复杂，可能只对自身文本服务有效 |
| WPS 官方加载项/COM | 是否有桌面版内部 selection/caret API，及版本/许可边界 | 文档不公开或仅企业 SDK；不能依赖未审计接口 |
| IA2/Legacy IAccessible | WPS 是否实现 IA2 文本对象、selection 与 text extents | IA2 不是 WPS 官方承诺；需验证可访问对象和屏幕坐标 |

### 不建议

| 路线 | 原因 |
|---|---|
| 直接用鼠标位置 | 用户悬停工具栏、拖选正文、滚动时会误显示，不能代表插入点 |
| `HOOK_DLL`/远程线程/进程注入 | InputTip 源码确实证明技术上可做，但需要 `OpenProcess`/远程内存/线程执行；存在权限、安全软件、位数、崩溃和版本布局风险，不符合 V1 默认边界 |
| 读取 WPS 私有内存或逆向绘制对象 | 版本脆弱、难以审计和维护，可能违反软件许可或触发安全产品拦截 |

## 给 InputCue 的执行建议

1. 保持 WPS 正文 `PositionUnknown` 时隐藏，避免回退到鼠标坐标。
2. 新增独立 TSF 可行性探针，仅记录接口是否存在、HRESULT、矩形和耗时；不接入提示层，不读取正文内容。
3. 若 TSF 也无 context/range，正式记录“当前 WPS 版本无公开无注入定位方案”，转向产品级降级（不显示）而不是继续增加高风险 Hook。
4. 每次 WPS、Windows 或输入法升级后重新跑探针；不要把单次成功固化成通用兼容承诺。
