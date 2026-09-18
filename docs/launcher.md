# Optional Windows repair launcher / 可选 Windows 修复启动器

Antigravity updates can remove `version.dll` and `config.json`. This launcher restores
missing files immediately before starting the application, then exits. It does not
install a service, watcher, scheduled task, proxy server or updater, and does not download anything.

## 使用方法

1. 将完整 IDE 发布包解压到 **Antigravity 安装目录之外**的固定目录，并保留整个目录。
2. 首次使用前配置发布包的 `ide/config.json`。已有代理用户可将正在使用的配置复制到这里，作为恢复副本。
3. 双击包根目录的 `AntigravityProxyLauncher.exe`。它查找当前用户和 Program Files 中的 Antigravity / Antigravity IDE；没有唯一结果时弹出文件选择框。
4. 可以为启动器创建快捷方式。多个安装或自定义目录可在快捷方式参数中指定：

```text
--target "D:\Apps\Antigravity\Antigravity.exe"
```

原来的手动 DLL 部署仍可使用。启动器无需管理员权限运行，但必须有目标目录写入权限；Program Files 安装可能需要手动以管理员运行。工具不会自动提权。

## English quick start

Extract the complete IDE release to a permanent directory **outside the application installation**.
Configure `ide/config.json` before first use, or copy your existing configuration there as the recovery copy.
Run `AntigravityProxyLauncher.exe`. It detects common per-user and Program Files installations;
if detection is ambiguous, select the executable or specify `--target` in a shortcut.

```text
AntigravityProxyLauncher.exe --target "D:\Apps\Antigravity\Antigravity.exe" -- "D:\My project" --new-window
AntigravityProxyLauncher.exe --target "D:\Apps\Antigravity\Antigravity.exe" --repair-only --quiet
```

Arguments following `--` are passed unchanged to the application. The caller's working directory
is preserved, including for relative project paths. `--repair-only` repairs without launching;
`--quiet` suppresses dialogs. Exit codes: 0 success, 1 error, 2 selection cancelled.

## Behavior and limitations / 行为与限制

- Only missing files are restored. Existing DLLs and configuration are never overwritten,
  even when different from the package. This is not a proxy-version upgrader.
- `ide/config.json` is an editable recovery copy, not a synchronized backup. Later changes in the
  installed configuration should also be copied here if you want them restored after an update.
- The bundled DLL checksum detects accidental corruption; it is not a publisher signature.
  The DLL and application must have matching x86/x64 PE machine types.
- Required sources are validated before writing. Each missing file is copied to a temporary file,
  verified, then moved into place without overwriting. A failed repair never starts the application.
  Repairing the pair is not transactional; a subsequent run completes any remaining missing file.
- Save and exit Antigravity before repairing missing files. The launcher refuses repair when the
  target process is running (or a same-name process cannot be inspected); it never kills processes.
- An independently started updater/application can still race the launcher. Finish the update,
  exit the application and retry if this happens. There is no background monitoring.
- Update-triggered restarts, original shortcuts, CLI launches and browser callbacks can bypass
  the launcher. After an update, close the application and use this launcher if the proxy is absent.
- The launcher itself exits immediately after starting the application. The proxy DLL runs inside
  Antigravity as before; your existing upstream proxy client must still be running.
- No registry, startup items or existing shortcuts are modified. Remove your launcher shortcut and
  extracted package to uninstall the launcher. Already deployed proxy files remain installed.

## Build and tests

The Windows build script compiles the launcher with the Windows .NET Framework C# compiler
(no SDK download). It is included only in IDE packages; CLI packaging is unchanged.

```powershell
.\scripts\build-launcher.ps1 -OutputDir .\output
.\scripts\test-launcher.ps1
```

`build.ps1 -RunTests` also runs the launcher regression tests. Tests use temporary fixtures,
not a real Antigravity installation, and compile their own mock application.
