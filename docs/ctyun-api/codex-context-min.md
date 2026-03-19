# Codex Context Min

## 项目背景

- 本仓库是 `Tysl.Ai` 的重构版新仓库，不是旧项目直接搬迁
- 产品定位固定为地图中心型桌面监控系统，不是后台管理系统
- 主界面长期保持“地图主视图 + 右侧详情抽屉 + 底部异常缩略条”

## 当前开发阶段

- 第 3 轮已开始接真实 ACIS 内核
- 平台设备权威源主路径已替换为 `AcisKernelPlatformSiteProvider`
- `StubPlatformSiteProvider` 仅可保留作临时 fallback / 开发测试实现，不再作为默认主路径
- 本地 SQLite 继续只保存 `site_local_profile` 补充信息，不保存完整平台主档
- 编辑弹窗继续只允许编辑本地补充信息，不允许新增平台设备

## 当前已接入范围

- `AcisApiKernel` 真实调用链复用
- 设备目录分页拉取
- 按需设备详情补拉
- 平台原始坐标透传
- 缺配置时的受控降级

## 当前坐标策略

- 当前项目坐标转换走前端高德 JSAPI
- 后端 ACIS 主链不依赖高德 WebService 坐标转换
- `AcisKernelPlatformSiteProvider` 不再把 `ConvertCoordinatesAsync` 作为平台点位必经步骤
- `SiteMapQueryService` 只合并平台原始坐标和本地手工坐标，不做服务层转换
- `amap.webServiceKey` 允许为空
- 若未来改回后端 Web 服务转换，再启用 `ConvertCoordinatesAsync`

## 当前仍未接入

- 真实播放 UI
- 静默巡检
- 企业微信派单
- WebView2 宿主播放器整合
- webhook 编排

## 开发提示

- 所有 CTYun / ACIS 相关接入统一落在 `Infrastructure/Integrations/Acis`
- `UI` 不直接调用平台，不直接处理签名、解密、坐标转换或日志细节
- 若本地不存在 `configs/acis-kernel.json`，应用应保持可运行并显示“平台未连接”
