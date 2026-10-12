# Codex Windows 额度监控

独立 C# / WPF 托盘工具，为 Windows Codex 桌面客户端显示账户真实剩余额度。
默认以**右侧使用量卡片**显示，保留输入区上方单行栏作为回退；不修改 Codex 安装文件、不注入页面，也不调整 Codex 窗口。

## 运行

1. 解压 `artifacts/CodexUsageMonitor-1.0.0-windows-x64.zip`。
2. 保持 Codex 已通过 ChatGPT 账户登录；API Key 模式不能提供订阅额度。
3. 双击 `CodexUsageMonitor.exe`。图标可能在 Windows 托盘的隐藏图标区域。
4. 打开 Codex 右侧面板；Codex 在前台且面板内有安全空白时，底部显示使用量卡片。
5. 双击托盘图标查看真实额度和定位状态；右键可刷新、校准、退出。
6. 也可以运行 `CodexUsageMonitor.exe --show-status`，启动即打开状态窗口。

要求 Windows 10 1607+ / Windows 11 x64、.NET Framework 4.8。无需 .NET SDK、Python、Node 或管理员权限。
发布 EXE 未签名；运行目录中的 SHA256SUMS.txt 用于校验。自动启动默认关闭，可从托盘开启。

## 跟随 Codex 启动

首次解压到固定目录并运行后，在托盘右键菜单勾选 **“登录 Windows 后待命，跟随 Codex 启动”** 即可。取消勾选则移除自动启动登记，无需管理员权限。

Windows 下次登录时工具会在托盘后台待命，不弹出状态页；检测到 Codex 窗口后才启动额度查询。不修改 Codex 快捷方式或安装文件。Codex 尚未启动时不启动 App Server。开始监控后仍保持原来的 60 秒刷新与托盘行为，退出 Codex 不会退出工具。

工具不能收到 Codex 提供的启动回调，因此采用登录后后台检测。若从托盘“退出”工具，本次登录期间就不会自动跟随；再次运行工具或下次登录 Windows 即可恢复。Windows 任务管理器禁用启动项也会阻止它自动运行。

不要移动已登记的运行文件夹；搬迁后请从新目录运行，再重新勾选以更新路径。ZIP 不包含任何人的自动启动登记：其他人需要在自己的电脑上开启一次。

命令行入口：`--enable-autostart` 登记当前 EXE，`--disable-autostart` 取消登记，`--background` 在没有 Codex 时后台待命。前两个命令成功返回 0，登记失败返回 1。仅修改当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `CodexUsageMonitor` 值。

### .NET Framework 4.8 是什么

这是 Windows 上运行本程序所需的基础运行组件，类似程序需要的运行库，不是开发环境。无需安装 .NET SDK、Python 或 Node。较新的 Windows 10 和 Windows 11 通常已有兼容组件（4.8 或 4.8.1）。若 Windows 提示缺少 .NET Framework，请通过微软官方安装说明补齐，再运行程序：https://learn.microsoft.com/zh-cn/dotnet/framework/install/ 。

## 额度显示

- 所有百分比均为**剩余**；接口返回已用百分比，按 `clamp(100-usedPercent,0,100)` 转换。
- 依据 300 / 10080 分钟窗口识别 5 小时 / 周额度，而非固定假定 primary/secondary 顺序。
- 正常每 60 秒读取一次，手动刷新与自动刷新合并，长期复用一个隐藏的 Codex App Server。
- 剩余不足 20% 时对应百分比及 4 DIP 进度条变红；正常 5h 为绿色，周为橙色。
- 重置时间来自接口 Unix 秒，转换为 Windows 当前本地时区，格式 MM-dd HH:mm。
- 空值、缺失或不支持的窗口显示“暂无数据”，不以零代替。
- 查询失败保留最后成功数据并标记“已过期”；超过 90 秒没有成功读取或重置时刻已过也标记过期。
- 自动重试间隔仍为 60 秒；退出的子进程在下一次刷新时重新启动。连续相同额度不会触发重启。
- 额度仅保存在当前进程内存，退出后清空。不会根据聊天次数估算或自行推算重置后的额度。

## 右侧使用量卡片（默认）

- 外观参考 Codex 原生“使用量”：标题 20 DIP，额度标签 15 DIP，辅助文字 13 DIP；浅灰底、细边框、圆角分组、深色进度条，支持深色主题。
- 分别显示 5 小时 / 周剩余额度、接口提供的本地重置时间；低于 20% 时变红。重置卡显示“可用 × 张”，点击可展开逐张有效期；**没有兑换按钮，不会消耗重置卡**。
- 数量读取 `rateLimitResetCredits.availableCount`。0 明确显示“可用 0 张”，字段缺失显示“暂无数据”，不按明细列表长度推算。失败保留最后成功快照并标记过期。
- 卡片宽 300–480 DIP，跟随面板宽度；高度由文字内容测量，窄区域调整行内排列。关闭按钮只隐藏卡片，后台继续刷新；托盘“显示使用量卡片”恢复。
- 自动识别独立的右侧 Group / Pane 边界，确认其位于完整输入区或已识别输入控件的右侧；卡片优先位于面板底部，边距约 12 DIP。遇到原生内容时只在同一面板内寻找空白，没有安全位置就隐藏，不覆盖来源、输出内容、滚动条或输入区。
- 未识别面板时先打开侧栏。托盘“右侧区域框选 / 校准”可拖动框选右侧区域：松开保存，Esc 取消。保存相对客户区右 / 下边距及区域宽 / 高，单位 DIP；面板收起或区域不再属于面板时隐藏。校准不能创造空白，也不会关闭避让检查。
- 托盘“显示原单行栏”可回退；“位置与外观设置”可选择显示模式、主题和 CLI。旧配置未写 DisplayMode 时自动采用右侧卡片，原有偏移保留，仅用于原单行栏。
- 只框选独立右侧面板内的区域，不框选聊天区。本机结构不提供可识别面板边界时会保持隐藏，不能保证所有 Codex 版本均支持自动定位。

## 原单行栏与配套窗口

采用克制原生风格，悬浮栏、状态窗口、校准窗口统一支持浅色 / 深色主题。

- 额度栏保持 840×42 DIP、单行排列、4 DIP 进度条；圆角 10 DIP，左右内边距 10 DIP。
- 标题 13 DIP，标签、重置时间及状态 12.5 DIP，剩余百分比 15 DIP 半粗体；字体为 Segoe UI / 微软雅黑 UI。
- 柔和绿色 / 橙色区分两种额度，低于 20% 变红；暂无数据使用中性灰。刷新按钮有悬停 / 按下反馈，点击继续不抢夺输入焦点。
- 状态窗口约 910×400 DIP，保留额度栏，下方分为“数据状态”和“定位状态”，可查看最后更新时间、错误及校准入口。
- 校准窗口约 520×540 DIP，保留原生标题栏；正文 13 DIP、区域标题 15 DIP，输入框 / 按钮约 34 DIP 高。长文本可滚动，取消或 Escape 关闭不保存；下拉选项显示中文，配置仍使用原来的英文枚举值。

## 原单行栏定位与配置

优先读取 Windows UI Automation 的结构边界，识别编辑框及其完整父容器，优先将额度栏底部放在输入区顶部上方 4 DIP。若该位置与文字或按钮相交，最多向上寻找 160 DIP 内最近的安全位置，并与障碍保持 4 DIP 间距；不忽略任何已识别的占用边界。
移动时跟随，缩放、DPI 或内容布局变化后重新检测；不是固定截图坐标。
扫描不读取聊天正文、输入值或控件文本。可见文字、图像和交互控件的边界用于遮挡检查。前台切换通过非注入 WinEvent 回调统一重新判断显示或隐藏，并保留定时检查。

配置位于 EXE 旁的 settings.json：

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| CodexExecutable | 空 | 完整 codex.exe 路径；空白自动发现桌面附带 CLI 或 PATH 下原生 CLI |
| PositionMode | auto | auto / relative |
| RelativeCalibrated | false | 用户确认相对位置后开启 |
| RelativeX | 0 | 相对 Codex 客户区中心的横向偏移，DIP，向右为正 |
| RelativeBottom | 150 | 额度栏底部距 Codex 客户区底部距离，DIP |
| Theme | auto | auto 跟随 Windows 应用主题；light / dark 可手动对齐 Codex |
| DisplayMode | sidebarCard | sidebarCard 右侧卡片；compactBar 原单行栏 |
| SidebarRegion | null | 框选区域，Right / Bottom / Width / Height 为相对客户区右、下边距和宽、高，单位 DIP |

通过托盘“位置与外观设置”编辑单行栏偏移并保存。若检测成功，校准对话框会预填当前自动锚点。
只修改偏移不能创造空白；相对定位也不会关闭输入区和可见内容的避让检查。
Codex 失去前台或最小化、布局未确定、窗口过窄或发生重叠时隐藏额度栏，托盘继续读取并显示数据。
不会将栏移动到其他应用上方。设置和额度状态窗口是用户主动打开的独立窗口。

## 监控窗口最小化与关闭

这里的“监控窗口”指工具自己的状态页，和 Codex 主窗口是两个独立窗口。

- 最小化或关闭监控状态页后，工具继续在托盘后台运行，额度读取、60 秒自动刷新和窗口跟随均不中断。
- Codex 在前台且当前显示模式对应区域有安全空间时，卡片或单行栏显示；状态页是否最小化不会成为隐藏条件。窗口状态及前台变化会触发统一检查，并按需后台重扫；有效布局就绪后恢复显示。
- 不主动切回 Codex，也不抢夺输入焦点。Windows 最小化状态页后如果切到其他应用，请自行点击回 Codex。
- Codex 本身最小化、其他应用在前台、布局过期、窗口太窄或发生重叠时，悬浮栏仍安全隐藏，不移到桌面其他位置。
- 状态页分别显示“监控状态页在前台”“Codex 不在前台”“Codex 已最小化”等当前原因，同时显示最近一次 Codex 前台时的定位结果及本地时间。历史只保存在内存，不能用于绕过当前布局检查；切换目标窗口或应用新配置时清除不适用的历史。
- 双击托盘图标或选择“查看额度与定位状态”会将已最小化的状态页恢复为正常窗口。只有托盘菜单“退出”结束后台监控并释放自己的 App Server 子进程。

## 已知限制

- Codex 可访问性树及 App Server 协议可能随版本变化；树不可读时隐藏并提示，而非声称精确定位。
- 输入区**上方**如果有聊天文字或按钮，会先在附近向上避让；附近没有足够安全空白才隐藏。相对校准保持指定位置，不自动移动；请查看托盘定位状态。当前布局未预留可用空白时，工具不能强制增加原生空白。
- 前期检查曾发现输入区下方仅约 23px，因此已按最终要求改为上方；该数值仅是一次检查结果，不用于运行时定位。
- 自动发现多个 Codex 窗口时优先前台窗口；首次切换窗口可等待约 0.8 秒重新扫描。
- DPI 几何及测试窗口覆盖已验证，实际 Codex 的跨显示器缩放、快捷键、点击发送等需结合当前客户端进行人工验收。
- 按 Windows 应用主题匹配外观；Codex 使用独立主题时请手动选择 light / dark。
- CLI 自行管理登录、认证与令牌续期。监控程序不读取、记录或输出认证令牌或认证文件。无登录时请在 Codex 内完成登录后刷新。

## 构建和测试

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Package
.\artifacts\tests\MonitorTests.exe
.\artifacts\tests\MonitorTests.exe --live
.\artifacts\tests\MonitorTests.exe --ui
.\artifacts\tests\MonitorTests.exe --ui --ui-live
.\artifacts\tests\MonitorTests.exe --lifecycle
.\artifacts\tests\MonitorTests.exe --startup
.\artifacts\tests\MonitorTests.exe --lifecycle-live
.\artifacts\tests\MonitorTests.exe --sidebar-ui --sidebar-live
.\artifacts\tests\MonitorTests.exe --sidebar-lifecycle --sidebar-live
```

构建前请从托盘退出已运行程序，避免 EXE 文件被占用。已编译后可单独运行 scripts/package.ps1 打包。
使用 Windows 已安装的 Framework64 csc.exe；所有源码位于 src/，不依赖网络或第三方 NuGet 包。
测试数据仅用于 tests/ 中的解析和安全定位测试，不进入运行程序。
`--live` 调用真实账号，验证启动、手动刷新、两个 60 秒自动周期及**本测试创建的** App Server 子进程终止后恢复；不终止 Codex 客户端。
测试会短暂打开/移动/缩放自己的测试窗口，结束后尝试恢复之前的前台窗口。
真实额度测试同时输出浅/深色栏的 PNG 供布局检查；不输出认证信息。
`--ui` 使用明确的测试数据检查 2 种主题 × 6 种状态 × 100% / 125% / 150% 的渲染，另检查配套窗口、长文本、中文选项及键盘操作；图片与结果写入 artifacts/tests/ui。`--ui-live` 额外读取真实账户生成预览。DPI 离屏渲染和测试窗口不等同于实际 Codex 跨显示器验收。

`--lifecycle` 用受控窗口和 RPC 测试服务运行完整监控应用，检查状态页最小化 / 关闭 / 托盘恢复 / 手动刷新 / 显式退出。`--lifecycle-live` 换用真实额度接口，并在状态页保持最小化时验证一次真实 60 秒自动刷新。报告分别写入 artifacts/tests/lifecycle-results.txt 和 lifecycle-live-results.txt。
Windows 若拒绝测试窗口取得前台，依赖真实前台切换的场景明确记录为跳过；不将这些场景计作通过。

`--sidebar-ui` 覆盖重置卡数量、校准配置兼容、右侧区域几何，以及浅/深色 × 8 状态 × 3 宽度 × 3 DPI 的卡片渲染；`--sidebar-live` 额外读取真实账户生成预览。`--sidebar-lifecycle` 检查完整应用中的关闭/恢复、侧栏收起/占用、非激活显示及原单行栏回退；加 `--sidebar-live` 验证真实 60 秒自动刷新。拒绝前台激活时记录跳过，不能当作实机通过。

分享 ZIP 只使用 config/settings.default.json 的干净默认配置，打包不覆盖本机运行配置；重置卡和额度不写入文件。

## 实现和许可

额度接口文档：docs/appserver-api.md。验证记录：docs/validation.md。
参考 D1NOOO/codex-usage-monitor 的 MIT 项目，具体比较和许可证见 THIRD-PARTY-NOTICES.md。
本项目独立实现 WPF 右侧卡片与输入区上方避让；登录待命是本地可关闭的开关，不复用参考项目的认证令牌读取、额度兑换或更新安装逻辑。

右侧识别兼容：部分布局没有完整面板容器，工具会依据至少三个对齐的右侧结构分组推定侧栏列，并继续执行输入区分离及内容遮挡检查。无法确认时仍隐藏。

点击“使用限额重置”可展开逐张卡的发放和到期时间（Windows 本地时间）；列表只读，不兑换。接口未提供明细时明确提示，展开空间不足时按原安全规则隐藏。
