# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。

当前主线已经进入：
- 真实 ACIS 点位源
- 本地补充信息覆盖
- `WebView2 + 高德 JSAPI 2.0` 地图宿主
- 前端坐标转换
- 静默巡检与运行态刷新
- 最近截图留痕首版
- 企业微信 webhook 派单、冷却与恢复首版

## 当前状态

- 平台设备权威源走 `AcisKernelPlatformSiteProvider`
- `SiteMapQueryService` 已合并：
  - 平台快照
  - 本地补充信息
  - 运行态
  - 派单记录摘要
- 本地 SQLite 已落地：
  - `site_local_profile`
  - `site_runtime_state`
  - `snapshot_record`
  - `inspection_settings`
  - `dispatch_policy`
  - `dispatch_record`
- 故障处置首版仅支持企业微信 webhook，不引入复杂工单系统、审批流、报表中心和多通知通道
- 截图仍是首版留痕实现，当前保证路径落库、详情可见、异常条联动，不是完整视频帧抓图

## 静默巡检与派单

- 默认监测时段：`07:00 - 22:00`
- 默认巡检间隔：`5` 分钟
- 巡检对象：仅 `IsMonitored = true` 的点位
- 首版故障触发信号：
  - 设备离线
  - 预览解析失败
  - 截图失败
  - 巡检执行失败
- 处置链路：
  - 巡检落运行态
  - 判断故障是否可派单
  - 查询本地派单记录和冷却状态
  - 自动模式直接发企业微信 webhook
  - 手动模式只落“待派单”
  - 恢复后按策略走自动恢复或待人工确认恢复

## 配置与降级

模板文件：
- `configs/acis-kernel.template.json`
- `configs/amap-js.template.json`

运行时读取：
- `configs/acis-kernel.json`
- `configs/amap-js.json`

受控降级规则：
- 缺少 `configs/acis-kernel.json`：应用仍可启动，静默巡检进入跳过模式
- 缺少 `configs/amap-js.json`：应用仍可启动，中央区域显示“地图未配置”
- 缺少 webhook 配置：自动派单不崩溃，记录保持“待发送/未发送”降级状态
- 单条 webhook 发送失败：只影响该点位记录，不拖垮整轮巡检
- 单个点位巡检失败：只更新该点位运行态与派单判断，不拖垮整轮巡检

## 当前边界

- 不做复杂工单系统
- 不做审批流
- 不做短信、邮件等多通道通知
- 不在 UI 暴露日志、堆栈或请求细节
- 不破坏地图主视图、右侧详情抽屉、底部异常缩略条的主交互
- 所有 CTYun / ACIS 接入继续统一落在 `Infrastructure/Integrations/Acis`

## 构建与运行

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```
