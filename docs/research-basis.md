# 调研依据与可复用结论

> 本文仅记录公开资料、产品试用现象与通用工程结论。
> InputCue 不包含或复用第三方项目的非公开代码、资源、协议或反编译产物。
> 文中提及第三方产品仅用于功能和工程方案比较，不表示关联或背书。

## InputTip

[InputTip](https://github.com/abgox/InputTip) 证明了输入法状态提示具有真实、持续的用户需求，也展示了广泛输入法和应用兼容所需的长期维护成本。它的功能积累说明 V1 不应复制规则引擎、自动切换和多套提示方案。

可复用结论：

- 输入法状态、插入光标坐标、多显示器 DPI 和应用兼容是四类独立问题；
- 把兼容选项直接暴露给普通用户，会迅速增加学习成本；
- 自动切换属于主动改变系统状态，故障影响远大于被动提示，应推迟。

## ImTip

[ImTip](https://github.com/aardio/ImTip) 证明轻量提示器可以获得用户。在本项目作者的试用环境中观察到两类误报：静态网页拖选和普通对话框悬停也可能触发提示。

可复用结论：

- 鼠标 I 型指针不是编辑能力的证据；
- 能得到鼠标坐标，不代表存在插入光标；
- “是否显示”和“显示在哪里”必须是两个独立判断；
- 应用特判不能替代统一的输入上下文模型。

## 选区状态研究

通过对多类 Windows 选区工具的公开行为、产品体验和兼容性现象进行研究，可以确认：可靠的选区判断不是一次 API 调用，而需要综合交互状态、焦点归属、UIA 选区、坐标有效性、超时取消和异步结果代次。

InputCue 不复现第三方取词功能，只将以下内容作为通用工程原则，用于抑制状态提示误报：

- 识别非折叠文本选区并隐藏提示；
- 每次前台窗口、焦点或选区变化都推进代次；
- 异步结果返回后重新校验窗口、进程、焦点和代次；
- 不跨上下文拼接 UIA、Win32 和鼠标结果；
- 不读取文本内容也能判断选区是否存在；
- V1 不引入剪贴板、DOM 扩展、WPS COM 或完整全局手势 Hook。

## Windows 官方能力边界

- [UI Automation Core](https://learn.microsoft.com/en-us/windows/win32/winauto/entry-uiautocore-overview) 可让客户端访问其他应用暴露的控件语义和模式；
- [UI Automation Tree Overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview) 明确指出自动化树是动态的，并随 UI 框架而变化；
- [WPF Overview](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/) 说明 WPF 是 Windows 专用的 .NET 桌面 UI 框架；
- [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy) 是运行时版本和支持期限的选择依据。

因此，UIA 应是语义主路径，但不能成为唯一来源。Win32/MSAA 仅作为有边界的补充，所有来源都必须接受同一状态机和一致性校验。

## 不应继承的路线

- 仅凭鼠标形状或鼠标位置判断；
- 先显示再异步纠正；
- 失败后层层重试并跨窗口复用旧结果；
- 每遇到一个软件就在 UI 中增加一个技术选项；
- 把读取用户文本作为判断是否存在选区的前提；
- 在没有回归用例时增加进程名白名单。
