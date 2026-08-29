# Sts2StateBridge MCP 安装包

这个压缩包面向已经拥有自己 Agent 或 MCP Host 的用户，不包含项目自带的 LangChain Agent。

## 1. 安装 Mod

完全关闭游戏，将 `mod/` 中的以下文件复制到《杀戮尖塔 2》的 `Mods` 目录：

- `Sts2StateBridge.dll`
- `Sts2StateBridge.json`

启动游戏、启用 Mod 并重启游戏。默认只读。

如需允许动作接口，将 `Sts2StateBridge.config.example.json` 复制到游戏 `Mods` 目录，改名为 `Sts2StateBridge.config.json`，并将 `write_enabled` 设为 `true`。只有充分理解风险后才应启用。

当前写操作覆盖战斗、奖励领取、卡牌奖励、药水丢弃、宝箱、休息点和锻造。每次只能执行最新状态候选中的一个动作。

## 2. 安装 MCP Server

需要 Python 3.11+ 和 uv：

```powershell
cd server
uv sync
```

## 3. 配置 MCP Host

把路径替换为解压后 `server` 目录的绝对路径：

```json
{
  "mcpServers": {
    "sts2": {
      "command": "uv",
      "args": [
        "--directory",
        "<解压目录>\\server",
        "run",
        "sts2-mcp"
      ]
    }
  }
}
```

Windows JSON 路径中的 `\` 必须写成 `\\`。默认 Bridge 地址为 `http://127.0.0.1:38281`。

本项目是非官方社区项目，不包含游戏程序集或游戏资产。
