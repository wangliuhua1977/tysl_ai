# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。当前首版长期保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”的工作方式。

## 首版能力边界

已完成：

- 真实 ACIS 平台点位权威源接入。
- 本地补充信息维护与坐标补录。
- `WebView2 + 高德 JSAPI 2.0` 地图宿主。
- 前端坐标转换与地图渲染联动。
- 静默巡检、运行态刷新、截图留痕首版闭环。
- 企业微信 webhook 派单、冷却、自动恢复和手工恢复首版。
- 缺少 ACIS / 地图 / 派单配置时的受控降级启动。

未完成：

- 复杂工单系统、审批流、权限体系。
- 多通道通知、复杂报表和任务面板回退。
- 完整视频抓帧能力。
- WebRTC 宿主播放链路完善接入。

## 地图展示约束

- 地图点位默认只显示“小图标 + 一行名称”，不再使用面板式名称块。
- 名称规则为别名优先，设备名兜底；超长名称在地图上自动省略。
- 点击点位只高亮当前点位，并联动右侧详情抽屉，不再弹出遮挡地图的摘要面板。
- hover 不再显示大卡片，地图默认保持清爽态势图视图。
- 地图支持风格切换，当前内置：
  - 默认地图 / 原生地图样式
  - 深色科技风
  - 简洁浅色风
  - 灰阶监控风
- 默认风格为“默认地图 / 原生地图样式”。
- 当前风格选择会持久化回本地 `configs/amap-js.json` 的 `mapStyle` 字段。

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
- 缺少 `amap-js.json` 时，应用可启动，中央区域显示“地图未配置”。
- 缺少 `dispatch.json` 时，应用可启动，派单链路保持未配置状态。
- `amap-js.json` 中 `mapStyle` 留空、缺省或设为 `default` 时，地图按原生默认样式启动。

## 运行方式

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## 首版验收自检

- 无 `acis-kernel.json` 时应用可启动。
- 无 `amap-js.json` 时应用可启动。
- 无 `dispatch.json` 时应用可启动。
- 有地图配置时可加载高德地图。
- 地图点位默认只显示小图标和单行名称。
- 地图点位名称遵循别名优先、设备名兜底。
- 点击点位只联动右侧详情，不再弹出地图摘要卡片。
- 地图风格切换即时生效，且默认使用原生地图样式。
- 右侧详情继续显示运行态与派单态。
- 底部异常缩略条与地图、详情保持联动。
- UI 不显示调试、堆栈或诊断信息。

## 当前结构要点

- `src/Tysl.Ai.App`：启动、组合根、配置读取。
- `src/Tysl.Ai.UI`：WPF 视图、ViewModel、主题和 WebView2 地图宿主。
- `src/Tysl.Ai.Services`：面向 UI 的查询编排。
- `src/Tysl.Ai.Infrastructure`：ACIS、SQLite、派单、截图和后台巡检。
- `src/Tysl.Ai.Core`：领域模型、枚举与稳定接口。
