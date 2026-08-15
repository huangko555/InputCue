# InputCue

InputCue 是一个 Windows 输入法状态提示器。它只在能够确认用户正处于可编辑输入位置时，安静地提示中文、英文或大写状态。

阶段 1 的只读诊断探针和五个首批黄金场景已经完成。代码仓库采用 C#、.NET 10 LTS 与 WPF；当前准备进入纯状态机阶段，尚未实现输入法状态提示。

## 文档入口

- [产品边界](docs/product-definition.md)
- [调研结论](docs/research-basis.md)
- [架构设计](docs/architecture.md)
- [开发计划](docs/development-plan.md)
- [诊断探针使用与验收](docs/diagnostic-probe.md)
- [领域词汇](CONTEXT.md)
- [架构决策](docs/adr/)

## 当前建议

当前只提供不绘制提示层的诊断探针。下一步是实现与 Windows API 解耦的 `IndicatorSession`，固化显示、隐藏、淡出、旧 Generation 淘汰等不变量；之后再做提示层。不要从设置页、主题或应用规则开始。

运行诊断探针：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj
```
