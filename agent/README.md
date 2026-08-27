# STS2 Agent

从本地 `Sts2StateBridge` 读取只读游戏快照，并通过 LangChain 工具 Agent 交给 OpenAI 兼容模型分析。默认提供四个只读工具，并保留 `--simple` 固定流程作为回退。详细使用说明见项目根目录 README。
