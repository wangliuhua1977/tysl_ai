# 架构总览

## 产品目标

`Tysl.Ai` 面向地图中心型值守场景，核心工作方式固定为：

- 地图主视图承载设备点位与状态表达
- 右侧详情抽屉承载单点详情与维护入口
- 底部异常缩略条承载关注点快速切换

本项目不是传统后台管理系统，不回退到表格中心页面。

## 第 2 轮纠偏后的核心边界

- 点位主源改为平台接口语义，本轮由 `StubPlatformSiteProvider` 代替真实平台返回设备快照。
- 本地 SQLite 只保存补充信息，不再承担平台设备主档职责。
- 地图、详情和异常条都由“平台设备快照 + 本地补充信息”合并得到。
- 编辑行为只允许修改本地补充信息，不能新增平台设备。

## 当前结构

### 平台设备权威源

- `PlatformSiteSnapshot` 表达平台返回的设备快照。
- 当前来源是 `Infrastructure/Integrations/Acis/StubPlatformSiteProvider`。
- 该 Stub 仅用于第 3 轮前的结构演示，不伪装成真实 ACIS 接入。

### 本地补充信息

- `SiteLocalProfile` 表达本地补充信息。
- 存储表为 `site_local_profile`，主键是 `device_code`。
- 字段覆盖别名、备注、监测开关、手工坐标、地址、接入号、维护信息和时间戳。

### 合并视图

- `SiteMapQueryService` 合并平台快照和本地补充信息。
- `SiteMergedView` 用于详情抽屉和编辑弹窗上下文。
- `SiteMapPoint` 用于地图点位展示。
- `SiteAlertDigest` 用于底部异常缩略条。

## 坐标规则

- 优先使用平台坐标。
- 平台无坐标时，允许使用本地手工补录坐标兜底。
- 平台和本地都无坐标时，不显示地图点位。

## 当前非目标

- 本轮不接真实 ACIS / CTYun。
- 本轮不接真实地图 SDK / WebView2 / webhook。
- 本轮不在 UI 展示日志、诊断、配置或调试信息。
