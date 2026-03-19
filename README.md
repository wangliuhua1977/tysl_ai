# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。

当前仓库已经进入“真实 ACIS 点位源 + 本地补充信息覆盖 + WebView2 地图宿主 + 静默巡检运行态”的主线：
- 中央区域由 `WebView2 + 高德 JSAPI 2.0` 承载本地地图宿主页
- 后端透传平台原始坐标、本地手工坐标和坐标来源
- 前端地图宿主按 `RawCoordinateType` 完成坐标转换与 marker 渲染
- 点位默认只显示“别名优先、设备名兜底”的一行名称
- 摘要卡片只在 hover / click marker 时出现，不默认铺满地图
- 后台静默巡检会在监测时段内刷新运行态、写入本地 SQLite，并生成最近截图留痕

## 当前状态

- 平台设备权威源默认走 `AcisKernelPlatformSiteProvider`
- `SiteMapQueryService` 负责“平台快照 + 本地补充信息 + 运行态”的合并
- 运行态持久化表已包含：
  - `site_local_profile`
  - `site_runtime_state`
  - `snapshot_record`
  - `inspection_settings`
- 编辑弹窗仍只维护本地补充信息，不新增平台点位，也不修改平台主档
- 最近截图链路为首版实现：当前写入占位截图文件和说明文本，保证路径落库、详情可见、异常条可联动

## 静默巡检

- 默认监测时段：`07:00 - 22:00`
- 默认巡检间隔：`5` 分钟
- 巡检对象：仅 `IsMonitored = true` 的点位
- 巡检信号：
  - 在线状态
  - 运行态摘要
  - 预览地址解析结果
  - 最近截图留痕
- UI 联动：
  - 地图 marker 状态色优先反映最新运行态
  - 右侧详情抽屉显示最近巡检、最近截图和运行摘要
  - 底部异常缩略条优先展示最近运行态与最近截图

## 配置与降级

仓库提供模板文件：
- `configs/acis-kernel.template.json`
- `configs/amap-js.template.json`

运行时读取：
- `configs/acis-kernel.json`
- `configs/amap-js.json`

受控降级规则：
- 缺少 `configs/acis-kernel.json`：应用仍可启动，静默巡检进入跳过模式，地图可显示为空底图
- 缺少 `configs/amap-js.json`：应用仍可启动，中央区域显示“地图未配置”
- JS 初始化失败：应用不崩溃，只显示通用占位
- 单个点位巡检失败：只更新该点位连续失败次数和本地日志，不拖垮整轮巡检

## 当前边界

- 本轮不接企业微信派单
- 本轮不接工单系统
- 本轮不接复杂报表
- 本轮不接全量视频墙
- 本轮不在 UI 暴露日志、堆栈或协议诊断文本
- 所有 CTYun / ACIS 相关接入继续统一落在 `Infrastructure/Integrations/Acis`

## 构建与运行

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```
