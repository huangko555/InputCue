# InputCue

InputCue 是一个 Windows 输入法状态提示器。它只在能够确认用户正处于可编辑输入位置时，安静地提示中文、英文或大写状态。

项目当前处于诊断探针阶段。代码仓库采用 C#、.NET 10 LTS 与 WPF，正在验证焦点、可编辑语义、选区和 Caret；尚未实现输入法状态提示。

## 文档入口

- [产品边界](docs/product-definition.md)
- [调研结论](docs/research-basis.md)
- [架构设计](docs/architecture.md)
- [开发计划](docs/development-plan.md)
- [诊断探针使用与验收](docs/diagnostic-probe.md)
- [领域词汇](CONTEXT.md)
- [架构决策](docs/adr/)

## 当前建议

当前只提供不绘制提示层的诊断探针，用于证明 InputCue 能区分真实编辑光标、文本选区和普通鼠标悬停；验证通过后再做提示层。不要从设置页、主题或应用规则开始。

运行诊断探针：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj
```
