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

## Edge 基线

`edge-basic.json` 来自真实 Edge 前台窗口的分步人工采集，保留四个最小观察：

1. 普通网页输入框光标；
2. 输入框内部选区；
3. 未预先点击输入框时的静态正文选区；
4. 普通单选按钮焦点。

另一次独立采集确认了 `EditableSelection → EditableCaret → NoEditableFocus(Document) → ReadOnlySelection(Document)` 的完整转换。原始采集只保存在 `.local/`，黄金文件不包含输入内容和真实机器字段。

采集同时发现 Edge 的 `RadioButton` 可能报告 `IsReadOnly=false` 和残留 MSAA Caret。该证据不能建立文本编辑资格；回归用例要求它保持 `NoEditableFocus`。

## Chrome 基线

`chrome-basic.json` 使用与 Edge 相同的独立人工步骤采集，覆盖输入框光标、输入框选区、静态正文选区和单选按钮。额外的转换采集确认 `EditableCaret → NoEditableFocus(Document) → ReadOnlySelection(Document)`。Chrome 与 Edge 共用通用策略，但保留各自 Trace，避免只在一个 Chromium 宿主上通过测试。

## Edge / Chrome 人工采集

浏览器自动化可以改变 DOM，但不保证把 Windows 前台焦点交给真实浏览器窗口，因此不能用自动化点击生成兼容性结论。采集时运行：

```powershell
dotnet run --project src/InputCue.App/InputCue.App.csproj -c Release -- --capture-trace .local/browser-capture.json
```

命令启动后有 20 秒操作时间。人工依次点击网页输入框、在输入框内建立选区、拖选静态正文、点击单选按钮。完成后先检查 Trace 的 `ProcessName` 确实属于目标浏览器，再选择最小观察集规范化进入本目录。未经确认的采集文件留在 `.local/`，不得作为黄金 Trace 提交。
