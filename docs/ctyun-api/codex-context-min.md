# Codex Context Min

## 项目背景

- 本仓库是 `Tysl.Ai` 的重构版新仓库，不是旧项目直接迁移
- 产品定位固定为地图中心型桌面监控系统
- 主界面长期保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”

## 当前开发状态

- 真实 ACIS 内核已接入
- 平台设备权威源主路径为 `AcisKernelPlatformSiteProvider`
- 地图宿主已切到 `WebView2 + 高德 JSAPI 2.0`
- 本地地图前端资源位于 `src/Tysl.Ai.UI/Web/amap/`
- 编辑弹窗继续只允许维护本地补充信息

## 当前坐标策略

- 后端只提供：
  - 平台原始坐标
  - 本地手工坐标
  - 原始坐标类型
  - 坐标来源
- 前端地图宿主负责：
  - `bd09 / gps / mapbar` 的 `convertFrom`
  - 手工坐标按 `GCJ-02` 直接上图
  - marker 渲染与点击回传
  - 地图点击回传经纬度
- `AcisKernelPlatformSiteProvider` 不再调用 `ConvertCoordinatesAsync`
- `SiteMapQueryService` 不在服务层做坐标转换

## 当前降级策略

- 无 `configs/acis-kernel.json`：应用可运行，但无平台点位
- 无 `configs/amap-js.json`：应用可运行，中央区域显示“地图未配置”
- JS 初始化失败：应用不崩溃，只显示通用占位

## 点位展示约束

- 默认只显示别名或设备名，别名优先
- 默认不展开所有摘要卡片
- hover / click marker 时才显示轻量摘要卡片
- 维护信息、详细坐标和备注继续放在右侧详情抽屉
