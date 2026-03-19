---
name: acis-kernel-integration
description: 用于后续接入 ACIS 单文件复用内核，强调其应放在 Infrastructure/Integrations/Acis，UI 不直接调用平台。
---

## 适用场景
- 准备引入 ACIS 单文件复用内核或重构现有平台接入代码。
- 需要划定 token、平台接口、坐标转换、预览地址和宿主页处理边界。
- 需要确保 UI 与平台能力通过服务边界间接协作。

## 本技能的执行步骤
1. 先阅读 `docs/ctyun-api/`，确认当前接入计划和禁止项。
2. 将 ACIS 相关实现限定在 `Infrastructure/Integrations/Acis` 下组织。
3. 通过 `Services` 暴露稳定接口，避免 `UI` 直接依赖平台细节。
4. 把 token、接口调用、解密、坐标转换、预览地址生成和日志封装到 ACIS 边界内。
5. 用占位、适配器或接口先完成边界，再逐步替换为真实接入。

## 应避免的问题
- 在 `UI`、ViewModel 或控件里直接发平台请求。
- 将 ACIS 逻辑散落到多个项目中，导致后续无法统一替换。
- 在没有完成文档确认前编造平台接口细节。
- 把日志和诊断信息展示到 UI。

## 产出物清单
- `Infrastructure/Integrations/Acis` 下的接入骨架。
- 与 `Services` 对接的稳定接口或适配边界。
- 更新后的接入计划或实施说明。

## 验收标准
- ACIS 相关实现只出现在 Infrastructure 约定路径。
- UI 不直接引用平台接入细节。
- 文档、接口和代码边界一致。
