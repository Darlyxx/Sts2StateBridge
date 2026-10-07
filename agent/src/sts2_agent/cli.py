from __future__ import annotations

import argparse
import json
import sys

from .agent import Sts2Agent
from .agent_types import LlmError
from .config import ConfigurationError
from .mcp_client import McpClientError
from .simple_agent import SimpleSts2Agent


HELP = """命令：
  /snapshot  显示将发送给模型的精简状态
  /refresh   重新读取并显示阶段和 state_id
  /clear     清除聊天与策略缓存、解除日志关联（不删除历史）
  /journal   查看本机日志编号与近期关键记录（不调用模型）
  /resume-run <id>  显式恢复日志，先读取游戏核对；不授予动作权限
  /help      显示帮助
  /quit      退出
普通文字会连同最新游戏状态一起发送给模型。"""


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="读取 STS2 状态并向 OpenAI 兼容模型提问")
    parser.add_argument("--simple", action="store_true", help="使用固定单快照流程，不启用 LangChain 工具 Agent")
    parser.add_argument("--full-state", action="store_true", help="simple 模式下向模型发送完整原始快照")
    subparsers = parser.add_subparsers(dest="command")
    ask = subparsers.add_parser("ask", help="单次提问")
    ask.add_argument("question")
    ask.add_argument("--full-state", action="store_true", dest="ask_full_state")
    return parser


def _print_stream(agent, question: str, full_state: bool) -> None:
    def tool_notice(name: str) -> None:
        labels = {
            "get_game_overview": "游戏概览",
            "get_combat_state": "战斗状态",
            "get_interaction": "当前交互",
            "get_full_snapshot": "完整快照",
            "execute_action": "游戏动作",
            "get_current_strategy_guide": "当前场景策略",
            "analyze_current_state": "快照算术分析",
            "get_run_journal": "本机对局日志",
            "record_run_decision": "关键决策记录",
        }
        verb = "执行" if name == "execute_action" else "保存" if name == "record_run_decision" else "读取"
        print(f"\n[正在{verb}{labels.get(name, name)}...]\n")

    state, chunks = agent.ask_stream(question, full_state=full_state, on_tool_call=tool_notice)
    print()
    for text in chunks:
        print(text, end="", flush=True)
    print(f"\n\n[阶段: {state.get('phase', 'unknown')} | state_id: {state.get('state_id', 'none')}]\n")


def run_repl(agent, full_state: bool, simple: bool) -> int:
    mode = "simple" if simple else "LangChain"
    print(f"STS2 Agent 已启动（{mode} 模式）。输入 /help 查看命令，输入问题开始分析。")
    while True:
        try:
            question = input("\n你> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\n已退出。")
            return 0
        if not question:
            continue
        if question == "/quit":
            return 0
        if question == "/help":
            print(HELP)
            continue
        if question == "/clear":
            agent.clear_history()
            print("已清除聊天与策略缓存，并解除日志关联；磁盘历史未删除。")
            continue
        try:
            if question == "/journal" or question.startswith("/resume-run"):
                if simple:
                    print("simple 模式不支持对局日志，请使用默认 LangChain 模式。")
                else:
                    try:
                        if question == "/journal":
                            result = agent.get_journal()
                        else:
                            parts = question.split()
                            if len(parts) != 2 or parts[0] != "/resume-run":
                                print("用法：/resume-run <日志编号>")
                                continue
                            result = agent.resume_run(parts[1])
                        print(json.dumps(result, ensure_ascii=False, indent=2))
                    except (ValueError, OSError):
                        print("日志不可读、编号无效或与当前游戏不匹配；未恢复旧计划。", file=sys.stderr)
            elif question == "/snapshot":
                print(json.dumps(agent.snapshot(full_state=full_state), ensure_ascii=False, indent=2))
            elif question == "/refresh":
                state = agent.snapshot(full_state=full_state)
                print(f"阶段: {state.get('phase', 'unknown')} | state_id: {state.get('state_id', 'none')}")
            elif question.startswith("/"):
                print("未知命令。输入 /help 查看可用命令。")
            else:
                _print_stream(agent, question, full_state)
        except (McpClientError, LlmError) as exc:
            print(f"错误：{exc}", file=sys.stderr)
    return 0


def main() -> int:
    args = build_parser().parse_args()
    try:
        agent = SimpleSts2Agent.from_env() if args.simple else Sts2Agent.from_env()
        if args.command == "ask":
            _print_stream(agent, args.question, args.full_state or args.ask_full_state)
            return 0
        return run_repl(agent, args.full_state, args.simple)
    except (ConfigurationError, McpClientError, LlmError) as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
