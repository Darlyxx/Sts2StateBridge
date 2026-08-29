# STS2 MCP Server

独立的《杀戮尖塔 2》MCP Server。它通过本机 HTTP 连接 `Sts2StateBridge` Mod，不包含 LangChain、OpenAI SDK、模型或 API Key。

已有自己 Agent 的用户可以只使用本目录；从 GitHub Release 下载 MCP 包或 clone 完整仓库均可。

```powershell
uv sync
uv run sts2-mcp
```

默认连接 `http://127.0.0.1:38281`。修改端口时设置 `STS2_BRIDGE_URL`。

提供四个只读工具和一个动作工具：

- `get_game_overview`
- `get_combat_state`
- `get_interaction`
- `get_full_snapshot`
- `execute_action(state_id, action_id)`

游戏写操作还必须在 Mod 的本机配置中显式启用。

`get_combat_state` 和 `get_interaction` 会返回当前状态对应的动作候选。`execute_action` 只接受同一最新快照中的 `state_id` 与 `action_id`；当前支持战斗、奖励、卡牌奖励、药水丢弃、宝箱、休息点和锻造操作。

MCP Host 配置中的 `--directory` 必须指向本目录，例如：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "<你的绝对路径>\\mcp\\server",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

Windows JSON 中的反斜杠需要写成 `\\`。
