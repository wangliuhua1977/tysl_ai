# 架构总览

## 产品定位

`Tysl.Ai` 固定为地图中心型桌面监控系统，工作方式保持为：

- 地图主视图承载点位与状态。
- 右侧详情抽屉承载单点详情、运行态、派单与维护信息。
- 底部异常缩略条承载异常切换、最近截图和恢复联动。

不回退成表格中心页面，也不在首页堆叠调试面板、日志窗口或诊断角标。

## 五层边界

- `App`：启动、组合根和依赖注入。
- `UI`：WPF 视图、主题、控件、ViewModel、WebView2 宿主和交互状态。
- `Services`：查询编排与服务接口。
- `Infrastructure`：ACIS、SQLite、派单、截图、配置持久化和外部系统接入。
- `Core`：领域模型、枚举和稳定接口。

## 当前主链路

1. `App` 启动时读取 `configs/acis-kernel.json`、`configs/amap-js.json`、`configs/dispatch.json`。
2. `AcisKernelPlatformSiteProvider` 通过 `AcisApiKernel` 拉取设备目录与详情。
3. `SilentInspectionHostedService` 在监测时段内驱动 `SilentInspectionService`。
4. 巡检结果落入 `site_runtime_state`、`snapshot_record` 和 `runtime/snapshots/`。
5. `DispatchService` 复用运行态完成派单、冷却、恢复与恢复通知。
6. `SiteMapQueryService` 合并平台快照、本地补充信息、运行态和派单记录。
7. `ShellViewModel` 组织地图点位、右侧详情抽屉和底部异常缩略条数据。
8. `AmapHostControl` 与 `UI/Web/amap/*` 负责地图渲染、前端坐标转换、marker 交互和风格切换。

## 地图默认行为

- 点位默认只显示“小图标 + 一行名称”。
- 名称规则为别名优先，设备名兜底。
- 点击点位只高亮当前点位并联动右侧详情，不再弹出遮挡地图的摘要面板。
- 地图风格支持轻量切换，默认值为原生地图样式。
- 当前风格选择会持久化到本地 `amap-js.json` 配置。

## 当前受控降级

- 缺少 `acis-kernel.json`：应用可运行，但平台点位为空，巡检跳过。
- 缺少 `amap-js.json`：应用可运行，地图区域显示未配置占位。
- 缺少 `dispatch.json`：应用可运行，派单链路保持未配置状态。
- 单条 webhook 发送失败：仅影响当前记录，不拖垮其他点位巡检。

## 首版边界

- 只支持企业微信 webhook。
- 不接复杂工单系统、审批流、多通道通知和复杂报表。
- 截图为首版留痕能力，优先保证落盘、落库和界面联动。
