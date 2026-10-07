# STS2 Agent

这是项目附带的可选 LangChain AI 客户端。它不直接访问游戏 HTTP Bridge，而是启动并调用独立的 `mcp/server`，因此项目内置 Agent 与第三方 Agent 使用完全相同的 MCP 工具协议。

Agent 0.9.0 默认加载仓库内的 `sts2-ironclad-player` Skill。核心规则常驻，基础、战斗、构筑、路线、经济/事件、遭遇和失败恢复七个主题按需加载。新增本地算术分析和对局日志；Mod/MCP 保持 0.13.0，无需重新部署。Skill 是策略提示，不保证通关率。

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

从完整仓库运行时两个路径变量均可留空。自定义 Skill 至少提供有效 UTF-8 `SKILL.md`，入口明确引用的本地文件必须存在；标准场景参考与分析脚本可选，没有脚本时保持文档模式。Agent 的日志实现复用仓库内置 Skill 脚本，因此只复制 Agent 源码不够，建议 clone 完整仓库。第三方 MCP 用户不需安装 Agent。

## 使用

```powershell
uv run sts2-agent
uv run sts2-agent ask "分析当前局面"
```

策略要求只询问、分析或查看状态时不调用写工具；明确要求“开始、继续或完成”后才在授权范围自主执行。每个动作后刷新并核对结果，accepted=true 仅表示被接受。非 IRONCLAD 不应用战士策略。这些是模型提示约束，不是形式化权限保证；MCP 写配置、候选白名单和 state_id 仍是硬边界，首次使用请监督。

其他 OpenAI 兼容工具调用模型只需替换 `LLM_BASE_URL`、`LLM_API_KEY` 和 `LLM_MODEL`。第三方 Agent 可安装整个 Skill 文件夹，并按 `SKILL.md` 的路由按需加载参考；`sources.md` 仅供资料审计。

## Skill 评测

不调用模型的回归测试：

```powershell
uv run pytest
```

检查 34 个固定场景能加载（默认不调用 API、不连接游戏、不写结果）：

```powershell
uv run python evals/run_skill_eval.py
```

用户显式启动真实模型评测才会消耗自己的 API：

```powershell
uv run python evals/run_skill_eval.py --run-model --model 你的模型名 --limit 5
uv run python evals/run_skill_eval.py --run-model
uv run python evals/run_skill_eval.py --run-model --without-skill
```

案例源码为 `evals/cases.py`，固定 JSON 与其一致性由测试检查。场景使用 schema 2 嵌套结构、资源、目标、效果与不含答案的动作 ID，数字是教学条件而非卡牌数据库。评测允许多个合理选择，区分未授权、版本错误、伪造候选、安全错误、策略分歧和 API/格式错误。全部匹配返回0，否则返回1供复核；不是90%胜率门槛。

报告默认保存系统本地数据目录的 `eval-results`，可用 `--output` 指定目录；记录模型、配置、时间、Skill/案例指纹、用量和错误类别，不存密钥、完整回答或推理。默认每例最多1200输出token，可用 `--max-tokens` 调整。相同案例和配置便于比较，但模型输出可能非确定。此集仅测单步决策，不证明整局胜率或真实长期工具循环。策略分歧需人工复核，不为某模型硬改答案。

`--simple` 保留固定工具调用流程作为回退，但仍然通过 MCP 获取状态：

```powershell
uv run sts2-agent --simple ask "这一回合怎么打？"
```

本客户端需要模型 API Key；独立的 `mcp/server` 不需要 API Key。完整安装、MCP Host 配置和 Mod 部署说明见仓库根目录 README。

Agent 与独立 MCP Server 使用各自的 `.venv` 和锁文件。请分别在两个目录执行 `uv sync`，不要把二者的 MCP SDK 版本强行合并。

## 本地工具与计算边界

下列工具属于自带 Agent，不加入独立 MCP 的五个工具：

| 工具 | 用途 |
|---|---|
| `get_current_strategy_guide(topic="auto")` | 一次完整快照与相关参考；基础问题指定 fundamentals；场景/遭遇变化重载 |
| `analyze_current_state()` | 一次完整快照与同版本算术；不选最佳动作 |
| `get_run_journal()` | 当前本机日志编号、摘要与最近20条关键记录 |
| `record_run_decision(...)` | 保存短记录并重新读取游戏；不执行动作 |

分析只汇总已知费用、牌组、意图、选择、预算与已揭示连线。未知伤害保持未知；攻击减格挡不是精确损血，整手伤害不当连招。预算枚举最多12件/256组合，路线最多128条/32节点，截断有标记。分析与候选必须使用关联的同一快照。

自定义 `scripts/analyze_snapshot.py` 若存在，将作为用户配置的本地 Python 模块加载，需提供 `analyze(snapshot)`。**只使用可信脚本，这不是沙箱。** 无脚本时保持文档模式；内置分析仅使用标准库且不联网，自定义代码的安全性需自行检查。

## 本机日志

默认 Windows：`%LOCALAPPDATA%\Sts2StateBridge\journals`；其他平台：`${XDG_DATA_HOME:-~/.local/share}/sts2-state-bridge/journals`。用 `STS2_JOURNAL_DIR` 可替换，不建议放入仓库或共享目录。

首次记录生成本机独立 UUID，不自动查找或合并旧局。类别为 observation（观察）、plan（计划）、judgment（判断）、summary（摘要）。accepted_unverified 不是 observed；程序核对版本来源，不能自动证明文字摘要的语义正确。不记录密钥、完整聊天、快照或内部推理。

```text
/journal
/resume-run <本机日志编号>
/clear
```

`/journal` 不调用模型。恢复先读游戏，角色/进阶冲突、章节/楼层倒退或不在局内则拒绝。无明显冲突仍不能证明同一局，必须由用户选择编号；旧计划不是新授权。`/clear` 清空聊天与缓存并解除日志关联，不删除磁盘历史。开始新局先清空，继续旧局显式恢复；摘要追加保存，不覆盖旧记录。

模型读取日志或恢复后的下一次问答会把日志内容发送给所配置的模型服务，勿存敏感信息。常见凭据及当前配置密钥会被拒绝，但不保证识别所有秘密。崩溃遗留 `.lock` 时，请先确认没有写入进程，再手工清理对应锁；程序不自动删除历史。日志不自动变成永久 Skill 规则。

## 第三方 Host 与独立脚本

复制完整 [Skill 文件夹](../skills/sts2-ironclad-player/SKILL.md)。能读文件的 Host 按入口路由加载；能执行 Python 的 Host 可用脚本。只能调用 MCP、不能读文件/执行脚本的 Host，需要用户手动注入入口和相关文档，不能声称自动加载。

从仓库根目录，已持有 `snapshot.json` 时：

```powershell
Get-Content -Raw -Encoding utf8 snapshot.json | python skills/sts2-ironclad-player/scripts/analyze_snapshot.py
Get-Content -Raw -Encoding utf8 snapshot.json | python skills/sts2-ironclad-player/scripts/journal.py new
python skills/sts2-ironclad-player/scripts/journal.py show <日志编号>
Get-Content -Raw -Encoding utf8 snapshot.json | python skills/sts2-ironclad-player/scripts/journal.py check-resume <日志编号>
```

分析只收 stdin JSON、输出 JSON，不访问游戏或网络。日志脚本只写本机目录；`record <编号>` 也从 stdin 接受 JSON，例如：

```json
{"source_state_id":"来源版本","category":"plan","summary":"下一层优先篝火，进入前重评生命","status":"proposed"}
```

核对结果后可用 status=observed 与 observed_state_id；独立脚本不能验证版本真来自游戏，调用者负责。`--directory` 放在日志子命令前。临时快照与输入文件避免提交 Git。

## 传统 pip 安装与依赖导出

在 agent 目录：

```powershell
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pip install --no-deps -e .
sts2-agent --help
```

依赖来源仍是 pyproject.toml / uv.lock；更新后执行，不手改 requirements：

```powershell
uv export --no-dev --no-emit-project --format requirements-txt --output-file requirements.txt
```
