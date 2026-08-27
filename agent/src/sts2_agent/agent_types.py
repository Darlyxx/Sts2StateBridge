from __future__ import annotations

from dataclasses import dataclass

import openai
from langgraph.errors import GraphRecursionError


class LlmError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class AgentAnswer:
    text: str
    state_id: str | None
    phase: str


def friendly_llm_error(exc: Exception) -> LlmError:
    if isinstance(exc, GraphRecursionError):
        return LlmError("LangChain Agent 已达到本轮最大工具调用次数，已安全停止。请缩小问题范围后重试。")
    if isinstance(exc, openai.AuthenticationError):
        return LlmError("模型服务拒绝了 API Key（401），请检查 LLM_API_KEY。")
    if isinstance(exc, openai.RateLimitError):
        return LlmError("模型服务限流或账户余额不足（429），请稍后重试并检查余额。")
    if isinstance(exc, openai.APITimeoutError):
        return LlmError("模型请求超时，请稍后重试或调高 LLM_TIMEOUT_SECONDS。")
    if isinstance(exc, openai.APIConnectionError):
        return LlmError("无法连接模型服务，请检查 LLM_BASE_URL 和网络。")
    if isinstance(exc, openai.APIStatusError):
        return LlmError(f"模型服务返回错误（HTTP {exc.status_code}）。")
    return LlmError(f"模型请求失败：{type(exc).__name__}")
