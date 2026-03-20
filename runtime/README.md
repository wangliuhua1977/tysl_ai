# runtime 目录说明

- `runtime/.acis-kernel/`
  - ACIS 内核运行目录。
  - 运行期可能生成 `token-cache.json`、`acis-kernel.log`、预览宿主页等文件。
  - 仅本地使用，不应提交到仓库。
- `runtime/snapshots/`
  - 静默巡检截图留痕目录。
  - 按日期分目录落盘，占位截图与摘要说明都在这里生成。
  - 仅运行期产物，不应提交到仓库。

首次启动时目录可为空，程序会按需自动创建。
