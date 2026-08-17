# InputCue

InputCue 是一个 Windows 输入法状态提示器。它只在能够确认用户正处于可编辑输入位置时，安静地提示中文、英文或大写状态。

当前 V1 开发版已经具备中文、英文、美式键盘和 Caps Lock 四态圆点提示，并实现输入后隐藏、最短显示时间、同一输入目标去抖、托盘、单实例、开机后台启动和设置持久化。圆点支持 8 个 Caret 相对方向、横纵微调、尺寸缩放和设置页实时预览。

## 文档入口

- [产品边界](docs/product-definition.md)
- [调研结论](docs/research-basis.md)
- [架构设计](docs/architecture.md)
- [开发计划](docs/development-plan.md)
- [诊断探针使用与验收](docs/diagnostic-probe.md)
- [领域词汇](CONTEXT.md)
- [架构决策](docs/adr/)

## 当前限制

WPS 正文、Windows Terminal、资源管理器地址栏和开始菜单搜索仍作为独立兼容性专题延期；无法取得可靠 Caret 时默认隐藏，不使用鼠标位置伪装插入光标。主题、颜色和其他提示样式尚未进入实现。

运行开发版：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj
```
