# 便携发布与自动更新

InputCue 的正式发布物是绿色便携 ZIP，不使用安装器、Windows 服务或常驻更新进程。用户解压后直接运行 `InputCue.exe`；程序设置和更新状态保存在同目录的 `data` 文件夹中。

## 检查规则

- 每次启动都会触发一次检查判断。
- 自动网络检查最多每 24 小时一次；失败也会记录本次尝试，避免后台反复请求。
- 程序连续运行超过 24 小时时，用一次可取消的延时任务触发下一次检查，不做短间隔轮询。
- 点击更新按钮会立即手动检查，并刷新 24 小时计时。
- 自动或手动发现新版后都会下载、校验、退出主程序、替换文件并自动重启。

## 更新安全边界

客户端先验证 `portable-releases.json.sig` 的 ECDSA P-256 签名，再验证 ZIP 的 SHA-256 和文件长度。独立更新器只接受带 `portable.flag` 的目录，安全解压到同磁盘暂存目录，拒绝路径穿越及 `data` 目录，并在替换失败时回滚旧文件。

`data` 永远不进入发布 ZIP，也不会被更新器替换。更新过程中会使用系统临时目录，完成后尽力清理。

## 首次配置 GitHub

1. 将 `.local/update-signing-private.pem` 的完整内容保存为仓库 Actions secret：`UPDATE_SIGNING_PRIVATE_KEY_PEM`。该私钥已被 `.gitignore` 排除，不能提交或公开。
2. 确认仓库地址为 `https://github.com/huangko555/InputCue`。若仓库改名或转移，需要同步修改程序和打包脚本中的地址。
3. 推送形如 `v1.1.0` 的标签。`Release portable` 工作流会测试、发布两个单文件 EXE、生成便携 ZIP、签名清单并创建 GitHub Release。

本地打包命令：

```powershell
./scripts/package-portable.ps1 -Version 1.1.0
```

输出位于 `artifacts/release/1.1.0`。Release 资产包括便携 ZIP、签名更新清单和 `SHA256SUMS.txt`。

## 便携版的边界

便携版不安装系统组件；但启用“开机启动”时仍会按用户选择写入当前用户的 Run 注册表项。未做 Authenticode 代码签名的 EXE 也可能触发 Windows SmartScreen 提示，更新清单签名只负责确认更新来源和文件完整性，不能替代代码签名信誉。
