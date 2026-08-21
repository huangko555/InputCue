# ADR 0006：使用应用级点击锚点作为定位降级

Status: Accepted

部分自绘编辑器能够提供稳定的可编辑语义和输入状态，却不暴露 UIA、Win32 或 MSAA Caret。对经过 Trace 验证并显式加入白名单的应用画像，InputCue 允许把最近一次非拖动左键点击记录为短期 `PointerClick` Anchor。该 Anchor 只有在可编辑已确认、目标进程和窗口类一致、没有明确选区、标准 Caret 全部不可用且点击未超过两秒时才生效；Shift 点击、拖动、目标变化、输入活动和过期都会使其失效。

点击锚点只补齐定位，不建立 Eligibility。有效快照仍交给既有 `IndicatorSession`，因此首次上下文、状态变化、显示时长、输入活动隐藏、同一目标不重播和 Overlay 定位规则保持不变。鼠标移动或悬停永远不会建立 Anchor，未加入白名单的软件继续保持 `PositionUnknown` 并隐藏。
