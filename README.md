# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。当前首版围绕“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”展开，主线已经收口到可运行、可降级、可验收的首版状态。

## 首版能力边界

已完成：

- 真实 ACIS 平台点位权威源接入。
- 本地补充信息覆盖与维护。
- `WebView2 + 高德 JSAPI 2.0` 地图宿主。
- 前端坐标转换与地图渲染联动。
- 静默巡检、运行态刷新、截图留痕首版闭环。
- 企业微信 webhook 派单、冷却、自动恢复 / 手工恢复首版。
- 缺少 ACIS / 地图 / 派单配置时的受控降级启动。

当前未完成：

- 复杂工单系统、审批流、权限系统。
- 多通道通知、复杂报表、任务面板回退。
- 真实视频抓帧级截图能力。
- WebRTC 宿主播放链路完备接入。

首版已知限制：

- 截图仍为首版留痕实现，优先保证落盘、落库和 UI 可见，不等同于完整视频帧抓图。
- 当前自动派单只支持企业微信 webhook。
- 缺少 `dispatch.json` 时应用可运行，但派单处于未配置状态。

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

- `acis-kernel.json` 缺失时：应用可启动，但平台点位为空，静默巡检进入跳过模式。
- `amap-js.json` 缺失时：应用可启动，中央区域显示“地图未配置”。
- `dispatch.json` 缺失时：应用可启动，派单链路保持可运行但未配置状态，不会注入真实 webhook。

## 运行方式

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

运行期目录说明：

- `runtime/.acis-kernel/`：ACIS 内核日志、令牌缓存、预览宿主页等本地产物。
- `runtime/snapshots/`：静默巡检截图留痕目录。

上述目录只保留说明和占位文件，不提交运行期产物。

## 首版验收自检

- 无 `acis-kernel.json` 时应用可启动。
- 无 `amap-js.json` 时应用可启动。
- 无 `dispatch.json` 时应用可启动。
- 有地图配置时可加载真实高德地图。
- 点位默认优先显示别名，其次显示设备名。
- 默认不展开所有摘要卡片，仅在 hover / click marker 时显示。
- 右侧详情显示运行态与派单态。
- 静默巡检链路写入 `site_runtime_state`。
- 派单链路写入 `dispatch_record`，冷却内不重复派单。
- 自动恢复 / 手工恢复链路可达。
- UI 不显示调试、堆栈或诊断信息。

## 当前结构要点

- `src/Tysl.Ai.App`：启动、依赖装配、配置读取。
- `src/Tysl.Ai.UI`：WPF 视图、ViewModel、主题、WebView2 地图宿主。
- `src/Tysl.Ai.Services`：面向 UI 的查询编排。
- `src/Tysl.Ai.Infrastructure`：ACIS、SQLite、派单、截图、后台巡检。
- `src/Tysl.Ai.Core`：领域模型、枚举与稳定接口。
