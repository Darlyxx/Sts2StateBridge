# STS2 Agent

这是项目附带的可选 LangChain AI 客户端。它不直接访问游戏 HTTP Bridge，而是启动并调用独立的 `mcp/server`，因此项目内置 Agent 与第三方 Agent 使用完全相同的 MCP 工具协议。

Agent 0.7.0 默认加载仓库内的 `sts2-ironclad-player` Skill。它面向单人进阶 10 战士，提供战斗推演、构筑、路线、商店和篝火策略。Skill 只是模型策略层；游戏状态、数值和可执行候选仍以 MCP 为准。

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
STS2_SKILL_PATH=
```

从完整仓库运行时两个路径变量均可留空，Agent 会自动找到同级的 `mcp/server` 和内置 Skill。如果只复制了 Agent，需设置 MCP 路径，并将 `STS2_SKILL_PATH` 指向包含 `SKILL.md` 与 `references/ironclad-playbook.md` 的 Skill 目录。

## 使用

```powershell
uv run sts2-agent
uv run sts2-agent ask "分析当前局面"
```

只询问、分析或查看状态时不会调用写工具。明确要求“开始、继续或完成”战斗、房间或爬塔后，Agent 才能在授权范围内自主执行；每个动作后必须读取新状态。首次读取到的角色不是 `IRONCLAD` 时，战士策略不得应用。

其他 OpenAI 兼容廉价模型只需替换 `LLM_BASE_URL`、`LLM_API_KEY` 和 `LLM_MODEL`。若第三方 Agent 已经能调用 MCP，也可直接把仓库中的 `skills/sts2-ironclad-player/SKILL.md` 与 `references/ironclad-playbook.md` 合并到其系统提示；`sources.md` 仅供资料审计，不必发送给模型。

`--simple` 保留固定工具调用流程作为回退，但仍然通过 MCP 获取状态：

```powershell
uv run sts2-agent --simple ask "这一回合怎么打？"
```

本客户端需要模型 API Key；独立的 `mcp/server` 不需要 API Key。完整安装、MCP Host 配置和 Mod 部署说明见仓库根目录 README。

Agent 与独立 MCP Server 使用各自的 `.venv` 和锁文件。请分别在两个目录执行 `uv sync`，不要把二者的 MCP SDK 版本强行合并。
