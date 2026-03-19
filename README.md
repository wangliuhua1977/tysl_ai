# Tysl.Ai

`Tysl.Ai` 是地图中心型桌面值守监控系统的重构仓库，不是传统后台管理系统。

当前仓库已经切换到真实 ACIS 平台设备主链：
- 平台设备权威源默认走 `AcisKernelPlatformSiteProvider`
- `SiteMapQueryService` 继续负责“平台快照 + 本地补充信息 + 运行态”的合并
- 编辑弹窗只维护本地补充信息，不新增平台点位，也不修改平台主档

## 当前状态

- 平台设备源：优先读取 `configs/acis-kernel.json`，成功后通过 `AcisApiKernel` 拉取 ACIS 设备目录与部分设备详情
- 本地补充信息：继续保存在 SQLite `site_local_profile` 表，用于别名、监测开关、本地手工坐标和维护信息补充
- 坐标规则：后端只透传平台原始坐标；本地手工坐标仍可作为兜底；当前项目坐标转换走前端高德 JSAPI
- 受控降级：未检测到 ACIS 配置文件或配置不完整时，应用仍可启动，UI 只显示简短平台状态，不显示异常堆栈或诊断信息

## 坐标链路说明

- 后端 ACIS 主链不依赖高德 WebService 坐标转换
- `AcisKernelPlatformSiteProvider` 不再把 `AcisApiKernel.ConvertCoordinatesAsync` 作为平台点位必经步骤
- 平台返回的原始经纬度会直接进入 `PlatformSiteSnapshot`
- `SiteMapQueryService` 只负责合并，不在服务层做百度转高德
- 前端地图宿主后续可根据 `PlatformRawCoordinateType` 使用高德 JSAPI 进行转换
- 若未来改回后端 Web 服务转换，再启用 `ConvertCoordinatesAsync`

## 目录

```text
/
  AGENTS.md
  README.md
  configs/
    acis-kernel.template.json
  docs/
    architecture/
    ctyun-api/
  src/
    Tysl.Ai.App/
    Tysl.Ai.UI/
    Tysl.Ai.Services/
    Tysl.Ai.Infrastructure/
    Tysl.Ai.Core/
```

## ACIS 配置

仓库仅提供模板文件：
- `configs/acis-kernel.template.json`

运行时实际读取：
- `configs/acis-kernel.json`

建议做法：
1. 复制模板为 `configs/acis-kernel.json`
2. 填入真实 CTYun / ACIS 参数
3. `amap.webServiceKey` 允许为空，也可以直接不填

说明：
- 当前项目坐标转换走前端高德 JSAPI，不依赖后端 `amap.webServiceKey`
- 若未来恢复后端高德 Web 服务坐标转换，再补充 `amap.webServiceKey`

## 构建与运行

```powershell
dotnet build Tysl.Ai.sln
dotnet run --project src/Tysl.Ai.App/Tysl.Ai.App.csproj
```

## 当前边界

- 真实 ACIS 设备目录与详情已开始接入
- Stub 仅保留作开发测试实现，不再是默认主路径
- 真实预览播放器、静默巡检和派单能力留待后续轮次
