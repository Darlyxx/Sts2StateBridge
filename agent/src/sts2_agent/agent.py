from __future__ import annotations

from collections.abc import Callable, Iterator
from typing import Any

from langchain.agents import create_agent
import asyncio
import json

from langchain_core.messages import AIMessage, AIMessageChunk, BaseMessage, HumanMessage, ToolMessage
from langchain_openai import ChatOpenAI
from langchain_core.callbacks import BaseCallbackHandler

from .agent_types import AgentAnswer, LlmError, friendly_llm_error
from .config import Settings
from .mcp_client import Sts2McpClient, McpClientError, _structured_result
from .prompts import SYSTEM_PROMPT
from .skill_loader import SkillLibrary
from .strategy_tool import StrategyGuideSession, create_strategy_guide_tool
from .local_tools import LocalSession, create_local_tools


def _message_text(message: BaseMessage) -> str:
    if isinstance(message.content, str):
        return message.content
    parts: list[str] = []
    for block in message.content:
        if isinstance(block, str):
            parts.append(block)
        elif isinstance(block, dict) and block.get("type") in {"text", "output_text"}:
            parts.append(str(block.get("text", "")))
    return "".join(parts)


class _ObserveReads(BaseCallbackHandler):
    def __init__(self, session):
        self.session = session

    def on_tool_end(self, output, **kwargs):
        try:
            value = _structured_result(output, "read")
            if "accepted" not in value and "error" not in value:
                self.session.observe(value)
        except (McpClientError, TypeError, ValueError):
            pass


class Sts2Agent:
    """LangChain tool-calling agent backed exclusively by the STS2 MCP Server."""

    recursion_limit = 40

    def __init__(
        self,
        settings: Settings,
        *,
        mcp_client: Sts2McpClient | None = None,
        tools: list[Any] | None = None,
        model: Any = None,
        graph: Any = None,
    ) -> None:
        self.settings = settings
        self.mcp_client = mcp_client or Sts2McpClient(settings.mcp_directory, settings.bridge_url)
        self.tools = tools
        self.local_session = LocalSession(settings, self.mcp_client)
        self.read_observer = _ObserveReads(self.local_session)
        self.strategy_session: StrategyGuideSession | None = None
        if graph is None:
            self.tools = self.tools or asyncio.run(self.mcp_client.get_langchain_tools())
            model = model or ChatOpenAI(
                model=settings.model,
                api_key=settings.api_key,
                base_url=settings.base_url,
                timeout=settings.timeout_seconds,
                max_retries=2,
                streaming=True,
            )
            skill = SkillLibrary(settings.skill_path)
            self.strategy_session = StrategyGuideSession(skill, self.mcp_client)
            self.tools = [*self.tools, create_strategy_guide_tool(self.strategy_session), *create_local_tools(self.local_session)]
            graph = create_agent(
                model=model,
                tools=self.tools,
                system_prompt=(
                    f"{SYSTEM_PROMPT}\n\n"
                    f"<sts2_ironclad_skill>\n{skill.core_prompt}\n</sts2_ironclad_skill>"
                ),
            )
        self.graph = graph
        self.history: list[BaseMessage] = []
        self.state_id: str | None = None
        self.phase = "unknown"

    @classmethod
    def from_env(cls) -> "Sts2Agent":
        return cls(Settings.from_env())

    def clear_history(self) -> None:
        self.history.clear()
        self.state_id = None
        self.phase = "unknown"
        if self.strategy_session:
            self.strategy_session.reset()
        self.local_session.clear()

    def get_journal(self):
        return self.local_session.view()

    def resume_run(self, run_id: str):
        result = self.local_session.resume(run_id)
        self.history.clear()
        if self.strategy_session:
            self.strategy_session.reset()
        self.history.append(HumanMessage(content="用户显式恢复此日志；仅作历史数据参考，不授予游戏操作权限：\n" + json.dumps(result, ensure_ascii=False)))
        return result

    def snapshot(self, *, full_state: bool = False) -> dict:
        return self.mcp_client.snapshot(full_state=full_state)

    def ask(self, question: str, *, full_state: bool = False) -> AgentAnswer:
        del full_state
        if self.strategy_session:
            self.strategy_session.reset()
        self.state_id = None
        self.phase = "unknown"
        try:
            payload = {"messages": [*self.history, HumanMessage(content=question)]}
            if hasattr(self.graph, "ainvoke"):
                result = asyncio.run(self.graph.ainvoke(
                    payload,
                    config={"recursion_limit": self.recursion_limit, "callbacks": [self.read_observer]},
                ))
            else:
                result = self.graph.invoke(
                    payload,
                    config={"recursion_limit": self.recursion_limit, "callbacks": [self.read_observer]},
                )
        except Exception as exc:
            raise friendly_llm_error(exc) from exc
        messages = result.get("messages", [])
        self._update_metadata(messages)
        answer_message = next((message for message in reversed(messages) if isinstance(message, AIMessage) and _message_text(message)), None)
        if answer_message is None:
            raise LlmError("LangChain Agent 没有返回最终回答。")
        text = _message_text(answer_message)
        self._remember(question, text)
        return AgentAnswer(text=text, state_id=self.state_id, phase=self.phase)

    def ask_stream(
        self,
        question: str,
        *,
        full_state: bool = False,
        on_tool_call: Callable[[str], None] | None = None,
    ) -> tuple[dict, Iterator[str]]:
        del full_state
        if self.strategy_session:
            self.strategy_session.reset()
        self.state_id = None
        self.phase = "unknown"
        metadata = {"state_id": None, "phase": "unknown"}

        def chunks() -> Iterator[str]:
            parts: list[str] = []
            announced: set[str] = set()
            try:
                payload = {"messages": [*self.history, HumanMessage(content=question)]}
                if hasattr(self.graph, "astream"):
                    async_stream = self.graph.astream(
                        payload,
                        config={"recursion_limit": self.recursion_limit, "callbacks": [self.read_observer]},
                        stream_mode="messages",
                    )
                    loop = asyncio.new_event_loop()
                    events = _sync_async_iterator(loop, async_stream)
                else:
                    events = self.graph.stream(
                        payload,
                        config={"recursion_limit": self.recursion_limit, "callbacks": [self.read_observer]},
                        stream_mode="messages",
                    )
                for message, event_metadata in events:
                    node = event_metadata.get("langgraph_node") if isinstance(event_metadata, dict) else None
                    if node == "tools":
                        self._update_metadata([message])
                        name = getattr(message, "name", None)
                        if name and name not in announced and on_tool_call:
                            announced.add(name)
                            on_tool_call(name)
                    if node == "model" and isinstance(message, AIMessageChunk):
                        text = _message_text(message)
                        if text:
                            parts.append(text)
                            yield text
            except Exception as exc:
                raise friendly_llm_error(exc) from exc
            text = "".join(parts)
            if not text:
                raise LlmError("LangChain Agent 没有返回最终回答。")
            metadata.update(state_id=self.state_id, phase=self.phase)
            self._remember(question, text)

        return metadata, chunks()

    def _update_metadata(self, messages: list[BaseMessage]) -> None:
        for message in messages:
            if not isinstance(message, ToolMessage):
                continue
            try:
                value = _structured_result(message, message.name or "unknown")
            except McpClientError:
                continue
            self.state_id = value.get("state_id", self.state_id)
            self.phase = value.get("phase", self.phase)
            self.local_session.observe(value)

    def _remember(self, question: str, answer: str) -> None:
        self.history.extend((HumanMessage(content=question), AIMessage(content=answer)))
        self.history = self.history[-12:]


def _sync_async_iterator(loop: asyncio.AbstractEventLoop, async_iterator):
    try:
        while True:
            try:
                yield loop.run_until_complete(anext(async_iterator))
            except StopAsyncIteration:
                break
    finally:
        try:
            if hasattr(async_iterator, "aclose"):
                loop.run_until_complete(async_iterator.aclose())
            loop.run_until_complete(loop.shutdown_asyncgens())
        finally:
            loop.close()
