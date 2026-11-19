# 继续开发 / Contributing

本仓库是基于 **wushuai0802/MyFinder Device Battery** 的二次开发分支。欢迎继续完善，但请先读 [CREDITS.md](CREDITS.md) 和 [LICENSE](LICENSE)，保留原作者署名与 AGPL-3.0-only 许可证。

## 1. 先了解边界

- 上游项目：https://github.com/wushuai0802/myfinder-device-battery
- 上游版本：`1.3.11-beta.14`
- 本分支只新增硬件控制、宏、驱动入口和便携打包；原有电量读取/设备协议尽量不改。
- 新增硬件命令必须先做只读探测，确认 VID/PID、报告描述符、读写集合，再实机验证；不要向未确认设备盲目写。
- 所有功能默认普通用户权限，不引入 `runas` / `requireAdministrator`。
- 不要把个人绝对路径、个人宏、缓存或机器专属配置提交进仓库。

## 2. 代码地图

| 功能 | 主要文件 |
| --- | --- |
| 设备卡片右键菜单 | `scripts/DeviceControlMenu.cs` |
| 托盘菜单 / 单实例 | `scripts/TrayApp.cs` |
| 外设驱动目录 | `scripts/PeripheralCatalog.cs` |
| 驱动中心窗口 | `scripts/PeripheralHubForm.cs` |
| 鼠标 DPI / 回报率 | `scripts/DpiEditorForm.cs`、`scripts/RapooHid.cs`、`engine/providers/rapoo.py` |
| 鼠标宏 / 按键替换 | `scripts/MouseMacros.cs`、`scripts/MacroManagerForm.cs` |
| 开机自启 | `scripts/StartupManager.cs`、`scripts/SettingsForm.cs` |
| 耳机 7.1 / 变声 / 降噪 | `scripts/SpatialAudio.cs`、`scripts/AudioControl.cs`、`scripts/VoiceEffect.cs`、`engine/server.py` |
| 引擎与设备协议 | `engine/server.py`、`engine/providers/` |
| 便携运行时构建 | `scripts/Build-Backend.ps1`、`engine/runtime/` |

## 3. 构建

在源码根目录依次运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Build-Backend.ps1 -OutputDirectory .\engine\runtime
```

- `Build.ps1` 默认输出到 `outputs\`。
- `Build-Backend.ps1` 按 `engine\dependencies.lock.json` 准备便携 Python 与设备读取组件。
- 发行目录需要同时保留 `app\` 和 `engine\`；只复制 exe 会丢失设备读取能力。
- 构建/打包细节也可参考上游 README 的“构建与发布”章节。

## 4. 测试

没有实机硬件时，先跑自检：

```powershell
# 驱动中心 34 项目录识别
.\app\AtkMouseBattery.exe --peripheral-selftest .\peripheral-selftest.txt

# 驱动中心界面离屏渲染（输出 PNG + 摘要）
.\app\AtkMouseBattery.exe --peripheral-ui-selftest .\peripheral-hub.png .\peripheral-hub.txt

# 雷柏 DPI / 回报率探测与读回
.\app\AtkMouseBattery.exe --rapoo-selftest .\rapoo-selftest.txt
```

引擎单元测试：

```powershell
.\engine\runtime\python.exe -I -B -m unittest discover -s tests -p "test_*.py"
.\engine\runtime\python.exe -I -B -m unittest discover -s engine\tests
```

提交 PR 时请附上：

- 改动说明与影响范围
- 自检/单元测试输出
- 实机验证的鼠标/耳机/键盘型号、VID/PID、连接方式（有线/2.4G/蓝牙）
- 如果改了 HID 写入，附写入前后的读回值

## 5. 代码规范

- 不新增管理员权限依赖。
- 不把厂商驱动打包进本仓库，只做“发现并启动”的入口。
- 不向未知设备发送写命令；探测失败就保持只读或禁用。
- 新增 UI 文案使用中文，路径用环境变量/相对路径，不写死个人目录。
- 提交信息写清楚“新增/修复/影响范围”，不要混入个人配置或构建产物。
- 修改后至少跑一遍 `--peripheral-ui-selftest`，确认 34 项目录和界面仍可用。

## 6. 许可证与署名

- 本分支和衍生作品继续使用 **AGPL-3.0-only**。
- 保留原作者 **wushuai0802** 的署名、上游链接、LICENSE、THIRD_PARTY_NOTICES。
- 如果通过网络向用户提供修改后的程序，按 AGPL 向这些用户提供对应源码。
- 不要以原作者或上游官方名义发布衍生版本。
- 本软件按现状提供，不提供担保。
