# Third-party notices

InputCue 的源代码采用 `GPL-3.0-only`。以下条目说明构建或便携发行中涉及的第三方组件；各组件仍分别受其原始许可证约束。

## .NET

InputCue 便携版以 self-contained 方式携带 Windows x64 的 .NET 运行时文件。发行包中的 `licenses/DOTNET-LICENSE.txt` 与 `licenses/DOTNET-THIRD-PARTY-NOTICES.txt` 来自用于构建该版本的 .NET SDK。

- 项目与许可证信息：<https://github.com/dotnet/core/blob/main/license-information.md>
- Windows 产品发行的许可说明：<https://github.com/dotnet/core/blob/main/license-information-windows.md>
- .NET 资产许可模型：<https://github.com/dotnet/runtime/blob/main/docs/project/licensing-assets.md>

## Microsoft.Windows.CsWin32

InputCue 使用 Microsoft.Windows.CsWin32 在构建时生成 Windows API 绑定。该项目采用 MIT License：

<https://github.com/microsoft/CsWin32>

## 开发与测试依赖

测试项目使用 xUnit、Microsoft.NET.Test.Sdk、coverlet.collector 与 xunit.runner.visualstudio。这些工具不随 InputCue 便携版分发；其许可证与归属信息可在各自的 NuGet 包和上游仓库中查看。
