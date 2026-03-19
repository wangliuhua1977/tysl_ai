# 五层职责与依赖方向

## 固定分层
- `Tysl.Ai.App`：启动、组合根、依赖注入。
- `Tysl.Ai.UI`：WPF 视图、ViewModel、主题、控件与交互状态。
- `Tysl.Ai.Services`：服务接口、编排占位、面向 UI 的应用服务。
- `Tysl.Ai.Infrastructure`：外部系统接入占位，后续 CTYun 相关实现统一落在 `Integrations/Acis`。
- `Tysl.Ai.Core`：领域模型、枚举、稳定抽象。

## 依赖方向
- `App -> UI / Services / Infrastructure / Core`
- `UI -> Services / Core`
- `Services -> Core`
- `Infrastructure -> Core`
- `Core -> none`

## 边界约束
- `UI` 不直接写平台调用、签名、解密、坐标转换。
- `Services` 不承载真实 SDK 或平台协议实现。
- `Infrastructure` 不承载页面交互逻辑。
- 新增功能必须优先适配既定五层结构，而不是反推结构为功能让路。
