from __future__ import annotations

from collections.abc import Callable, Iterator
from typing import Any

from langchain.agents import create_agent
from langchain_core.messages import AIMessage, AIMessageChunk, BaseMessage, HumanMessage
from langchain_openai import ChatOpenAI

from .agent_types import AgentAnswer, LlmError, friendly_llm_error
from .bridge import BridgeClient
from .compact import compact_snapshot
from .config import Settings
from .prompts import SYSTEM_PROMPT
from .tools import ToolState, build_read_tools


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


class Sts2Agent:
    """LangChain tool-calling agent backed by the read-only STS2 bridge."""

    recursion_limit = 10

    def __init__(self, settings: Settings, *, bridge: BridgeClient | None = None, model: Any = None, graph: Any = None) -> None:
        self.settings = settings
        self.bridge = bridge or BridgeClient(settings.bridge_url)
        self.tool_state = ToolState()
        self.tools = build_read_tools(self.bridge, self.tool_state)
        if graph is None:
            model = model or ChatOpenAI(
                model=settings.model,
                api_key=settings.api_key,
                base_url=settings.base_url,
                timeout=settings.timeout_seconds,
                max_retries=2,
                streaming=True,
            )
            graph = create_agent(model=model, tools=self.tools, system_prompt=SYSTEM_PROMPT)
        self.graph = graph
        self.history: list[BaseMessage] = []

    @classmethod
    def from_env(cls) -> "Sts2Agent":
        return cls(Settings.from_env())

    def clear_history(self) -> None:
        self.history.clear()

    def snapshot(self, *, full_state: bool = False) -> dict:
        return compact_snapshot(self.bridge.get_snapshot(), full_state=full_state)

    def ask(self, question: str, *, full_state: bool = False) -> AgentAnswer:
        del full_state
        self.tool_state.state_id = None
        self.tool_state.phase = "unknown"
        try:
            result = self.graph.invoke(
                {"messages": [*self.history, HumanMessage(content=question)]},
                config={"recursion_limit": self.recursion_limit},
            )
        except Exception as exc:
            raise friendly_llm_error(exc) from exc
        messages = result.get("messages", [])
        answer_message = next((message for message in reversed(messages) if isinstance(message, AIMessage) and _message_text(message)), None)
        if answer_message is None:
            raise LlmError("LangChain Agent 没有返回最终回答。")
        text = _message_text(answer_message)
        self._remember(question, text)
        return AgentAnswer(text=text, state_id=self.tool_state.state_id, phase=self.tool_state.phase)

    def ask_stream(
        self,
        question: str,
        *,
        full_state: bool = False,
        on_tool_call: Callable[[str], None] | None = None,
    ) -> tuple[dict, Iterator[str]]:
        del full_state
        self.tool_state.state_id = None
        self.tool_state.phase = "unknown"
        metadata = {"state_id": None, "phase": "unknown"}

        def chunks() -> Iterator[str]:
            parts: list[str] = []
            announced: set[str] = set()
            try:
                stream = self.graph.stream(
                    {"messages": [*self.history, HumanMessage(content=question)]},
                    config={"recursion_limit": self.recursion_limit},
                    stream_mode="messages",
                )
                for message, event_metadata in stream:
                    node = event_metadata.get("langgraph_node") if isinstance(event_metadata, dict) else None
                    if node == "tools":
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
            metadata.update(state_id=self.tool_state.state_id, phase=self.tool_state.phase)
            self._remember(question, text)

        return metadata, chunks()

    def _remember(self, question: str, answer: str) -> None:
        self.history.extend((HumanMessage(content=question), AIMessage(content=answer)))
        self.history = self.history[-12:]
