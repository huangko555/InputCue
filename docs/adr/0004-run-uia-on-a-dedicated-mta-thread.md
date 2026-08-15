# ADR 0004：UIA 在专用 MTA 线程运行

Status: Accepted

InputCue 会访问整个桌面，包括自己的 WPF 界面。微软明确要求这类 UI Automation 客户端不要在 UI 线程调用 UIA，而应使用不拥有窗口的独立 COM MTA 线程；事件的订阅与移除也应在同一非 UI MTA 线程完成。InputCue 因此将所有 UIA 对象的创建、查询和释放限制在一个长期存活的 MTA 工作线程，通过有界通道向上层发布不可变结果。代价是需要显式管理线程生命周期和卡死降级，但可避免 UI 阻塞、STA 消息泵死锁和跨 apartment 对象失效。

依据：[Microsoft UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)。
