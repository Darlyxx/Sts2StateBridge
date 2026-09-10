# Sts2StateBridge v0.13.0

本版本完成统一选择界面的读取与操作闭环。MCP 工具数量和 `execute_action(state_id, action_id)` 签名保持不变。

## 新增能力

- 战斗临时三选一、卡牌组合、手牌及战斗牌堆单选/多选。
- 选择、取消选择、手动确认、取消和跳过均作为最新快照中的白名单动作提供。
- 选择弹窗存在时不再混入普通出牌、药水或结束回合候选。
- 支持 `NDeckEnchantSelectScreen`，按稳定卡牌实例 ID 完成附魔选牌与预览确认。
- `combat.selection` 新增选择数量、确认/取消可用状态、跳过状态和确认阶段。
- `combat.readiness` 新增当前公开出牌限制；仪式兽的限制通过稳定 Affliction ID `RINGING` 识别。
- 奖励领取完成后，只要“继续”按钮可用就会生成 `proceed`，不再因 `IsComplete` 提前隐藏动作。

## 安全边界

- 每次动作仍会重新捕获快照并校验 `state_id/action_id`。
- 同一 `state_id` 只接受一次动作，随后必须重新读取。
- 选择对象按真实实例 ID 重新定位，并调用游戏原有 UI 处理器。
- 不直接移动卡牌、修改牌堆、附魔、奖励或存档。
- 写操作仍需用户在本机 Mod 配置中显式开启。

## 兼容性

- 目标游戏：Slay the Spire 2 `v0.111.0`。
- Mod 与 MCP Server：`0.13.0`。
- 快照 schema 保持 `2`；新增字段向后兼容。
- Agent 与 Skill 未修改。

本版本先提交代码并进行游戏内监督验收；Release ZIP 和 GitHub Release 在验收通过后另行生成。
