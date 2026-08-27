# STS2 Agent

从本地 `Sts2StateBridge` 读取只读游戏快照，并通过 LangChain Agent 或标准 MCP Server 提供给 AI。`sts2-agent` 是内置模型客户端，`sts2-mcp` 是供外部 MCP Host 使用的 stdio 服务器；两者共享四个只读状态工具。详细使用说明见项目根目录 README。
