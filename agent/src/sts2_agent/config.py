from __future__ import annotations

import os
from dataclasses import dataclass
from pathlib import Path

from dotenv import load_dotenv


class ConfigurationError(ValueError):
    """Required local configuration is missing or invalid."""


@dataclass(frozen=True, slots=True)
class Settings:
    api_key: str
    base_url: str = "https://api.deepseek.com"
    model: str = "deepseek-v4-flash"
    bridge_url: str = "http://127.0.0.1:38281"
    mcp_directory: Path = Path(__file__).resolve().parents[3] / "mcp" / "server"
    timeout_seconds: float = 60.0

    @classmethod
    def from_env(cls, env_file: str | Path | None = None) -> "Settings":
        load_dotenv(dotenv_path=env_file) if env_file else load_dotenv()
        api_key = os.getenv("LLM_API_KEY", "").strip()
        if not api_key:
            raise ConfigurationError(
                "缺少 LLM_API_KEY。请复制 agent/.env.example 为 agent/.env，并填写 API Key。"
            )
        try:
            timeout = float(os.getenv("LLM_TIMEOUT_SECONDS", "60"))
        except ValueError as exc:
            raise ConfigurationError("LLM_TIMEOUT_SECONDS 必须是数字。") from exc
        if timeout <= 0:
            raise ConfigurationError("LLM_TIMEOUT_SECONDS 必须大于 0。")
        default_mcp_directory = Path(__file__).resolve().parents[3] / "mcp" / "server"
        configured_mcp_directory = os.getenv("STS2_MCP_DIRECTORY", "").strip()
        return cls(
            api_key=api_key,
            base_url=os.getenv("LLM_BASE_URL", "https://api.deepseek.com").strip().rstrip("/"),
            model=os.getenv("LLM_MODEL", "deepseek-v4-flash").strip(),
            bridge_url=os.getenv("STS2_BRIDGE_URL", "http://127.0.0.1:38281").strip().rstrip("/"),
            mcp_directory=Path(configured_mcp_directory or default_mcp_directory)
            .expanduser()
            .resolve(),
            timeout_seconds=timeout,
        )
