# Tysl.Ai Project Rules

## Product Positioning
- 本项目是地图中心型桌面监控系统，不是传统后台管理系统。
- 主界面长期保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”的工作方式，不回退成表格中心页面。
- 静默巡检替代传统任务面板；复杂操作收入弹窗、抽屉或更多菜单，不在主视图堆按钮。

## Architecture Guardrails
- 既定五层架构固定为 `App / UI / Services / Infrastructure / Core`，禁止随意改层、并层或反向依赖。
- `App` 只负责启动、组合根和依赖注入。
- `UI` 只负责视图、主题、控件、ViewModel 和交互状态，不直接写 `HttpClient`、平台签名、解密、坐标转换。
- `Services` 只放服务接口与编排占位，不承载平台接入细节。
- `Infrastructure` 负责外部系统接入占位；后续 CTYun 相关能力统一走 `Infrastructure/Integrations/Acis`。
- `Core` 只放领域模型、枚举、稳定接口和无外部依赖的抽象。

## UI And Theme Rules
- 界面风格保持蓝黑科技态势风，但必须极简、克制、可值守。
- 主题色、状态色、文案键集中管理，禁止在页面、控件和 ViewModel 中散落硬编码。
- 所有调试与诊断信息只允许写本地日志，不允许显示在 UI 上。
- 首屏优先地图与详情联动，不增加无关页面、调试面板或诊断角标。

## CTYun And Integration Workflow
- 后续每次变更前先阅读 `docs/ctyun-api/` 下文档，再决定是否调整接入边界。
- 本仓库是重构版新仓库，不是旧仓库直接改；不要把旧项目结构原样迁入。
- 真实 CTYun、地图 SDK、WebView2、SQLite、Webhook 接入未启用前，只保留接口、占位和文档，不写假实现冒充真实能力。

## Change Process
- 所有新增功能都要先给出“本轮目标 / 风险 / 验收项”。
- 优先保持代码小步、可构建、可回退；每轮先保证 `dotnet build`。
- 变更时先检查是否破坏分层、主题集中管理和地图中心布局，再开始编码。

## Build Baseline
- 解决方案：`Tysl.Ai.sln`
- 默认构建命令：`dotnet build Tysl.Ai.sln`
- 默认运行命令：`dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj`
