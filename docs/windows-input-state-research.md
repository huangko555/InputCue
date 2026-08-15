# Windows 输入状态只读探测调研（阶段 3）

## 结论摘要

阶段 3 可以可靠取得“当前前台线程正在使用的输入区域设置（HKL）”以及“该 HKL 是否带 IME”；但 Windows 公开 API **没有为第三方跨进程提示器承诺**一个能在所有 TSF 输入法、所有目标控件中稳定给出“微软拼音当前中文/英文模式”的单一读取接口。

因此 V1 应把输入状态视为可失败的外部证据：仅在同一次观察中完成前台/焦点复核、IME 探测成功、结果符合已验证画像时发布 `Chinese` 或 `English`；其余情况一律为 `Unknown` 并隐藏。绝不能复用上一次的 IME 状态补齐本次结果。

本文将“官方事实”和“工程推断/决策”明确分开。链接均为 Microsoft Learn 或 Microsoft 维护的 Win32 SDK 元数据项目；调研日期为 2026-08-16。

## 1. Caps Lock：API 选择与线程语义

### 官方事实

- [`GetKeyState(VK_CAPITAL)`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate) 的低位是 toggle 状态；对 Caps Lock，低位为 1 表示锁定开启。高位仅表示按键此刻是否按下。
- 该 API 的返回值随**调用线程**从其消息队列读取键盘消息而变化；它不是硬件中断级状态。[`GetKeyboardState`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardstate) 也明确是调用线程的队列状态，其他线程的键盘消息不会更新它。
- [`GetAsyncKeyState`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate) 适合“按键目前是否按下”的物理状态；其低位只是“自上次查询以来是否曾按下”，且微软明确说明在抢占式多任务下不可靠。它不是 Caps Lock toggle 状态 API。
- [`AttachThreadInput`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput) 能让两个线程共享键盘状态与焦点，但会在附加时重置可由 `GetKeyState`/`GetKeyboardState` 读取的键状态；还要求双方存在消息队列，且不能跨 desktop。

### 工程推断与 V1 决策

1. **首选读取 API 是 `GetKeyState(VK_CAPITAL) & 0x0001`，不是 `GetAsyncKeyState`。** 调用必须固定在 InputCue 自己的、持续处理消息的 UI/STA 线程上，且不得从后台工作线程混读。
2. 这仍不是“读取前台应用键盘队列”的官方保证。为获得这种效果而暂时 `AttachThreadInput` 会改变/重置相关状态，增加焦点耦合和恢复风险；V1 不使用它。
3. 将 Caps Lock 作为低健康度、可实验验证的来源：读取失败、前后上下文变化、或该来源在当前机器画像中与实际状态不一致时，发布 `Unknown`，不保留旧值。它不能单独建立提示显示资格，也不应覆盖已确认的 IME 中/英文状态。
4. 若 V1 的产品语义要求“全局、始终准确的 Caps Lock 指示”，在“不使用全局键盘 Hook”的既定边界下，公开 API 证据不足；应推迟该承诺，而不是把 best-effort 值伪装为确定状态。

## 2. 前台目标的布局与 IME 身份

### 官方事实

1. [`GetForegroundWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow) 返回用户当前工作的前台窗口，切换期间可能返回 `NULL`。
2. [`GetWindowThreadProcessId`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid) 返回创建指定窗口的线程 ID 和可选进程 ID。
3. [`GetKeyboardLayout(threadId)`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout) 返回该线程活动输入区域设置 HKL。微软特别说明 HKL 的概念宽于物理键盘布局，可以是 IME、语音转文本或其他输入形式；布局会动态变化。
4. [`ImmIsIME(hkl)`](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immisime) 直接回答指定输入区域设置是否带 IME。
5. [`WM_INPUTLANGCHANGE`](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-inputlangchange) 的 `lParam` 带 HKL，但微软说明 IME profile 改变**未必**发送此消息；TSF 的 `ITfActiveLanguageProfileNotifySink` 才是处理活动语言/文本服务变化的通知接口。

### 建议的单次只读观察

这是工程流程，不是微软承诺的原子事务：

```text
foreground HWND
  -> GetWindowThreadProcessId(foreground)
  -> GetKeyboardLayout(foregroundThread)
  -> ImmIsIME(HKL)
  -> 仅在已确认 EditableCaret 时，尝试读取 IME 状态
  -> 再次读取 foreground / focus / process / thread / HKL
  -> 任一不一致：丢弃整个观察
```

- 对焦点子窗口：以现有 `GetGUIThreadInfo`/UIA 已确认的焦点目标为优先，但布局归属以它所属 GUI 线程为准。
- HKL 只回答“该线程当前选中的输入区域设置/文本服务”，不回答“这个可编辑控件可否收文本”、也不回答“当前是否正在选中文本”。这两项仍由阶段 1/2 的 Eligibility 证据决定。
- 对语言显示，Windows 8 起微软偏好 `Windows.Globalization.Language.CurrentInputMethodLanguageTag`，而不是缓存 HKL；但该成员描述的是**当前**输入法语言，不提供“指定前台线程”的参数。因此它不适合替代本项目的跨窗口关联依据。参见 [`GetKeyboardLayout` 备注](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout) 与 [`WM_INPUTLANGCHANGE` 备注](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-inputlangchange)。

## 3. IMM32：可调用事实、跨进程边界与消息路径

### `ImmGetContext` / `ImmGetOpenStatus` / `ImmGetConversionStatus`

| API | Microsoft 明确说明 | 对 InputCue 的边界 |
|---|---|---|
| [`ImmGetContext(HWND)`](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetcontext) | 返回指定窗口关联的输入上下文；结束后必须 `ImmReleaseContext`。 | 文档没有声明该 `HIMC` 可作为跨进程读取的稳定契约，也没有承诺在 TSF 控件中一定存在。将它视为可失败的实验路径，绝不缓存句柄。 |
| [`ImmGetOpenStatus(HIMC)`](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetopenstatus) | 返回 IME open/closed。 | 只有已获得有效 `HIMC` 时才有意义；open/closed 本身不等价于“中文/英文”。 |
| [`ImmGetConversionStatus(HIMC)`](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetconversionstatus) | 获取转换/句子模式；仅在 IME 支持这些模式时才设置。 | `IME_CMODE_NATIVE` 的定义是 Native vs Alphanumeric；不能把任意 IME 的该位普遍命名为“中文”。 |

[`IME Conversion Mode Values`](https://learn.microsoft.com/en-us/windows/win32/intl/ime-conversion-mode-values) 明确 `IME_CMODE_NATIVE` 为 Native 模式、未设置为 Alphanumeric 模式；同时也存在全角、假名、韩文等其他转换位。因此“中文 HKL + open + native”至多是**微软拼音画像的候选映射**，不是 IMM32 对所有 IME 的语言语义承诺。

**跨进程结论（官方事实与不确定性）：** 这些函数的 Microsoft Learn 页面都描述了 API 的参数和返回值，但没有给出“可跨进程读取前台第三方窗口的输入上下文，并在现代 TSF 输入法下稳定返回模式”的保证。不能从函数接受 `HWND` 推导出这一保证。V1 可以试用并记录成功/失败与耗时；失败、`NULL`、访问异常或前后复核不一致均安全隐藏。

### `ImmGetDefaultIMEWnd` + `WM_IME_CONTROL`

- [`ImmGetDefaultIMEWnd`](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetdefaultimewnd) 说明系统为每个线程创建默认 IME 窗口，应用可以向它发送 `WM_IME_CONTROL`。
- 但 [`WM_IME_CONTROL`](https://learn.microsoft.com/en-us/windows/win32/intl/wm-ime-control) 的桌面文档把它描述为应用控制其创建的 IME 窗口，列出的命令主要是候选/组合/状态窗口 UI；它没有把 `IMC_GETOPENSTATUS` 或 `IMC_GETCONVERSIONMODE` 作为面向跨进程读取模式的桌面契约。
- [`SendMessage`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagew) 和 [`SendMessageTimeout`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw) 在跨线程时会等待接收窗口处理消息；消息发送受 UIPI 限制，只能发往完整性级别不高于发送方的进程。`SendMessageTimeout` 可设置超时，并提供 `SMTO_ABORTIFHUNG`、`SMTO_ERRORONEXIT`；系统只自动封送 `WM_USER` 以下的系统消息。

**工程决策：** `ImmGetDefaultIMEWnd` + `WM_IME_CONTROL` 可以作为隔离的 fallback 实验，但不进入 V1 的“已支持”主路径。若以后实测某一版本的微软拼音稳定支持它，调用必须使用 `SendMessageTimeout`（短超时、`SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT`、不在 UI 线程）、每次重新取窗口句柄、并把超时/UIPI/返回异常全部视为 `Unknown`。绝不使用无超时的 `SendMessage`。

## 4. 微软拼音与 TSF：哪些是可靠事实，哪些只能试验

### 可作为生产前提的官方事实

- Windows 当前要求自定义 IME 使用 [Text Services Framework (TSF)](https://learn.microsoft.com/en-us/windows/apps/design/input/input-method-editor-requirements)；文档说明系统会阻止以旧 IMM32 实现的 IME。TSF 是现代文本服务链路的一部分，不是 InputCue 可以绕开的细节。
- `GetKeyboardLayout(threadId)` + `ImmIsIME(HKL)` 能可靠区分“该前台线程当前选择了带 IME 的输入区域设置”与“非 IME 布局”，前提是前后身份复核一致。
- `ImmGetOpenStatus` 与 `ImmGetConversionStatus` 的语义只在其函数成功、并且相应 IME 支持模式时成立；`ImmGetConversionStatus` 的官方文档明确包含这一支持前提。

### 只能作为实验路径的推断

- 对已验证的**微软拼音版本 + 应用框架 + Windows 版本**组合，可尝试：

  ```text
  non-IME HKL                         -> English（布局级）
  Microsoft Pinyin HKL + !open        -> English（画像候选）
  Microsoft Pinyin HKL + open + NATIVE -> Chinese（画像候选）
  Microsoft Pinyin HKL + open + !NATIVE -> English（画像候选）
  ```

- 上述后三行不是 Microsoft 对微软拼音的公开兼容承诺。TSF 组件、目标应用是否使用 IMM 兼容层、权限级别和输入法版本均可能令读数缺失或不同步。
- 组合期、候选窗、全角/半角以及“中英文”以外的模式不应被此二值映射伪造为确定状态；V1 在无法解释时为 `Unknown`。

## 5. V1 的保守判定与失败降级

### 判定优先级

1. 先运行阶段 1/2 的输入上下文探测。仅 `EditableCaret` 且有通过验证的当前 Generation 才允许读取输入状态；选区、只读、无焦点、权限不足和坐标缺失都不读/不显示。
2. 采样前取得前台 HWND、前台线程/进程、焦点 HWND/线程与 HKL；采样后重复取得并逐项比较。任一变化丢弃。
3. `ImmIsIME(HKL) == false` 时，可发布低歧义的 `English`（含 `HKL` 与来源健康度），但不能据此推断 Caps Lock。
4. `ImmIsIME(HKL) == true` 时，先尝试 `ImmGetContext` 路径；只有 `HIMC` 有效、调用成功、该微软拼音画像已在兼容矩阵通过、且模式位可解释时，才发布 `Chinese`/`English`。
5. Caps Lock 只作为独立附加证据读取；不反向把未知 IME 状态解释为 Caps Lock，也不因 Caps Lock 值而突破 Eligibility。

### 必须降级为 `Unknown` 并隐藏的情况

- 前台窗口为 `NULL`、前后 HWND/线程/进程/焦点/HKL 任一不同；
- HKL 为 IME，但 `HIMC` 不可得、IMM 调用失败、模式位不受该 IME 支持、或没有该输入法画像；
- 目标是管理员窗口且消息/API 受 UIPI 拒绝；
- 外部调用超时、异常、熔断中，或尝试读取的窗口已销毁；
- Caps Lock 状态不能从规定线程获得新读数，或其来源健康度不合格；
- 当前处于组合/候选等无法映射为稳定二值中英文的阶段。

### 建议的实现约束

- 新建内部 `WindowsInputStateProbe`，输入为已验证的目标身份和 Generation，输出值、证据等级、失败原因、来源与耗时；不把 `HIMC`、`HKL` 或 IME 控制码泄漏到 Core。
- 读取 IME 的外部调用必须设短超时、取消和来源熔断；不在 Overlay/UI 线程同步等待。
- 首批兼容矩阵至少覆盖：微软拼音（中文/英文切换、Caps 开/关）× 记事本、Edge、Chrome；每一正例都配一个静态选区/按钮悬停负例。每个通过的组合都保存脱敏 Trace，记录 Windows、输入法和应用版本。
- 调试记录只保存匿名化的 HKL/结果类别/失败码/耗时，不保存文字、按键序列、窗口标题或文件路径。

## 6. 可执行的阶段 3 验收标准

- `GetKeyboardLayout(foregroundThread)`、`ImmIsIME` 与前后身份复核有单元测试和实机 Trace；
- IMM32 与 IME-window fallback 均有严格超时、取消、UIPI/失败到 `Unknown` 的测试；
- 微软拼音的中/英映射先作为 `Experimental` 画像，未通过 A 级矩阵前不宣称通用支持；
- Caps Lock 以单独健康度报告，未证明跨前台线程一致前不作为“始终准确”的产品承诺；
- 任意失败只能让提示消失，不能造成误显示，更不能改变用户输入法。

## 7. 参考实现边界

InputTip 与 ImTip/aardio 仅用于发现候选 API、兼容场景和失败案例。InputCue 的实现依据 Windows API 契约、自有数据模型和回归测试独立完成，不复制或翻译其源码、状态机和配置结构。

阶段 3 已将默认 IME 窗口消息路径实现为独立的实验探针：每条消息使用 25ms 短超时，成功返回的零值与查询失败分别表示；结果只进入诊断证据，在兼容矩阵验证前不映射为中文或英文。该路径不改变前述“桌面公开文档未承诺通用跨进程兼容性”的结论。

## 官方资料索引

- [GetKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeystate)、[GetKeyboardState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardstate)、[GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate)、[AttachThreadInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-attachthreadinput)
- [GetForegroundWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getforegroundwindow)、[GetWindowThreadProcessId](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)、[GetKeyboardLayout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getkeyboardlayout)、[WM_INPUTLANGCHANGE](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-inputlangchange)
- [ImmIsIME](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immisime)、[ImmGetContext](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetcontext)、[ImmGetOpenStatus](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetopenstatus)、[ImmGetConversionStatus](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetconversionstatus)、[IME Conversion Mode Values](https://learn.microsoft.com/en-us/windows/win32/intl/ime-conversion-mode-values)
- [ImmGetDefaultIMEWnd](https://learn.microsoft.com/en-us/windows/win32/api/imm/nf-imm-immgetdefaultimewnd)、[WM_IME_CONTROL](https://learn.microsoft.com/en-us/windows/win32/intl/wm-ime-control)、[SendMessage](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagew)、[SendMessageTimeout](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendmessagetimeoutw)
- [IME requirements / TSF](https://learn.microsoft.com/en-us/windows/apps/design/input/input-method-editor-requirements)、[Microsoft Win32 metadata](https://github.com/microsoft/win32metadata)
