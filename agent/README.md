# STS2 Agent

这是项目附带的可选 LangChain AI 客户端。它不直接访问游戏 HTTP Bridge，而是启动并调用独立的 `mcp/server`，因此项目内置 Agent 与第三方 Agent 使用完全相同的 MCP 工具协议。

## 安装

```powershell
cd agent
Copy-Item .env.example .env
uv sync
```

在 `.env` 中填写模型服务：

```dotenv
LLM_BASE_URL=https://api.deepseek.com
LLM_API_KEY=你的_API_Key
LLM_MODEL=deepseek-v4-flash
LLM_TIMEOUT_SECONDS=60
STS2_MCP_DIRECTORY=
```

从完整仓库运行时，`STS2_MCP_DIRECTORY` 留空即可，Agent 会自动找到同级的 `mcp/server`。如果只复制了 Agent，需把它设为独立 MCP Server 的绝对路径。

## 使用

```powershell
uv run sts2-agent
uv run sts2-agent ask "分析当前局面"
```

`--simple` 保留固定工具调用流程作为回退，但仍然通过 MCP 获取状态：

```powershell
uv run sts2-agent --simple ask "这一回合怎么打？"
```

本客户端需要模型 API Key；独立的 `mcp/server` 不需要 API Key。完整安装、MCP Host 配置和 Mod 部署说明见仓库根目录 README。

Agent 与独立 MCP Server 使用各自的 `.venv` 和锁文件。请分别在两个目录执行 `uv sync`，不要把二者的 MCP SDK 版本强行合并。
