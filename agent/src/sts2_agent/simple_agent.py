from __future__ import annotations

import json
from typing import Iterator

from openai import OpenAI

from .agent_types import AgentAnswer, LlmError, friendly_llm_error
from .bridge import BridgeClient
from .compact import compact_snapshot
from .config import Settings
from .prompts import SYSTEM_PROMPT


class SimpleSts2Agent:
    """Stable one-snapshot/one-model-call fallback client."""

    def __init__(self, settings: Settings, *, bridge: BridgeClient | None = None, client: OpenAI | None = None) -> None:
        self.settings = settings
        self.bridge = bridge or BridgeClient(settings.bridge_url)
        self.client = client or OpenAI(api_key=settings.api_key, base_url=settings.base_url, timeout=settings.timeout_seconds, max_retries=2)
        self.history: list[dict[str, str]] = []

    @classmethod
    def from_env(cls) -> "SimpleSts2Agent":
        return cls(Settings.from_env())

    def clear_history(self) -> None:
        self.history.clear()

    def snapshot(self, *, full_state: bool = False) -> dict:
        return compact_snapshot(self.bridge.get_snapshot(), full_state=full_state)

    def _messages(self, question: str, state: dict) -> list[dict[str, str]]:
        payload = json.dumps(state, ensure_ascii=False, separators=(",", ":"))
        return [{"role": "system", "content": SYSTEM_PROMPT}, *self.history, {"role": "user", "content": f"当前游戏状态 JSON：\n{payload}\n\n玩家问题：{question}"}]

    def ask(self, question: str, *, full_state: bool = False) -> AgentAnswer:
        state = self.snapshot(full_state=full_state)
        try:
            response = self.client.chat.completions.create(model=self.settings.model, messages=self._messages(question, state), stream=False)
        except Exception as exc:
            raise friendly_llm_error(exc) from exc
        text = response.choices[0].message.content or ""
        self._remember(question, text)
        return AgentAnswer(text=text, state_id=state.get("state_id"), phase=state.get("phase", "unknown"))

    def ask_stream(self, question: str, *, full_state: bool = False, on_tool_call=None) -> tuple[dict, Iterator[str]]:
        state = self.snapshot(full_state=full_state)

        def chunks() -> Iterator[str]:
            parts: list[str] = []
            try:
                stream = self.client.chat.completions.create(model=self.settings.model, messages=self._messages(question, state), stream=True)
                for chunk in stream:
                    content = chunk.choices[0].delta.content or ""
                    if content:
                        parts.append(content)
                        yield content
            except Exception as exc:
                raise friendly_llm_error(exc) from exc
            self._remember(question, "".join(parts))

        return state, chunks()

    def _remember(self, question: str, answer: str) -> None:
        self.history.extend(({"role": "user", "content": question}, {"role": "assistant", "content": answer}))
        self.history = self.history[-12:]
