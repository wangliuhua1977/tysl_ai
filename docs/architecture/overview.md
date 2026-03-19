# 架构总览

## 产品目标

`Tysl.Ai` 面向地图中心型桌面值守场景，固定工作方式为：
- 地图主视图承载点位和状态
- 右侧详情抽屉承载单点详情、本地补充信息和最新运行态
- 底部异常缩略条承载异常切换与最近截图留痕

本项目不回退成表格中心页面，也不在首屏堆叠调试面板或诊断文本。

## 当前主链

- 真实平台设备源：`AcisKernelPlatformSiteProvider`
- 本地补充信息：SQLite `site_local_profile`
- 运行态持久化：SQLite `site_runtime_state`
- 最近截图留痕：`runtime/snapshots/` + `snapshot_record`
- 巡检编排：`SilentInspectionService` + `SilentInspectionHostedService`
- 视图编排：`SiteMapQueryService`
- 地图宿主：`Tysl.Ai.UI.Views.Controls.AmapHostControl`
- 地图前端：`WebView2 + 本地 HTML/JS + 高德 JSAPI 2.0`

## 当前数据流

1. `App` 启动时读取 `configs/acis-kernel.json` 与 `configs/amap-js.json`
2. `AcisKernelPlatformSiteProvider` 通过 `AcisApiKernel` 拉取设备目录与部分详情
3. `SilentInspectionHostedService` 在监测时段内触发 `SilentInspectionService`
4. 巡检服务对纳管点位采集在线状态、预览解析结果和截图留痕，并更新 SQLite 运行态
5. `SiteMapQueryService` 合并平台快照、本地补充信息和运行态，输出 `SiteMergedView / SiteMapPoint / SiteAlertDigest`
6. `ShellViewModel` 定时刷新查询结果，组装地图宿主 DTO，并序列化给 `AmapHostControl`
7. `AmapHostControl` 通过消息桥和本地 JS 通信
8. 前端地图宿主完成坐标转换、marker 渲染、hover/click 摘要卡片与点击回传
9. 前端把 marker 点击、地图点击和转换后的显示坐标回传给 WPF

## 运行态闭环

- 巡检时段控制由 `inspection_settings` 提供
- 巡检结果落库到 `site_runtime_state`
- 最近截图文件写入 `runtime/snapshots/`
- 截图记录写入 `snapshot_record`
- 重启后仍可显示最近巡检时间、最近截图路径和运行摘要

## 受控降级

- 缺少 `amap-js.json`：应用仍可运行，中央区域显示“地图未配置”
- 缺少 `acis-kernel.json`：应用仍可运行，但平台点位为空，静默巡检自动跳过
- ACIS 平台读取失败：只写本地诊断日志，UI 保持简短状态
- 预览解析失败：更新运行态摘要和连续失败次数，不让程序崩溃
- 单个点位巡检失败：不拖垮整轮巡检

## 当前非目标

- 企业微信派单
- 工单系统
- 复杂报表
- 全量视频墙
- 在 UI 暴露诊断堆栈
