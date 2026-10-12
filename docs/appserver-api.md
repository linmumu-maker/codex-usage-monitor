# App Server 额度读取合同

依据：https://learn.chatgpt.com/docs/app-server
参考：https://github.com/D1NOOO/codex-usage-monitor
验证 CLI：0.162.0-alpha.2（2026-10-08）；卡片版在 0.162.0-alpha.17.2（2026-10-12）核对本机生成协议和真实读取。后续版本应重新验证。

## 传输和初始化

由工具创建并拥有隐藏子进程：
`codex.exe app-server --listen stdio:// -c analytics.enabled=false`。

stdin/stdout 使用 UTF-8 换行 JSON。Framework 标准输入若已发出 BOM，会先用独立行隔离此前导，再发送正常 JSON；后续写入使用 UTF-8 无 BOM。递增整数 id 匹配响应：
1. initialize，params.clientInfo 包含 name/title/version。
2. 等待 result，发送无 id 的 initialized 通知。
3. account/rateLimits/read，无 params。
4. 单个请求等待 15 秒；传输失败关闭自己创建的进程，后续刷新重新初始化。
5. stderr 持续排空并丢弃，不保存原始日志；stdout 仅在内存解析，未知响应忽略。

认证由 Codex CLI 管理；监控不读取凭据文件、不提供认证令牌、不发起登录/退出、额度兑换、消息发送或推理。

## 返回值到 UI 的映射

- result.rateLimitsByLimitId.codex 优先；缺少该项时兼容 result.rateLimits，legacy limitId 不得为其他 bucket。
- primary/secondary 中 windowDurationMins == 300 对应 5 小时，== 10080 对应周额度；其他时长不强行命名。
- usedPercent 是已使用量。显示 remaining = clamp(100-usedPercent, 0, 100)，缺失/非数值显示暂无数据。
- resetsAt 是 Unix 秒；有效范围内按 Windows 本地时区转换。缺失、负值、非整数或越界显示暂无数据。
- 成功响应缺少窗口时清除旧窗口显示；查询失败才保留最后成功快照并标记过期。
- 不显示模型独立额度、credits 余额、账号字段等其他返回信息。重置卡数量独立读取以下字段。

## 可用重置卡（只读）

`result.rateLimitResetCredits.availableCount` 表示可用重置卡数量，映射到内存快照 ResetCards 并显示“可用 N 张”。非负整数有效；缺失、null、负值、分数、字符串或非有限数字显示“暂无数据”。数字类型浮点值只接受能精确表示的整数范围。0 与缺失不同。

不使用 `credits` 明细长度推算数量，明细可能被截断。本工具没有兑换按钮，也不调用 account/rateLimitResetCredit/consume 或私有后端接口。数量和两种额度共用成功/失败/过期及账户切换规则；没有单独保存账户资料。

## 配置合同新增

- DisplayMode：sidebarCard（默认）或 compactBar。旧配置字段缺失时默认使用右侧卡片，保留原有定位字段。
- SidebarRegion：null 表示自动识别；对象字段 Right、Bottom、Width、Height 是相对 Codex 客户区右/下边距及区域宽高，单位 DIP。数值须有限且非负，最大 8192，区域至少 324 DIP 宽、250 DIP 高；实际可用高度还须容纳测量后的卡片和边距。
- 校准区域只在当前可识别右侧面板中有效。尺寸、占用、前台或面板边界变化时重新验证，不使用旧布局绕过隐藏条件。

## 通知、并发和状态

account/rateLimits/updated 触发一次合并后的真实重读，不将不完整通知误当完整快照。
account/updated 清空原快照并触发重读；账户切换前发出的响应通过 epoch 检查丢弃。
每 60 秒自动查询；进行中的刷新会合并新的刷新操作，避免重叠查询。
状态有：正在连接、自动刷新、刷新中、查询失败、已过期。过期时保留真实历史数据，工具提示给出最后成功时间。
快照超过 90 秒或返回的重置时间已到也显示过期；不伪造窗口重置结果。

## 对外合同影响

不新增 HTTP 服务，不修改 Codex 原接口。此文档描述消费外部接口及本工具展示语义。

## 只读逐张有效期

点击“使用限额重置”展开 result.rateLimitResetCredits.credits 中 status=available 的明细。grantedAt 是 Unix 秒发放时间，expiresAt 是 Unix 秒到期时间，均按 Windows 本地时间显示。expiresAt 显式 null 表示没有到期时间；字段缺失、非法值或超出可表示日期范围显示“到期时间暂无数据”。卡名统一按 resetType 显示中文：codexRateLimits 为“完全重置（每周 + 5 小时）”，其他类型为“使用限额重置卡”；不直接显示后端 title，不使用或保存 id。

credits 为 null 或缺失时显示“接口暂未提供逐张有效期”；空数组区别于缺失。明细可能由后端截断，只展示实际返回的可用卡，数量继续使用 availableCount。界面最多呈现 200 张，列表可滚动；未提供的卡不补造时间。明细沿用失败保留并过期标记和账户切换清除规则。展开后重新测量卡片，安全空间不足时仍隐藏。没有消耗重置卡的请求。