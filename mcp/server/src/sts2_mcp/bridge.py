from __future__ import annotations

import json
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


class BridgeError(RuntimeError):
    pass


class BridgeUnavailableError(BridgeError):
    pass


class BridgeNotReadyError(BridgeError):
    pass


class BridgeActionError(BridgeError):
    pass


class BridgeClient:
    def __init__(self, base_url: str, timeout_seconds: float = 3.0) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout_seconds = timeout_seconds

    def get_snapshot(self) -> dict:
        request = Request(f"{self.base_url}/snapshot", method="GET")
        try:
            with urlopen(request, timeout=self.timeout_seconds) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            if exc.code == 503:
                raise BridgeNotReadyError("游戏界面正在切换，快照暂时不可用，请稍后重试。") from exc
            raise BridgeError(f"读取游戏快照失败（HTTP {exc.code}）。") from exc
        except (URLError, TimeoutError, OSError) as exc:
            raise BridgeUnavailableError(
                f"无法连接本地游戏桥接器 {self.base_url}。请确认游戏已启动并启用 Mod。"
            ) from exc
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise BridgeError("游戏桥接器返回了无效 JSON。") from exc

    def execute_action(self, state_id: str, action_id: str) -> dict:
        payload = json.dumps(
            {"state_id": state_id, "action_id": action_id},
            ensure_ascii=False,
        ).encode("utf-8")
        request = Request(
            f"{self.base_url}/action",
            data=payload,
            headers={"Content-Type": "application/json; charset=utf-8"},
            method="POST",
        )
        try:
            with urlopen(request, timeout=self.timeout_seconds) as response:
                return json.loads(response.read().decode("utf-8"))
        except HTTPError as exc:
            try:
                error = json.loads(exc.read().decode("utf-8"))
                code = error.get("error", "action_rejected")
                message = error.get("message", "游戏拒绝了这个动作")
            except (UnicodeDecodeError, json.JSONDecodeError):
                code = "action_rejected"
                message = f"游戏拒绝了这个动作（HTTP {exc.code}）"
            raise BridgeActionError(f"{message} [{code}]") from exc
        except (URLError, TimeoutError, OSError) as exc:
            raise BridgeUnavailableError(
                f"无法连接本地游戏桥接器 {self.base_url}。请确认游戏已启动并启用 Mod。"
            ) from exc
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            raise BridgeError("游戏桥接器返回了无效 JSON。") from exc
