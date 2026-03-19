
# ACIS API 复用内核调用说明手册

## 1. 交付内容

本次交付包含两个文件：

- `AcisApiKernel.cs`  
  单文件复用内核。把 CTYun 令牌本地缓存、签名/加解密、平台接口、地图坐标转换、点位直播 URL 获取、预览宿主页生成、配置文件读写、本地日志，全部收敛在一个 C# 文件中。
- `AcisApiKernel_Usage_Manual.md`  
  本说明手册。

## 2. 适用场景

适合你后续把项目重新设计为新的 .NET 8 桌面应用、服务层库、控制台探针、后台任务执行器时直接复用。

定位是：

- 先把 **“接平台、拿数据、拿流、落日志、持久化 token/配置”** 这条基础链单独抽出来
- 新项目的 UI、ViewModel、任务编排、地图渲染、派单流程，后续再在它上面重新搭

## 3. 已封装能力

### 3.1 CTYun 认证与缓存
- `GetAccessTokenAsync`
- 内存 token 缓存
- 本地 `token-cache.json` 持久化
- 过期前复用
- 优先 refreshToken 刷新
- 刷新失败且旧 token 仍有效时继续复用

### 3.2 CTYun 安全链
- 私参串拼装
- XXTEA 加密
- `AppId + ClientType + encryptedParams + timestamp + Version` 签名
- HMAC-SHA256 十六进制签名
- RSA PKCS#1 分段解密
- 兼容十六进制密文 / Base64 密文
- 兼容 `BEGIN PRIVATE KEY` / `BEGIN RSA PRIVATE KEY`

### 3.3 CTYun 业务接口
- `PostProtectedJsonAsync`
- `GetDeviceCatalogPageAsync`
- `GetDeviceDetailAsync`
- `GetDeviceAlertsAsync`
- `GetAiAlertsAsync`

### 3.4 直播 URL 获取
- `ResolvePreviewAsync`
- 点击预览默认协议顺序：`FLV -> HLS -> WebRTC -> H5`
- 后台巡检默认协议顺序：`FLV -> HLS`
- 协议级成功缓存
- 协议级失败冷却
- 自动回退
- payload 递归拆包
- URL 字段递归搜索

### 3.5 视频流播放宿主页
- `BuildPreviewHostAsync`
- 生成本地 HTML 宿主页
- FLV 使用 `mpegts.js`
- HLS 使用浏览器原生能力或 `hls.js`
- WebRTC 当前明确返回“宿主未集成承载播放器”
- H5 直接跳转

### 3.6 地图接口
- `ConvertCoordinatesAsync`
- 走高德 Web 服务坐标转换 API
- 支持把百度坐标转换成高德坐标
- 默认调用地址：
  `https://restapi.amap.com/v3/assistant/coordinate/convert`

### 3.7 配置与日志
- `LoadOptions`
- `SaveOptions`
- `WriteDiagnosticAsync`
- 默认文件日志 `acis-kernel.log`

---

## 4. 为什么这样设计

这个文件不是照抄现有项目，而是把你现在已经验证过的几条核心链路提炼成“新项目基建”：

- `ty_sight` 里已经明确存在 `TokenService`、`DeviceService`、`StreamService`，说明旧项目本身就是把 token、设备、流地址分层封装的。citeturn121922view0turn253438view0turn669449view0turn669449view1turn899741view2
- `ty_sight` 的 `ApiClient` 已经按 `AppId + ClientType + encryptedParams + timestamp + Version` 计算签名，并在 `decryptResponseData=true` 时按版本走 RSA / XXTEA 解密。citeturn781491view1
- `ty_acis` 当前的 `CtyunSecurity` 也已经按这个顺序组签名，并用 XXTEA 生成 `params`。citeturn756557view4turn756557view3
- `ty_acis` 的预览执行器已经有协议缓存、失败冷却、按失败优先级挑最终结果、以及“URL 已拿到但宿主不支持”和“解析失败”的区分。citeturn756557view1turn756557view2
- 高德官方当前仍提供 Web 服务坐标转换 API，支持 `baidu` 作为原坐标系，接口地址为 `https://restapi.amap.com/v3/assistant/coordinate/convert`。citeturn602020search0

所以这次交付的文件，本质上是把你已验证有效的几条链从“现项目里分散存在”收敛成“单文件可复用基建”。

---

## 5. 文件结构建议

建议你在新项目里这样放：

```text
src/
  YourProject.Infrastructure/
    Integrations/
      AcisApiKernel.cs
configs/
  acis-kernel.json
runtime/
  .acis-kernel/
```

---

## 6. 最小配置示例

保存为 `configs/acis-kernel.json`：

```json
{
  "workDirectory": "C:\\your_project\\runtime\\.acis-kernel",
  "ctyun": {
    "baseUrl": "https://你的天翼视联平台域名",
    "appId": "你的AppId",
    "appSecret": "你的AppSecret",
    "enterpriseUser": "你的enterpriseUser",
    "clientType": "1",
    "version": "1.1",
    "apiVersion": "1.0",
    "grantType": "vcp_189",
    "tokenReuseBeforeExpirySeconds": 60,
    "rsaPrivateKeyPem": "-----BEGIN PRIVATE KEY-----\n...\n-----END PRIVATE KEY-----",
    "endpoints": {
      "getAccessToken": "/open/oauth/getAccessToken",
      "getAllDeviceListNew": "/open/token/device/getAllDeviceListNew",
      "getCusDeviceByDeviceCode": "/open/token/device/getCusDeviceByDeviceCode",
      "showDevice": "/open/token/device/showDevice",
      "getDeviceInfoByDeviceCode": "/open/token/device/getDeviceInfoByDeviceCode",
      "getDeviceAlarmMessage": "/open/token/device/getDeviceAlarmMessage",
      "getAiAlertInfoList": "/open/token/AIAlarm/getAlertInfoList",
      "getDeviceMediaUrlFlv": "/open/token/cloud/getDeviceMediaUrlFlv",
      "getDeviceMediaUrlHls": "/open/token/cloud/getDeviceMediaUrlHls",
      "getDeviceMediaWebrtcUrl": "/open/token/vpaas/getDeviceMediaWebrtcUrl",
      "getH5StreamUrl": "/open/token/vpaas/getH5StreamUrl"
    }
  },
  "amap": {
    "webServiceKey": "你的高德Web服务Key",
    "coordinateConvertUrl": "https://restapi.amap.com/v3/assistant/coordinate/convert"
  },
  "preview": {
    "clickProtocolOrder": [ "flv", "hls", "webrtc", "h5" ],
    "inspectionProtocolOrder": [ "flv", "hls" ]
  },
  "http": {
    "timeoutSeconds": 30
  }
}
```

---

## 7. 最小调用示例

### 7.1 初始化

```csharp
using TianyiVision.Acis.Reusable;

var options = AcisApiKernel.LoadOptions("configs/acis-kernel.json");
using var kernel = new AcisApiKernel(options);
```

### 7.2 取 token

```csharp
var token = await kernel.GetAccessTokenAsync();
Console.WriteLine(token.AccessToken);
```

### 7.3 拉设备目录页

```csharp
var page = await kernel.GetDeviceCatalogPageAsync(lastId: 0, pageSize: 20);
foreach (var item in page.Items)
{
    Console.WriteLine($"{item.DeviceCode} {item.DeviceName}");
}
```

### 7.4 拉设备详情

```csharp
var detail = await kernel.GetDeviceDetailAsync("51110209021322000002");
Console.WriteLine($"经度={detail.Longitude}, 纬度={detail.Latitude}, 在线={detail.IsOnline}");
```

### 7.5 拉设备告警

```csharp
var alerts = await kernel.GetDeviceAlertsAsync("51110209021322000002");
Console.WriteLine($"告警数={alerts.Items.Count}");
```

### 7.6 拉 AI 告警

```csharp
var aiAlerts = await kernel.GetAiAlertsAsync();
Console.WriteLine(aiAlerts.DataJson);
```

### 7.7 顺序取直播 URL

```csharp
var preview = await kernel.ResolvePreviewAsync(
    "51110209021322000002",
    AcisPreviewIntent.ClickPreview);

if (preview.IsSuccess)
{
    Console.WriteLine($"协议={preview.ParsedProtocolType}, URL={preview.PreviewUrl}");
}
else
{
    Console.WriteLine($"失败={preview.FailureReason}");
}
```

### 7.8 生成 WebView2 宿主页

```csharp
var preview = await kernel.ResolvePreviewAsync(
    "51110209021322000002",
    AcisPreviewIntent.ClickPreview);

if (preview.IsSuccess)
{
    var host = await kernel.BuildPreviewHostAsync(new PreviewHostRequest
    {
        DeviceCode = preview.DeviceCode,
        SourceUrl = preview.PreviewUrl!,
        Protocol = preview.ParsedProtocolType ?? preview.SelectedProtocol,
        Title = "点位预览"
    });

    // WebView2.Source = host.HtmlUri;
    Console.WriteLine(host.HtmlUri);
}
```

### 7.9 高德坐标转换

```csharp
var result = await kernel.ConvertCoordinatesAsync(
[
    new GeoPoint(103.663455m, 29.591510m)
], "baidu");

if (result.IsSuccess)
{
    var point = result.Converted[0];
    Console.WriteLine($"{point.Longitude}, {point.Latitude}");
}
```

### 7.10 主动写诊断日志

```csharp
await kernel.WriteDiagnosticAsync("InspectionPreview", "开始单点预览");
```

---

## 8. 输出文件说明

运行后默认会在 `workDirectory` 下产生这些文件：

```text
.acis-kernel/
  token-cache.json
  acis-kernel.log
  preview-hosts/
    <deviceCode>-flv-xxxx.html
```

说明：

- `token-cache.json`：令牌本地缓存
- `acis-kernel.log`：统一日志
- `preview-hosts/*.html`：用于 WebView2 承载的视频预览宿主页

---

## 9. 当前协议建议

基于你前面已经跑通的真实结论，建议新项目默认这样用：

### 点击预览
`FLV -> HLS -> WebRTC -> H5`

### 后台巡检
`FLV -> HLS`

原因：

- FLV 已经是当前宿主里最接近稳定可承载的主路径
- HLS 可以作为次选
- WebRTC 当前主要问题在宿主承载层，不在签名、解密和 URL 获取层
- H5 只做最后兜底

---

## 10. 你后续重构时最该怎么接

### 推荐接法
- 新项目先只保留这个内核文件
- 所有平台调用都先走这个文件
- UI 不直接写 HttpClient
- ViewModel 不直接写签名、解密、坐标转换
- 日志统一从这里出

### 不推荐接法
- 在 ViewModel 里自己拼平台参数
- 在多个 Service 里重复写 token 逻辑
- 地图页单独再写一套高德转换
- 预览页单独再写一套 FLV/HLS/H5 回退

---

## 11. 已知边界

这份文件已经能作为重构基线，但有几个边界你要知道：

1. WebRTC 这里只做了“URL 获取 + 明确报不支持”  
   没有在单文件里再塞一套完整 WebRTC 宿主播放器。

2. HLS/FLV 宿主页走的是前端播放器方案  
   你后续若要完全离线或内网部署，需要把 `hls.js` / `mpegts.js` 改成你自己的本地静态资源。

3. `GetDeviceCatalogPageAsync` 当前按你现项目主链使用 `/getAllDeviceListNew`  
   如果你后续换环境，要按平台实际接口名改 `Endpoints`。

4. 这份文件是“新项目基础设施收敛版”  
   不是为了 100% 原样替代当前整个 ACIS 解决方案。

---

## 12. 建议你下一步怎么做

最合理的下一步是：

1. 在新仓库先放入这两个文件  
2. 先写一个 50 行以内的控制台探针  
3. 验证四件事：
   - token 能拿到
   - 设备目录能拉到
   - FLV 预览 URL 能拿到
   - 高德坐标能转
4. 这四件事过了，再开始新的 UI/任务中心/地图中台设计

---

## 13. 结论

这次交付的重点不是“再补一个零散工具类”，而是把你项目里最容易反复踩坑的底层能力单独抽出，变成可复用、可移植、可重构的单文件基建。

你后面重新设计开发时，优先复用这个文件，能少掉一大半“平台接入重新踩坑”的时间。
