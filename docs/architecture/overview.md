# 架构总览

## 产品目标

`Tysl.Ai` 面向地图中心型桌面值守场景，固定工作方式为：
- 地图主视图承载点位和状态
- 右侧详情抽屉承载单点详情、本地补充信息、运行态与派单摘要
- 底部异常缩略条承载异常切换、最近截图和恢复联动

本项目不回退成表格中心页面，也不在首屏堆叠调试面板或诊断文本。

## 当前主链

- 真实平台设备源：`AcisKernelPlatformSiteProvider`
- 本地补充信息：SQLite `site_local_profile`
- 运行态持久化：SQLite `site_runtime_state`
- 最近截图留痕：`runtime/snapshots/` + `snapshot_record`
- 派单策略与记录：`dispatch_policy` + `dispatch_record`
- 巡检编排：`SilentInspectionService` + `SilentInspectionHostedService`
- 派单编排：`DispatchService`
- 视图编排：`SiteMapQueryService`
- 地图宿主：`Tysl.Ai.UI.Views.Controls.AmapHostControl`
- 地图前端：`WebView2 + 本地 HTML/JS + 高德 JSAPI 2.0`

## 当前数据流

1. `App` 启动时读取 `configs/acis-kernel.json` 与 `configs/amap-js.json`
2. `AcisKernelPlatformSiteProvider` 通过 `AcisApiKernel` 拉取设备目录与部分详情
3. `SilentInspectionHostedService` 在监测时段内触发 `SilentInspectionService`
4. 巡检服务对纳管点位采集在线状态、预览解析结果和截图留痕，并更新 SQLite 运行态
5. `DispatchService` 复用最新运行态判断故障、冷却、自动派单、恢复候选和恢复通知
6. `SiteMapQueryService` 合并平台快照、本地补充信息、运行态和派单记录，输出 `SiteMergedView / SiteMapPoint / SiteAlertDigest`
7. `ShellViewModel` 定时刷新查询结果，组装地图宿主 DTO，并序列化给 `AmapHostControl`
8. 前端地图宿主完成坐标转换、marker 渲染、轻量状态角标、hover/click 摘要卡片与点击回传
9. 右侧详情抽屉和底部异常缩略条根据聚合结果展示待派单、已派单、冷却中、待人工恢复和已恢复

## 处置闭环

- 派单策略由 `dispatch_policy` 提供
- 派单历史与冷却状态落库到 `dispatch_record`
- 自动模式当前只支持企业微信 webhook
- 手动模式只落“待派单”，不扩展为复杂工单系统
- 恢复逻辑支持：
  - 自动恢复
  - 手工确认恢复
  - 恢复通知可选

## 受控降级

- 缺少 `amap-js.json`：应用仍可运行，中央区域显示“地图未配置”
- 缺少 `acis-kernel.json`：应用仍可运行，但平台点位为空，静默巡检自动跳过
- 缺少 webhook：应用不崩溃，派单记录进入简短降级状态
- webhook 单条发送失败：只影响该条派单记录
- 预览解析失败或截图失败：更新运行态与派单判断，不让程序崩溃
- 单个点位巡检失败：不拖垮整轮巡检

## 当前非目标

- 复杂工单系统
- 审批流
- 多通道通知
- 复杂报表
- 全量视频墙
- 在 UI 暴露诊断堆栈
