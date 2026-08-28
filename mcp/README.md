# STS2 MCP 套件

该目录是可以独立于项目内置 Agent 使用的 MCP 套件：

```text
mcp/
├─ mod/Sts2StateBridge/  # 安装到游戏的 C# Bridge Mod 源码
└─ server/               # 标准 stdio MCP Server
```

已有自己的 Agent 或 MCP Host 的用户只需安装游戏 Mod，并安装、配置 `server/`，无需安装仓库中的 `agent/`。

- `server` 的四个状态工具只读取游戏。
- `execute_action` 会改变游戏，且只有 Mod 的本机写配置显式开启时才可用。
- MCP Server 不包含模型、不需要 API Key，也不依赖 LangChain。

具体安装与配置见根目录 README 和 [server/README.md](server/README.md)。
