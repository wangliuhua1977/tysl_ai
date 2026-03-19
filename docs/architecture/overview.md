# 架构总览

## 产品目标

`Tysl.Ai` 面向地图中心型桌面值守场景，固定工作方式为：
- 地图主视图承载点位和状态
- 右侧详情抽屉承载单点详情与本地补充信息
- 底部异常缩略条承载快速切换

本项目不回退成表格中心页面，也不在首屏堆叠调试面板或诊断文本。

## 当前主链

- 真实平台设备源：`AcisKernelPlatformSiteProvider`
- 本地补充信息：SQLite `site_local_profile`
- 视图编排：`SiteMapQueryService`
- 地图宿主：`Tysl.Ai.UI.Views.Controls.AmapHostControl`
- 地图前端：`WebView2 + 本地 HTML/JS + 高德 JSAPI 2.0`

## 当前数据流

1. `App` 启动时读取 `configs/acis-kernel.json` 与 `configs/amap-js.json`
2. `AcisKernelPlatformSiteProvider` 通过 `AcisApiKernel` 拉取设备目录与部分详情
3. 平台原始坐标和原始坐标类型进入 `PlatformSiteSnapshot`
4. `SiteMapQueryService` 把平台快照与本地补充信息合并为 `SiteMergedView / SiteMapPoint / SiteAlertDigest`
5. `ShellViewModel` 组装地图宿主 DTO，并序列化给 `AmapHostControl`
6. `AmapHostControl` 通过消息桥和本地 JS 通信
7. 前端地图宿主完成坐标转换、marker 渲染、hover/click 摘要卡片与点击回传
8. 前端把 marker 点击、地图点击和转换后的显示坐标回传给 WPF

## 坐标规则

- 后端不做高德 WebService 坐标转换
- 前端地图宿主按 `PlatformRawCoordinateType` 决定是否 `convertFrom`
- 本地手工坐标视为 `GCJ-02`
- 平台与本地都无坐标时，不落点
- 详情抽屉保留：
  - 平台原始坐标
  - 本地手工坐标
  - 当前显示坐标
  - 坐标来源

## 受控降级

- 缺少 `amap-js.json`：应用仍可运行，中央区域显示“地图未配置”
- 缺少 `acis-kernel.json`：地图仍可初始化为空底图，但没有平台点位
- JS 初始化失败：只显示“地图暂不可用”的通用占位，不让程序崩溃

## 当前非目标

- 真实派单链路
- 复杂播放器
- 静默巡检完整编排
- 在 UI 暴露诊断堆栈
