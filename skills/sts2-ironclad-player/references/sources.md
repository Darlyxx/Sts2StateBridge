# 来源台账与证据限制

核对日期：2026-10-06。Skill 0.9.0，目标 STS2 v0.111.0。此文件供维护/人工核查，不常驻模型提示。
结论分类：规则事实、版本相关事实、专家经验、统计倾向、项目实测。手册决策方法若无直接一手证据，明确作为“项目策略综合/判断”，不伪装成上述来源的原话或结论。

## 已核对来源

| ID | 作者、资料与链接 | 日期/版本 | 此次取得的证据与使用边界 |
|---|---|---|---|
| G1 | Mega Crit，[Beta v0.111.0](https://store.steampowered.com/news/app/2868840/view/671751488532383386) | 目标补丁；发布日此次未提取 | 官方标题/入口可核对；正文抓取只返回页面框架，未把具体平衡数值写入手册。 |
| G2 | Wiki 社区，[Ironclad](https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2%3AIronclad) | 动态页面，非 v0.111.0 冻结快照 | 已读角色身份和卡池段落。当前页面说明 Corruption 的特殊来源，故不将其默认列为普通奖励必拿组件。其他精确数值以 MCP 为准。 |
| G3 | Wiki 社区，[Queen](https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2%3AQueen) | Beta 页面，含 v0.109.0 等历史 | 已读锁链/Bound、伴随敌人和阶段关系；仅提供不带固定伤害的公开机制摘要。Wiki 不是官方实现证明，遇冲突复核。 |
| G4 | Wiki 社区，[Buffs](https://slaythespire.wiki.gg/wiki/Buffs_%28Slay_the_Spire_2%29) | 动态页面 | 已核对 Personal Hive、Reattach、Sandpit 等公开描述，用于提示污染、复活、倒计时机制；未把整页当目标版本百科。 |
| G5 | Wiki 社区转载 Mega Crit，[v0.107.1](https://slaythespire.wiki.gg/wiki/Slay_the_Spire_2%3AV0.107.1_-_Major_Update_2) | 早于目标版本；二手补丁转载 | 取得 Doormaker 被替换说明；用于排除旧攻略，不据此写新 Boss 完整招式。官方正文未在此次独立复核，可靠性低于直接公告。 |
| S1 | Spire Codex，[Ironclad Tier List Guide](https://spire-codex.com/guides/ironclad-tier-list) | 2026-07-15，main branch | 已读正文。作者说明来自社区提交，且某高位卡受多人数据影响；本项目不采纳数值排名。 |
| R1 | brendon-colburn，[SLAI 仓库](https://github.com/brendon-colburn/slai) / [归属声明](https://raw.githubusercontent.com/brendon-colburn/slai/main/ATTRIBUTION.md) | 动态 main；2026-10-06 核对 | 已读 README、Skill/脚本结构与声明；借鉴分层、轻量分析、对局日志概念，独立编写。作者明确未获 Baalorlord 审阅或背书。它是只读 coach，接口不等同本项目 MCP。 |
| P1 | Darlyxx 项目监督对局与当前 schema 2 | Bridge/MCP 0.13.0，目标 v0.111.0 | 用户反馈：重复纯格挡、反伤未核算、临时选牌与继续界面、锁链/出牌额度。缺少完整录屏/种子，不宣称统计证明；重建固定案例验证边界。 |

## 一手教学：证据完整性

- E1：Baalorlord，[Every Ironclad Card in Slay the Spire 2](https://www.youtube.com/watch?v=POaEE_3GDBQ)，2026-05-26。核对到其官方频道的视频标题、作者、日期与说明，**没有取得可逐条验证的完整字幕/时间戳**；不声称本手册已经复述其完整教学或得到认可。
- E2：Caleb Gannon Gaming，[How To Win With the Ironclad](https://www.youtube.com/watch?v=b_K0WRmNgwY)。检索到社区引用及视频链接，直接获取遇到限流；发布日、精确内容和适用版本待核实，不给未经核验的具体建议署名。
- 旧来源中的 Jorbs 二手报道、NaveGreed/vmService 排名不能代替一手逐段核对。本次不据此宣称专家共识。后续补教学材料须记录原链接、时间戳、原意、版本和适用例外。
- 教学视频无法完整取得不阻止使用本地原创条件框架，但这是调研覆盖限制，不得宣传“完整吸收大师策略”。

## 统计审计

S1 的过滤条件是混合社区主分支数据，**本次没有取得可复验的 v0.111.0 + A10 + Solo 样本与样本量**。因此未引用胜率、排行或精确评分，也不把“社区高胜率”当行动规则。动态数据入口：[卡牌统计](https://spire-codex.com/tier-list/cards?color=ironclad)。

以后引入统计必须记录抓取日期、版本/时间窗、进阶、Solo 条件、出现/选择样本数、估计方法，并讨论获得时间、玩家水平、上传选择、幸存者、多版本和多人混杂。缺少样本量则只能标为不可量化先验。

## 维护与使用

不复制完整卡牌数值或百科；参考公开规律与事件可能结果是允许的，本局隐藏 RNG/未揭示实际结果不是公开知识。当前快照与先验冲突时以快照为准并记差异。例子中的数字为人为教学场景，不代表当前补丁卡牌基础值。

许可证边界：不复制 R1 代码、固定评分或成段知识库；本项目脚本和文字独立编写。上述第三方名称、素材及原文属于原作者；不暗示官方合作。参考链接可随网站变化失效，自动测试只验证本地引用，不能证明远端内容永远可用。
