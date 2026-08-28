# Sts2StateBridge v0.9.0

首个可分发的 MCP 套件版本。

## 主要功能

- 提供只监听 `127.0.0.1` 的《杀戮尖塔 2》游戏 Bridge Mod。
- 提供独立 stdio MCP Server，不需要模型 API Key 或 LangChain。
- 提供游戏概览、战斗状态、非战斗交互和完整快照读取工具。
- 支持带 `state_id` 校验的候选战斗动作。
- 支持星能、奥斯蒂和充能球等角色专属战斗机制。
- 写操作默认关闭，必须通过本机配置明确启用。

## 下载选择

- 已有自己的 Agent 或 MCP Host：下载 `Sts2StateBridge-MCP-v0.9.0.zip`。
- 希望使用项目自带 LangChain Agent 或参与开发：clone 完整仓库。

## 兼容性

- 目标游戏版本：STS2 `v0.111.0`
- Mod 运行时：.NET 9
- MCP Server：Python 3.11+ 与 uv

请阅读压缩包内 README 完成 Mod 安装和 MCP Host 配置。本项目不包含任何游戏程序集或游戏资产。
