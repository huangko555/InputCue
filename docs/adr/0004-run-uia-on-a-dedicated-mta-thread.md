# ADR 0004：UIA 在专用 MTA 线程运行

Status: Accepted

InputCue 会访问整个桌面，包括自己的 WPF 界面。微软明确要求这类 UI Automation 客户端不要在 UI 线程调用 UIA，而应使用不拥有窗口的独立 COM MTA 线程；事件的订阅与移除也应在同一非 UI MTA 线程完成。InputCue 因此让 UIA 事件源在长期存活的 MTA 调度线程上订阅和移除事件，并让一个观察会话复用同一条专用 MTA 查询线程；查询对象只在该线程内创建和使用，结果通过有界通道向上层发布不可变数据。查询线程一旦超时就进入隔离状态，在它返回前不得承接新查询；返回并经过冷却后淘汰，由新 MTA 线程接替。代价是需要显式管理线程生命周期和卡死降级，但可避免 UI 阻塞、STA 消息泵死锁、跨 apartment 对象失效，以及短生命周期托管线程句柄等待 GC 才释放造成的长期句柄增长。

查询线程的超时与替换规则见 ADR 0005。

依据：[Microsoft UI Automation threading guidance](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading)。
