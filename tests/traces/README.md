# 黄金 Trace

此目录只保存已经确认目标应用和操作步骤的规范化 Trace。规范化会清除真实时间、进程 ID、屏幕坐标和耗时，同时保留进程基名、UI 框架、控件类型、证据来源及分类结果。Trace 不得包含文本、按键、窗口标题或完整路径。

## 原生 WPF 基线

`wpf-native-basic.json` 由隐藏测试宿主依次执行以下场景生成：

1. 可编辑输入框中的折叠光标；
2. 可编辑输入框中的非折叠选区；
3. 只读输入框中的非折叠选区；
4. 非编辑按钮获得焦点。

重新生成：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj -c Release -- --probe-smoke-test --trace-output tests/traces/wpf-native-basic.json
```

## Edge / Chrome 人工采集

浏览器自动化可以改变 DOM，但不保证把 Windows 前台焦点交给真实浏览器窗口，因此不能用自动化点击生成兼容性结论。采集时运行：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj -c Release -- --capture-trace .local/browser-capture.json
```

命令启动后有 20 秒操作时间。人工依次点击网页输入框、在输入框内建立选区、拖选静态正文、点击单选按钮。完成后先检查 Trace 的 `ProcessName` 确实属于目标浏览器，再选择最小观察集规范化进入本目录。未经确认的采集文件留在 `.local/`，不得作为黄金 Trace 提交。
