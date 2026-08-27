# Sts2StateBridge

`Sts2StateBridge` 是一个面向《杀戮尖塔 2》AI Agent 的本地只读状态桥接 Mod。

Mod 在游戏进程内读取当前状态，并通过仅监听本机的 HTTP 接口输出 JSON。当前版本不会执行出牌、选择奖励或其他游戏操作。

## 当前状态

- Mod 版本：`0.8.0`
- 兼容目标：《杀戮尖塔 2》`v0.111.0`
- 运行时：`.NET 9`
- HTTP 地址：`http://127.0.0.1:38281`
- 写操作：禁用

当前可读取：

- 主菜单、局内、战斗和非战斗阶段
- 角色、生命、金币、楼层、章节和进阶等级
- 完整牌组、升级和附魔
- 遗物、药水、Power、手牌和战斗牌堆
- 敌人生命、状态和意图伤害
- 地图节点、连线、当前位置和可达路线
- 战斗奖励、卡牌奖励、宝箱、事件、远古遗物、休息点和商店
- 当前合法的只读动作候选与防过期 `state_id`

## 项目结构

```text
.
├─ mod/
│  └─ Sts2StateBridge/       # 游戏内 C# Mod
├─ agent/                     # Python AI 分析客户端
├─ .gitignore
├─ LICENSE
└─ README.md
```

游戏 Mod、Python 层和未来的 MCP Server 保持分离。

## 环境要求

- Windows
- 《杀戮尖塔 2》Steam 版本
- .NET SDK 9
- Python 3.11 或更高版本
- uv
- PowerShell

验证 SDK：

```powershell
dotnet --version
```

## 构建

游戏程序集只作为本地编译引用，不会复制进构建产物，也不应提交到 Git。

```powershell
dotnet build .\mod\Sts2StateBridge\Sts2StateBridge.csproj `
  -c Release `
  -p:Sts2ManagedDir="E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

成功后 DLL 位于：

```text
mod/Sts2StateBridge/bin/Release/net9.0/Sts2StateBridge.dll
```

## 安装

1. 正常退出游戏。
2. 将以下两个文件复制到游戏的 `Mods` 目录：
   - `Sts2StateBridge.dll`
   - `mod/Sts2StateBridge/Sts2StateBridge.json`
3. 启动游戏并启用 `Sts2StateBridge`。
4. 重启游戏使 Mod 生效。

不要将 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll` 或其他游戏文件复制到本项目或上传到 GitHub。

## HTTP API

### 健康检查

```powershell
Invoke-RestMethod http://127.0.0.1:38281/health
```

示例：

```json
{
  "ok": true,
  "bridge": "Sts2StateBridge",
  "bridge_version": "0.8.0",
  "game_version_target": "v0.111.0",
  "write_enabled": false
}
```

### 游戏快照

```powershell
Invoke-RestMethod http://127.0.0.1:38281/snapshot
```

快照的主要区域：

- `run`：整局长期状态与构筑
- `combat`：当前战斗状态；非战斗时为 `null`
- `interaction`：地图、奖励、事件、商店等当前非战斗选择
- `state_id`：当前决策状态的短期指纹

客户端必须把索引视为仅对当前快照有效。未来提交操作时必须携带对应的 `state_id`，避免使用过期状态。

### 角色专属战斗机制

`combat.player.mechanics` 是可组合的机制列表，当前支持 `stars`、`osty` 和 `orbs`。它描述玩家此刻实际拥有的能力，而不是只根据 `run.character_id` 推断；因此其他角色通过卡牌、遗物或 Mod 获得相应机制时也能被读取。没有专属机制时返回空数组。

手牌与战斗牌堆中的卡牌同时提供 `star_cost` 和 `costs_star_x`。`star_cost` 是当前效果修正后的实际星能费用；不使用星能或具有 X 星能费用时为 `null`。

专属机制及卡牌星能费用均参与 `state_id`，资源、奥斯蒂状态或充能球队列发生变化时会生成新的状态指纹。

## 安全与隐私

- 服务只绑定 `127.0.0.1`，不会监听局域网地址。
- 当前版本没有 POST 动作接口，`write_enabled=false`。
- 游戏对象读取统一在 Godot 主线程执行。
- 不上传存档、遥测或游戏状态，不访问外部网络。
- 未揭示的地图节点和未知抽牌顺序不会暴露给 AI。

## AI 客户端

Python 客户端默认使用 LangChain 只读工具 Agent。模型会根据问题选择游戏概览、战斗状态、当前交互或完整快照工具；每次工具调用都会读取最新 `/snapshot`。它不会控制游戏，也不会向本地 Bridge 写入数据。

### 配置 DeepSeek

先进入 Python 项目并创建本地配置：

```powershell
cd agent
Copy-Item .env.example .env
```

打开 `agent/.env`，填入自己的 Key：

```dotenv
LLM_BASE_URL=https://api.deepseek.com
LLM_API_KEY=你的_DeepSeek_API_Key
LLM_MODEL=deepseek-v4-flash
LLM_TIMEOUT_SECONDS=60
STS2_BRIDGE_URL=http://127.0.0.1:38281
```

`.env` 已被 Git 忽略，不要把真实 Key 写入 `.env.example`、源码、截图或提交记录。其他 OpenAI 兼容服务只需更换 URL、Key 和模型名。

安装锁定依赖：

```powershell
uv sync
```

不用 uv 的用户可以使用自动生成的 `requirements.txt`：

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
```

`requirements.txt` 来自 `pyproject.toml` 和 `uv.lock`，不要手工修改。依赖变化后由维护者重新生成：

```powershell
uv export --format requirements-txt --no-dev --no-hashes --default-index https://pypi.org/simple --output-file requirements.txt
```

### 在 VS Code 终端提问

先启动游戏并启用 Mod，然后运行：

```powershell
uv run sts2-agent
```

直接输入问题即可。可用命令包括 `/snapshot`、`/refresh`、`/clear`、`/help` 和 `/quit`。单次提问可以使用：

```powershell
uv run sts2-agent ask "分析当前局面，推荐这一回合的出牌顺序"
```

默认 Agent 可自主调用四个只读工具：`get_game_overview`、`get_combat_state`、`get_interaction` 和 `get_full_snapshot`。工具循环有次数上限，终端只显示工具读取提示与最终回答，不展示内部推理。

如 DeepSeek 的工具调用暂时不可用，可切换到原有固定流程：

```powershell
uv run sts2-agent --simple
uv run sts2-agent --simple ask "分析当前局面"
```

完整原始状态开关仅用于 simple 模式：

```powershell
uv run sts2-agent --simple --full-state
```

### 在 Python 代码中调用

```python
from sts2_agent import Sts2Agent

agent = Sts2Agent.from_env()
answer = agent.ask("现在应该怎么打？")
print(answer.text)
print(answer.phase, answer.state_id)
```

固定流程也可以从 Python 调用：

```python
from sts2_agent import SimpleSts2Agent

agent = SimpleSts2Agent.from_env()
```

## MCP Server

`sts2-mcp` 是供外部 AI Host 使用的标准只读 MCP Server。它通过 stdio 通信，不监听新端口、不需要模型 API Key，也不包含 LangChain；使用哪个模型以及何时调用工具由 Codex、Claude Desktop、VS Code 扩展等 Host 决定。

提供的工具：

- `get_game_overview`：阶段、角色资源和构筑摘要。
- `get_combat_state`：战斗、手牌、敌人、意图和合法候选。
- `get_interaction`：地图、奖励、事件、商店、休息点和宝箱。
- `get_full_snapshot`：完整原始快照，仅在其他工具信息不足时使用。

手动启动命令：

```powershell
cd agent
uv sync
uv run sts2-mcp
```

手动启动后没有普通终端输出是正常现象：服务器正在等待 MCP Host 通过 stdin 发送协议消息。

### 我们自己的 MCP 配置

当前项目在本机的实际配置为：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "E:\\lhy\\vs code\\slay the spire project\\agent",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

### 其他用户的 MCP 配置

下载本项目后，必须把下面的占位内容替换成自己电脑上的项目绝对路径：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "<替换为你下载本项目后的绝对路径>\\agent",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

Windows JSON 路径中的一个反斜杠必须写成两个 `\\`。例如实际路径 `D:\Projects\Sts2StateBridge\agent` 在 JSON 中应写成：

```json
"D:\\Projects\\Sts2StateBridge\\agent"
```

`127.0.0.1:38281` 始终表示使用者自己的电脑和游戏，普通用户不需要修改。如果本地 Mod 改用了其他端口，可以在服务器配置中增加：

```json
"env": {
  "STS2_BRIDGE_URL": "http://127.0.0.1:新端口"
}
```

MCP 工具每次调用都会读取最新状态，并返回对应的 `phase` 和 `state_id`。当前所有工具都明确标记为只读、非破坏和幂等。

### 常见问题

- 无法连接本地桥接器：确认游戏已启动、Mod 已启用，并检查 `/health`。
- 快照返回 `503`：游戏正处于界面切换或动画中，稍后重试。
- API 返回 `401`：检查 `.env` 中的 Key。
- API 返回 `429`：检查账户余额或等待限流恢复。
- 请求超时：检查网络，或适当提高 `LLM_TIMEOUT_SECONDS`。
- Agent 达到最大调用次数：缩小问题范围，或使用 `--simple` 回退模式。

运行离线测试：

```powershell
uv run pytest
```

## 路线图

1. 完善只读协议文档与版本兼容测试。
2. 完善 Python 客户端和紧凑的 Agent 视图。
3. 完善 MCP Host 配置与兼容性验证。
4. 在 `state_id`、严格 readiness 和请求幂等保护下加入安全动作接口。
5. 增加自动化测试、发布包和版本迁移说明。

## 免责声明

本项目是非官方社区项目，与 Mega Crit 或《杀戮尖塔 2》的发行方无关。游戏名称及相关资产归其权利人所有。本仓库不包含游戏程序集或游戏资产。

## License

[MIT](LICENSE) © 2026 Darlyxx
