# CTYun API 文档目录

本目录用于真实 ACIS / CTYun 接入的文档基线。

## 当前文档

- `AcisApiKernel_Usage_Manual.md`
  - ACIS 单文件内核的调用说明
- `codex-context-min.md`
  - 当前轮次的压缩上下文
- `integration-plan.md`
  - 真实 ACIS 内核接入计划与本轮落点

## 当前状态

- 第 3 轮已开始接入真实 ACIS 内核
- 平台设备权威源主路径已切换到真实 ACIS 平台设备提供器
- 若缺失 `configs/acis-kernel.json`，应用进入受控降级模式

## 使用约束

- 涉及 CTYun、ACIS、坐标转换、预览地址或日志封装的变更前，先阅读本目录文档
- 不要绕过 `AcisApiKernel` 重新实现 token、签名、解密、坐标转换或预览回退逻辑
