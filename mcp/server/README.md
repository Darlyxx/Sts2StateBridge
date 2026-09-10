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

`get_combat_state` 和 `get_interaction` 会返回当前状态对应的动作候选。`execute_action` 只接受同一最新快照中的 `state_id` 与 `action_id`；当前还支持临时选牌、多选确认、取消/跳过及卡牌附魔选择。

战斗快照 schema 保持版本 2；`0.13.0` 在向后兼容的基础上包含：

- `readiness`：当前玩家回合阶段、输入锁定、动画和选择弹窗状态。
- `selection`：卡牌药水、卡牌组合、战斗牌堆、手牌及局外附魔的候选和选择进度。
- `selection:*` 动作：选择/取消选择实例、确认、取消或跳过；每一步都必须重新读取 `state_id`。
- `readiness.cards_playable_remaining`：使用稳定 Affliction ID 识别当前公开的出牌次数限制。
- 卡牌类型、稀有度、关键词、动态费用、动态数值、附魔与异变信息。
- 敌人的可见状态、结构化意图效果，以及基于当前公开信息计算的来袭伤害汇总。

这些字段不包含未知抽牌顺序、隐藏随机结果或敌人尚未显示的后续行动。

地图动作只包含当前可达节点；事件动作只包含可见且未锁定的选项；商店动作只包含有库存且金币足够的商品。打开/关闭商店、进入删牌选择、确认删牌和离开商店房间也各自是独立动作，每次执行后都必须重新读取最新 `state_id`。

卡牌结果展示页会返回 `proceed`，对应“√”确认按钮。商店中的“空值”是游戏内合法卡牌 `Null`，不是解析失败。

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
