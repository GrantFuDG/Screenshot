# 截图管家 · ScreenshotManager

一个轻量的 Windows 截图 / 贴图工具：单进程托盘常驻，热键唤出覆盖层，截完可以直接标注、复制、保存，也可以把图片钉在桌面上。
基于 WPF + .NET 8。

**简体中文** | [English](README.en.md)

## 功能

- **区域截图**：`F1` 打开覆盖层，左键拖拽选区，支持跨多个显示器；
  不拖动直接**单击** = 选中鼠标下的窗口，**双击**选区内 = 确认。
- **全屏截图**：`Ctrl+F1`，截取鼠标所在显示器并按设置保存 / 复制。
- **悬停取窗**：鼠标停在哪，哪个窗口高亮（自动忽略桌面、任务栏与透明浮层）。
- **标注**：矩形、椭圆、直线、箭头、画笔、荧光笔、文字、马赛克、模糊、橡皮；颜色与粗细可调，支持撤销 / 重做。
- **取色放大镜**：截取时显示光标处的放大像素与颜色，滚轮调倍率（1.5×–12×）。
- **输出**：复制到剪贴板、保存为 png / jpg / bmp（文件名模板可配）。
- **贴图**：`F3` 把剪贴板图片钉在桌面上（可缩放、调透明度）；`Shift+F3` 直接把剪贴板图片粘贴到当前窗口。
- **截图历史**：`Ctrl+Shift+H`，缩略图列表，可复制 / 贴图 / 打开 / 定位；条数与保存天数可限制。
- **托盘常驻**：热键、保存目录、历史策略、开机自启、DPI 与提示等都在设置里改，保存后立即生效（无需重启）。

## 快捷键

| 按键 | 作用 |
| --- | --- |
| `F1` | 区域截图 |
| `Ctrl+F1` | 全屏截图（当前显示器） |
| `F3` | 贴图：把剪贴板图片钉到桌面 |
| `Shift+F3` | 把剪贴板图片粘贴到当前窗口 |
| `Ctrl+Shift+H` | 截图历史 |
| 拖拽 / 单击 / 双击 | 拉选区 / 选中鼠标下的窗口 / 确认当前选区 |
| `Shift` + 拖拽 | 正方形选区 |
| 方向键 | 微调选区位置 |
| `Enter` | 确认（按设置复制并保存） |
| `Esc` | 依次：放弃正在画的标注 → 清空选区 → 退出截图 |
| 右键 | **一次**退出截图（正在输入文字时先结束输入） |
| `Ctrl+C` / `Ctrl+S` / `Ctrl+T` | 复制 / 保存 / 贴图 |
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+A` | 撤销 / 重做 / 全选 |
| `R` `E` `L` `A` `P` `H` `T` `M` `B` `X` | 切换标注工具（矩形 / 椭圆 / 直线 / 箭头 / 画笔 / 荧光笔 / 文字 / 马赛克 / 模糊 / 橡皮） |
| 滚轮 | 调整放大镜倍率 |

以上全局热键都可以在「设置 → 热键」里改。

## 运行要求

- Windows 10 / 11（x64）
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)（`Microsoft.WindowsDesktop.App 8.0`）
- 构建需要 .NET 8 SDK

## 构建与运行

```powershell
# 调试构建
dotnet build "src\ScreenshotManager\ScreenshotManager.csproj" -c Debug

# 发布（框架依赖，目标机器需装 .NET 8 Desktop Runtime）
dotnet publish "src\ScreenshotManager\ScreenshotManager.csproj" -c Release -o dist

# 发布（自包含，目标机器无需安装 .NET）
dotnet publish "src\ScreenshotManager\ScreenshotManager.csproj" -c Release -r win-x64 --self-contained true -o dist
```

运行 `dist\ScreenshotManager.exe`：程序是托盘程序，**没有主窗口**，启动后请在任务栏右下角找它的图标。

> 如果程序运行在写入受限的环境里（例如 `%APPDATA%` 不可写），它会自动回退成绿色模式，数据放在程序目录下的 `data\`。

## 数据与隐私

- **所有处理都在本机完成：程序不联网，也不会上传任何内容。**
- 配置、历史、缩略图、日志默认放在 `%APPDATA%\ScreenshotManager\`：
  `settings.json`、`history.json`、`thumbs\`、`screenshotmanager.log`。
- 如果该目录不可写，会自动回退到程序目录下的 `data\`（绿色模式），日志里会写一行说明。
- 想彻底清干净：删掉上面那个目录（或程序目录下的 `data\`）即可。

## 项目结构

```
src/ScreenshotManager/
├─ Program.cs                  入口：全局异常兜底、启动日志、自检开关
├─ AppHost.cs                  接线：托盘 / 热键 / 截图流程 / 保存 / 历史 / 设置窗口
├─ ScreenshotManager.csproj
├─ app.manifest                以调用者权限运行、PerMonitorV2 DPI 感知
├─ Models/                     AppSettings、ScreenshotItem、Annotations（几何与标注模型）
├─ Services/                   屏幕捕获、剪贴板、图像合成、历史、热键、图标、设置、开机自启、托盘、日志
├─ Interop/                    Win32 P/Invoke：窗口枚举、DPI、热键、剪贴板、光标等
└─ Views/                      截图覆盖层、标注工具栏、贴图窗口、历史窗口、设置窗口
```

## 已知限制

- 仅支持 Windows（WPF + Win32）。
- 少数「隐形浮层」（游戏、录屏、直播或显卡驱动的覆盖层）可能干扰悬停取窗；程序已过滤透明穿透窗口、桌面窗口与铺满屏幕的窗口。
- 多显示器混合 DPI 按 PerMonitorV2 处理，极端组合下坐标可能仍有偏差。
- 全局热键依赖系统 `RegisterHotKey`，被别的程序占用时注册会失败（日志与设置界面会提示）。

## 作者与协作

- **需求、设计取舍与验收**：GrantFuDG
- **全部源代码**：由 DeepSeek Harness 中的 AI 助手（模型 `deepseek-v4.1-flash`）按需求编写、调试与修复，并在真机上逐项验证后交由 GrantFuDG 验收

提交信息里用 `Co-authored-by` 标注了这位 AI 协作者。GitHub 只有在邮箱已绑定账号时才会显示头像与主页链接，而这个地址没有注册账号，所以它会以纯文本形式出现。

## 声明

- 本项目是个人自用 / 学习性质的开源项目，按「原样」提供，**不附带任何明示或暗示的担保**。因使用本软件造成的任何直接或间接损失（数据丢失、误操作、隐私泄露等）由使用者自行承担。
- 本软件不会主动上传任何数据；但**截图内容本身可能包含隐私或敏感信息**，保存到哪里、是否分享、如何传播，均由使用者自行负责。
- 界面图标全部由代码绘制（见 `Services/IconFactory.cs`），使用系统字体，仓库中不包含任何第三方素材文件；文中若出现其它软件名称，仅用于说明或比较，相关名称与商标归各自所有者。

## 许可

[MIT](LICENSE)