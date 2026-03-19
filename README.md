# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。

当前仓库已经切到真实地图宿主主路径：
- 中央区域由 `WebView2 + 高德 JSAPI 2.0` 承载本地地图宿主页
- 后端只透传平台原始坐标、本地手工坐标和坐标来源
- 前端地图宿主按 `RawCoordinateType` 完成坐标转换与 marker 渲染
- 点位默认只显示“别名优先、设备名兜底”的一行名称
- 摘要卡片只在 hover / click marker 时出现，不再默认铺满地图

## 当前状态

- 平台设备权威源默认走 `AcisKernelPlatformSiteProvider`
- `SiteMapQueryService` 继续负责“平台快照 + 本地补充信息 + 运行态”的合并
- 编辑弹窗仍只维护本地补充信息，不新增平台点位，也不修改平台主档
- 缺少 `configs/amap-js.json` 时应用仍可启动，中央区域进入“地图未配置”的受控降级
- 缺少 `configs/acis-kernel.json` 时地图仍可初始化为空底图，但不会加载平台点位

## 地图配置

仓库提供模板文件：
- `configs/acis-kernel.template.json`
- `configs/amap-js.template.json`

运行时读取：
- `configs/acis-kernel.json`
- `configs/amap-js.json`

`amap-js.json` 至少包含：

```json
{
  "key": "your-amap-jsapi-key",
  "securityJsCode": "your-amap-security-js-code",
  "mapStyle": "amap://styles/darkblue",
  "zoom": 11,
  "center": [120.585316, 30.028105]
}
```

说明：
- `key` 和 `securityJsCode` 由前端地图宿主读取，不散落在多个文件
- 模板不包含真实敏感值
- 缺少真实 `amap-js.json` 时，应用保持可运行，但不初始化地图宿主

## 坐标分工

- 后端：
  - 读取并透传平台原始坐标
  - 读取并透传本地手工坐标
  - 输出坐标来源与原始坐标类型
  - 不重新启用 `ConvertCoordinatesAsync`
- 前端地图宿主：
  - `bd09` 走 `convertFrom("baidu")`
  - `wgs84 / gps` 走 `convertFrom("gps")`
  - `mapbar` 走 `convertFrom("mapbar")`
  - 本地手工坐标直接按 `GCJ-02` 上图
  - 渲染 marker，并把转换后的显示坐标回传给 WPF 详情抽屉

## 构建与运行

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## 当前边界

- 本轮不接真实派单
- 本轮不接复杂播放器
- 本轮不在 UI 暴露 JS 异常、堆栈或诊断文本
- 所有 CTYun / ACIS 相关接入仍统一落在 `Infrastructure/Integrations/Acis`
