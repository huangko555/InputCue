# InputCue

InputCue 是一个 Windows 输入法状态提示器。它只在能够确认用户正处于可编辑输入位置时，安静地提示中文、英文或大写状态。

阶段 1 的只读诊断探针和五个首批黄金场景已经完成。阶段 2 已建立第一版纯 `IndicatorSession` 状态机，覆盖显示、隐藏、淡出、重复观察去抖和旧 Generation 淘汰；尚未实现输入法状态提示 UI。

## 文档入口

- [产品边界](docs/product-definition.md)
- [调研结论](docs/research-basis.md)
- [架构设计](docs/architecture.md)
- [开发计划](docs/development-plan.md)
- [诊断探针使用与验收](docs/diagnostic-probe.md)
- [领域词汇](CONTEXT.md)
- [架构决策](docs/adr/)

## 当前建议

当前仍只提供不绘制提示层的诊断探针。下一步是用阶段 1 的黄金 Trace 驱动 `IndicatorSession`，补齐状态序列回归和组合事件不变量；之后再接入输入状态探测与提示层。不要从设置页、主题或应用规则开始。

运行诊断探针：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj
```
