# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。主界面长期保持“地图主视图 + 右侧详情抽屉 + 底部联动区”的工作方式。

## V2 第一轮重点

V2 第一轮聚焦“坐标治理与未落图点位管理”，不是新增业务模块。本轮重点解决以下问题：

- 顶部总点位容易被误解为地图已全部显示。
- 缺坐标点位缺少治理入口，用户不知道为什么未落图、如何补坐标。
- 多点位坐标重合时，地图可读性不足。

## 当前能力边界

已完成：

- 真实 ACIS 平台点位权威源接入。
- 本地补充信息维护与手工坐标补录。
- `WebView2 + 高德 JSAPI 2.0` 地图宿主。
- 静默巡检、运行态、截图留痕、企业微信派单与恢复首版。
- 顶部“总点位 / 已落图 / 未落图”统计。
- 左侧“全部 / 异常 / 已纳管 / 已处置 / 未落图”筛选。
- 右侧详情中的坐标治理信息与未落图原因说明。
- 底部未落图治理入口与地图重叠点位轻量偏移。

未完成：

- 复杂工单系统、审批流、权限体系。
- 多通道通知、复杂报表和传统后台台账页。
- 完整视频抓帧能力。
- WebRTC 宿主播放链路完善接入。

## 地图展示约束

- “全部”指全部点位，不等于全部点位都已落图。
- 当前地图只显示具备可用显示坐标的点位。
- 已落图点位来源可能是平台原始坐标，也可能是本地手工坐标。
- 未落图点位可通过“编辑补充信息 / 补坐标”继续治理。
- 地图点位仍保持“小图标 + 一行名称”，点击点位只联动右侧详情。
- 多个点位坐标重合或极近时，地图宿主会做轻量视觉偏移，不引入复杂聚合框架。

## 配置准备

仓库只保留模板文件，不提交真实敏感值：

- `configs/acis-kernel.template.json`
- `configs/amap-js.template.json`
- `configs/dispatch.template.json`

按需复制并准备本地运行配置：

```powershell
Copy-Item configs/acis-kernel.template.json configs/acis-kernel.json
Copy-Item configs/amap-js.template.json configs/amap-js.json
Copy-Item configs/dispatch.template.json configs/dispatch.json
```

说明：

- 缺少 `acis-kernel.json` 时，应用可启动，但平台点位为空，静默巡检进入跳过模式。
- 缺少 `amap-js.json` 时，应用可启动，但地图区域显示未配置占位。
- 缺少 `dispatch.json` 时，应用可启动，但派单链路保持未配置状态。

## 运行方式

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## V2 第一轮验收自检

- `dotnet build Tysl.Ai.sln` 成功。
- 顶部可看到总点位、已落图、未落图。
- 左侧存在未落图筛选。
- 地图区明确显示“当前地图显示 X / 总点位 Y”。
- 右侧详情能看到平台原始坐标、原始坐标类型、手工坐标、当前显示坐标、坐标来源、未落图原因和治理建议。
- 底部存在未落图治理入口，点击后可联动详情并直接进入补坐标。
- 地图重叠点位可读性优于未处理状态。
- UI 不显示调试、堆栈或诊断信息。

## 当前结构要点

- `src/Tysl.Ai.App`：启动、组合根、配置读取。
- `src/Tysl.Ai.UI`：WPF 视图、ViewModel、主题和 WebView2 地图宿主。
- `src/Tysl.Ai.Services`：面向 UI 的查询编排与治理汇总。
- `src/Tysl.Ai.Infrastructure`：ACIS、SQLite、派单、截图和后台巡检接入。
- `src/Tysl.Ai.Core`：领域模型、枚举与稳定接口。
