# Sts2StateBridge

面向《杀戮尖塔 2》AI 的本地 Bridge、标准 MCP Server 与可选 LangChain Agent。项目分为独立三层：已有自己 Agent 的用户只需使用 MCP 套件；想直接聊天的用户可以使用项目附带的 Agent。

## 架构

```text
游戏
  ↕ 本机 HTTP（127.0.0.1:38281）
mcp/mod/Sts2StateBridge        C# 游戏 Mod
  ↕
mcp/server                     独立 stdio MCP Server
  ↕
├─ 你自己的 MCP Host / Agent
└─ agent                       项目附带的 LangChain Agent（可选）
```

```text
.
├─ mcp/
│  ├─ mod/Sts2StateBridge/     # 游戏内 C# Bridge Mod
│  └─ server/                  # 独立 Python MCP Server 与测试
├─ agent/                      # 可选 LangChain/DeepSeek 客户端
├─ scripts/build-release.ps1   # 生成不含游戏 DLL 的 MCP 发布包
├─ RELEASING.md                # 维护者发布流程
├─ .gitignore
├─ LICENSE
└─ README.md
```

关键边界：

- Mod 负责在 Godot 主线程读取状态、校验并执行允许的动作。
- MCP Server 负责把 Bridge 包装成标准工具，不包含模型、LangChain 或 API Key。
- 项目自带 Agent 也只调用 MCP，不再直接访问 Bridge。
- 第三方已有 Agent 时，不需要安装 `agent/`。

## 当前版本与能力

- Mod：`0.10.0`，目标游戏 `v0.111.0`，使用 `.NET 9`
- MCP Server：`0.10.0`，Python 3.11+
- Agent：`0.6.0`，Python 3.11+
- Bridge：`http://127.0.0.1:38281`，只监听本机
- 写操作：默认关闭，必须由本机配置明确开启

读取范围包括角色、构筑、地图、各类非战斗交互、战斗手牌与牌堆、敌人及意图，以及星能、奥斯蒂、充能球等角色机制。所有可决策状态包含 `state_id`，用于防止操作过期状态。

MCP 工具：

- `get_game_overview`：阶段、角色资源与构筑摘要。
- `get_combat_state`：玩家、手牌、牌堆、敌人、意图和合法候选。
- `get_interaction`：地图、奖励、事件、商店、休息点和宝箱。
- `get_full_snapshot`：完整原始快照。
- `execute_action(state_id, action_id)`：执行最新快照中的白名单动作。

前四个工具只读且幂等。`execute_action` 会改变游戏，非幂等，应由 MCP Host 在调用前向用户确认。

## 环境要求

- Windows 与 Steam 版《杀戮尖塔 2》
- .NET SDK 9（构建 Mod 时需要）
- Python 3.11 或更高版本
- [uv](https://docs.astral.sh/uv/)
- PowerShell

## 构建和安装 Mod

先完全退出游戏。游戏程序集只作为本地编译引用，不会复制进构建产物，也不应提交到 Git。

```powershell
dotnet build .\mcp\mod\Sts2StateBridge\Sts2StateBridge.csproj `
  -c Release `
  -p:Sts2ManagedDir="E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
```

将以下文件复制到游戏的 `Mods` 目录：

- `mcp/mod/Sts2StateBridge/bin/Release/net9.0/Sts2StateBridge.dll`
- `mcp/mod/Sts2StateBridge/Sts2StateBridge.json`

启动游戏并启用 Mod，随后可验证：

```powershell
Invoke-RestMethod http://127.0.0.1:38281/health
Invoke-RestMethod http://127.0.0.1:38281/snapshot
```

不要把 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll` 或其他游戏文件放进仓库。

### 启用写操作

写操作默认关闭。需要测试动作时，将 `mcp/mod/Sts2StateBridge/Sts2StateBridge.config.example.json` 复制到游戏 `Mods` 目录，改名为 `Sts2StateBridge.config.json`，将 `write_enabled` 改为 `true`，然后重启游戏。

当前接受快照候选中的战斗动作，以及奖励领取、卡牌奖励、药水丢弃、宝箱、休息点和锻造动作。非战斗候选位于 `interaction.actions`。请求必须同时携带最新的 `state_id` 与 `action_id`；同一个状态只接受一次动作。真实本机配置不会提交到 Git。

药水槽已满时不会直接生成领取药水动作，而会为已有药水生成 `discard_potion` 候选。丢弃后必须重新读取快照，再领取奖励药水。地图、事件、商店和远古遗物目前仍为只读。

## 只使用 MCP（已有自己的 Agent）

### 方式一：clone 仓库

```powershell
git clone <仓库地址>
cd <仓库目录>\mcp\server
uv sync
```

你只需要使用 `mcp/`，无需进入或安装 `agent/`。由于游戏 Mod 也在 `mcp/mod`，clone 后可以自行构建 Mod。

### 方式二：GitHub Release

GitHub Release 提供 `Sts2StateBridge-MCP-v版本.zip`。其中包含编译后的 Mod、独立 MCP Server、安装说明和许可证，不包含项目自带 Agent、游戏 DLL、虚拟环境或 API Key。用户解压后安装其中的 Mod，并在 `server` 目录执行 `uv sync`。Release 适合普通用户；clone 适合需要查看源码、更新或参与开发的用户。

维护者生成发布包和创建 Release 的步骤见 [RELEASING.md](RELEASING.md)。

无论使用哪种方式，MCP Host 配置中的 `--directory` 都必须替换为自己电脑上 `mcp/server` 的绝对路径：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "<替换为你的绝对路径>\\mcp\\server",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

本机当前仓库的配置示例：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "E:\\lhy\\vs code\\slay the spire project\\mcp\\server",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

Windows JSON 中一个 `\` 必须写成 `\\`。`127.0.0.1` 表示每个用户自己的电脑，通常不需要修改；若 Bridge 改了端口，再添加：

```json
"env": {
  "STS2_BRIDGE_URL": "http://127.0.0.1:新端口"
}
```

手动执行 `uv run sts2-mcp` 后没有普通输出是正常的：stdio Server 正在等待 MCP Host 发送协议消息。

## 使用项目自带 Agent

Agent 默认使用 LangChain，通过独立 MCP Server 读取游戏。DeepSeek 或其他 OpenAI 兼容服务只需修改 URL、Key 和模型名。

```powershell
cd agent
Copy-Item .env.example .env
uv sync
uv run sts2-agent
```

`.env` 示例：

```dotenv
LLM_BASE_URL=https://api.deepseek.com
LLM_API_KEY=你的_API_Key
LLM_MODEL=deepseek-v4-flash
LLM_TIMEOUT_SECONDS=60
STS2_MCP_DIRECTORY=
```

完整仓库中 `STS2_MCP_DIRECTORY` 留空即可自动找到 `mcp/server`。单次提问：

```powershell
uv run sts2-agent ask "分析当前局面，推荐这一回合的出牌顺序"
```

固定流程回退模式同样通过 MCP：

```powershell
uv run sts2-agent --simple ask "分析当前局面"
```

在 Python 中调用：

```python
from sts2_agent import Sts2Agent

agent = Sts2Agent.from_env()
answer = agent.ask("现在应该怎么打？")
print(answer.text, answer.phase, answer.state_id)
```

## 依赖与测试

`pyproject.toml` 和 `uv.lock` 是各 Python 子项目的依赖来源。MCP 和 Agent 必须分别安装：

两个目录使用彼此隔离的虚拟环境：独立 Server 使用 MCP SDK 2.x；LangChain 适配器所在的 Agent 环境固定使用其兼容的 MCP SDK 1.x。不要把两个目录的依赖手工合并到同一个虚拟环境。

```powershell
cd mcp\server
uv sync
uv run pytest

cd ..\..\agent
uv sync
uv run pytest
```

不用 uv 时，可以在对应目录创建虚拟环境并运行：

```powershell
python -m pip install -r requirements.txt
```

`requirements.txt` 是从锁文件导出的运行时依赖，不要手工编辑。

## 安全与隐私

- Bridge 只绑定 `127.0.0.1`，不会监听局域网。
- MCP Server 不持有模型 API Key，也不访问模型服务。
- 写操作默认关闭，并受 `state_id`、动作候选白名单和单次消费保护。
- 读取、校验与动作入队均在 Godot 主线程执行。
- 不上传存档、遥测或游戏状态，不暴露未揭示内容。

## 常见问题

- MCP 无法启动：确认 `uv` 已安装并在 PATH 中，且 `--directory` 指向 `mcp/server`。
- 无法连接 Bridge：确认游戏已启动、Mod 已启用，并检查 `/health`。
- 快照返回 `503`：游戏正处于动画或界面切换，稍后重试。
- 动作被拒绝：检查 `write_enabled`，并重新读取快照取得最新 `state_id/action_id`。
- Agent 返回 `401/429`：检查 API Key、模型账户余额和限流。

## 免责声明

本项目是非官方社区项目，与 Mega Crit 或《杀戮尖塔 2》的发行方无关。游戏名称及相关资产归其权利人所有。本仓库不包含游戏程序集或游戏资产。

## License

[MIT](LICENSE) © 2026 Darlyxx
