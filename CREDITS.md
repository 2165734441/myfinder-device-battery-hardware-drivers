# 来源与致谢 / Credits

## 1. 上游项目与原作者

本仓库不是原创项目，而是基于以下上游项目的二次开发分支：

| 项目 | 信息 |
| --- | --- |
| 项目名 | **MyFinder Device Battery**（MyFinder 设备电量卡片） |
| 原作者 / 上游维护者 | GitHub **[@wushuai0802](https://github.com/wushuai0802)** |
| 上游仓库 | https://github.com/wushuai0802/myfinder-device-battery |
| 上游版本 | `1.3.11-beta.14` |
| 上游许可证 | **AGPL-3.0-only** |
| 本分支仓库 | https://github.com/2165734441/myfinder-device-battery-hardware-drivers |
| 本分支维护者 | GitHub **[@2165734441](https://github.com/2165734441)** |

原项目的电量卡片、设备读取、托盘界面、更新检查、设置与大部分设备协议解析均来自上游；原项目版权归原作者 **wushuai0802** 及其贡献者所有。本仓库只是在其基础上增加了硬件控制、宏、驱动入口和便携打包。

上游 README 还说明：部分代码改编自 **HaloBattery**（MIT），相关 MIT 声明保留在 `THIRD_PARTY_NOTICES.md` 与 `engine/licenses/HaloBattery-MIT.txt`。

## 2. 本分支新增了什么

相对于上游 `1.3.11-beta.14`，本分支新增：

### 鼠标（雷柏 VT7 V2）

- 7 档 DPI 自定义编辑：每档 X / Y 可分别设置 100–30000
- 启用档数、当前档位切换
- 回报率直写：8000 / 4000 / 2000 / 1000 / 500 / 250 / 125 Hz
- 写入后读回比对，实机验证
- 不需要打开雷柏 A HUB，不需要管理员权限

### 鼠标宏 / 按键替换

- 软件内录制宏，`Ctrl+Alt+F10` 停止
- 支持鼠标侧键 1/2、中键、左右键、键盘任意键作为触发键
- 低层键盘/鼠标钩子 + `GetAsyncKeyState` 轮询兜底
- 宏保存在 `%LOCALAPPDATA%\MyFinderDeviceBattery\mouse-macros.json`
- 属于软件层按键替换，不写鼠标固件；程序运行期间生效

### 耳机

- Windows Sonic 7.1 虚拟环绕开关
- 变声器：低沉 / 原声 / 尖锐 / 卡通
- 降噪：麦克风噪声门
- 变声/降噪需要 Voicemeeter / VB-CABLE 等虚拟声卡端点

### 开机自启

- 托盘右键或设置页一键开关
- 只写当前用户启动文件夹，不需要管理员，不弹 UAC

### 外设驱动中心

- 34 个键盘 / 鼠标 / 耳机 / 手柄 / 灯光 / 主板软件入口
- 自动扫描桌面、公共桌面、开始菜单的快捷方式与本机已知安装路径
- 已安装的优先显示，可直接打开；未安装的给官网下载
- 新增你指定的 **XINMENG Driver（新盟键盘）**、**OpenRGB（ARGB 灯光）**、**北通智控（手柄）**
- 额外识别雷柏 A HUB、XIBERIA Audio Center、MCHOSE HUB、JoyToKey
- 驱动中心只是厂商软件入口，不冒充驱动，不向未验证硬件发命令

### 便携发行版

- 内置便携 Python 运行时，不需要用户安装 Python / .NET
- 解压到任意可写目录即可运行，不绑定安装路径
- 不含个人配置、鼠标宏和缓存
- 附 `先读我.txt`、`使用说明.md`、`LICENSE`、`THIRD_PARTY_NOTICES.md`

## 3. 没有改动的部分

- 原有电量读取逻辑、设备协议、更新检查与设置体系保持上游实现
- 未删除原作者版权声明、LICENSE 或第三方声明
- 未把项目改名为原作者官方版本，也未使用原作者名义发布

## 4. 继续创作 / 二次开发

本分支沿用上游的 **AGPL-3.0-only**。欢迎 fork、修改、提交 PR 或继续制作衍生版本，但请遵守：

1. 保留原作者 **wushuai0802** 的署名、上游仓库链接、LICENSE 和源码地址；
2. 衍生作品继续使用 **AGPL-3.0-only**；
3. 如果通过网络向用户提供修改后的程序，按 AGPL 要求向这些用户提供对应源码；
4. 不要以原作者或上游官方名义宣传本分支；
5. 新增硬件协议前先做只读探测和实机验证，不要向未验证设备盲目发送写命令；
6. 本软件按现状提供，不提供任何担保。

构建、代码地图和测试方法见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 5. 相关链接

- 上游项目：https://github.com/wushuai0802/myfinder-device-battery
- 本分支：https://github.com/2165734441/myfinder-device-battery-hardware-drivers
- 本分支硬件说明：[docs/hardware-drivers.md](docs/hardware-drivers.md)
- 第三方声明：[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
- 许可证：[LICENSE](LICENSE)
