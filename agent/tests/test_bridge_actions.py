import io
import json
from urllib.error import HTTPError

import pytest

import sts2_agent.bridge as bridge_module
from sts2_agent.bridge import BridgeActionError, BridgeClient


class FakeResponse:
    def __init__(self, payload):
        self.payload = payload

    def __enter__(self):
        return self

    def __exit__(self, *_):
        return False

    def read(self):
        return json.dumps(self.payload).encode("utf-8")


def test_execute_action_posts_only_state_and_candidate(monkeypatch):
    captured = {}

    def fake_urlopen(request, timeout):
        captured["url"] = request.full_url
        captured["method"] = request.method
        captured["body"] = json.loads(request.data.decode("utf-8"))
        captured["timeout"] = timeout
        return FakeResponse({
            "accepted": True,
            "state_id": "state-1",
            "action_id": "end_turn",
            "action_type": "end_turn",
        })

    monkeypatch.setattr(bridge_module, "urlopen", fake_urlopen)
    result = BridgeClient("http://127.0.0.1:38281").execute_action("state-1", "end_turn")
    assert result["accepted"] is True
    assert captured == {
        "url": "http://127.0.0.1:38281/action",
        "method": "POST",
        "body": {"state_id": "state-1", "action_id": "end_turn"},
        "timeout": 3.0,
    }


def test_execute_action_surfaces_safe_bridge_error(monkeypatch):
    def fake_urlopen(_request, timeout):
        del timeout
        body = io.BytesIO(json.dumps({
            "error": "stale_state",
            "message": "state_id does not match the current game state",
        }).encode("utf-8"))
        raise HTTPError("http://local/action", 409, "Conflict", {}, body)

    monkeypatch.setattr(bridge_module, "urlopen", fake_urlopen)
    with pytest.raises(BridgeActionError) as error:
        BridgeClient("http://127.0.0.1:38281").execute_action("old", "end_turn")
    assert "stale_state" in str(error.value)
