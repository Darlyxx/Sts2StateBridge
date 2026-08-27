from pathlib import Path

import pytest

from sts2_agent.config import ConfigurationError, Settings


def test_settings_load_from_file(tmp_path: Path, monkeypatch):
    monkeypatch.delenv("LLM_API_KEY", raising=False)
    env = tmp_path / ".env"
    env.write_text("LLM_API_KEY=test-key\nLLM_MODEL=test-model\nLLM_TIMEOUT_SECONDS=12\n", encoding="utf-8")
    settings = Settings.from_env(env)
    assert (settings.api_key, settings.model, settings.timeout_seconds) == ("test-key", "test-model", 12)


def test_missing_key_has_safe_error(tmp_path: Path, monkeypatch):
    monkeypatch.delenv("LLM_API_KEY", raising=False)
    env = tmp_path / ".env"
    env.write_text("LLM_MODEL=test-model\n", encoding="utf-8")
    with pytest.raises(ConfigurationError) as error:
        Settings.from_env(env)
    assert "API Key" in str(error.value)
