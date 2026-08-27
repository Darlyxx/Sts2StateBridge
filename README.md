# Sts2StateBridge

`Sts2StateBridge` 是一个面向《杀戮尖塔 2》AI Agent 的本地只读状态桥接 Mod。

Mod 在游戏进程内读取当前状态，并通过仅监听本机的 HTTP 接口输出 JSON。当前版本不会执行出牌、选择奖励或其他游戏操作。

## 当前状态

- Mod 版本：`0.7.0`
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
├─ .gitignore
├─ LICENSE
└─ README.md
```

后续版本计划在根目录增加独立的 Python 客户端与 MCP Server。游戏 Mod、Python 层和 AI 策略层将保持分离。

## 环境要求

- Windows
- 《杀戮尖塔 2》Steam 版本
- .NET SDK 9
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
  "bridge_version": "0.7.0",
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

## 安全与隐私

- 服务只绑定 `127.0.0.1`，不会监听局域网地址。
- 当前版本没有 POST 动作接口，`write_enabled=false`。
- 游戏对象读取统一在 Godot 主线程执行。
- 不上传存档、遥测或游戏状态，不访问外部网络。
- 未揭示的地图节点和未知抽牌顺序不会暴露给 AI。

## 路线图

1. 完善只读协议文档与版本兼容测试。
2. 增加 Python 客户端和紧凑的 Agent 视图。
3. 封装 MCP Server，只提供读取工具。
4. 在 `state_id`、严格 readiness 和请求幂等保护下加入安全动作接口。
5. 增加自动化测试、发布包和版本迁移说明。

## 免责声明

本项目是非官方社区项目，与 Mega Crit 或《杀戮尖塔 2》的发行方无关。游戏名称及相关资产归其权利人所有。本仓库不包含游戏程序集或游戏资产。

## License

[MIT](LICENSE) © 2026 Darlyxx
