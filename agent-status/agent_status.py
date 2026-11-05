# -*- coding: utf-8 -*-
"""agent_status — UacSentinel「项目状态」的助手侧上报模块（零依赖，静默失败）。

这是给 AI 助手（豆包 / 千问 / WorkBuddy 等）用的上报侧实现；UacSentinel 看板的读取侧
（src/Projects.cs 的 ScanExternal）已经就绪，只等助手按本模块写 %LOCALAPPDATA%\\AgentStatus\\<id>.json
（或提供 GET http://127.0.0.1:<port>/agent-status），就会出现在「项目状态」列表里。

约定（与读取端严格对齐，字段名不可改）：
  - 文件：%LOCALAPPDATA%\\AgentStatus\\<id>.json（id = 产品名小写，本模块默认 doubao）
  - 原子写入：先写同目录 .tmp，再 os.replace 覆盖；UTF-8 无 BOM；失败静默
  - status 仅限：running / done / interrupted / idle
  - 只上报标题、状态、工作目录、轮数；不涉及对话内容、文件内容、密钥、账号、IP
  - 只写本机文件；HTTP 服务仅监听 127.0.0.1 回环，不发任何外网请求

作为模块：
    from agent_status import AgentStatus
    st = AgentStatus()                          # id 默认 doubao，构造时会 reconcile 上次残留
    st.begin("task-20261105-001", "整理《标识符命名法则》笔记", note="开始")
    st.update("task-20261105-001", note="第 2 节完成")   # 每轮结束调一次，轮数+1、刷新 updatedAt
    st.done("task-20261105-001", note="完成")
    st.interrupt("task-20261105-001", note="报错")
    st.idle()                                    # 无任务

命令行（一步一进程，不自带退出清理）：
    python agent_status.py begin --pid task-1 --title "任务标题" [--note "..." ]
    python agent_status.py update --pid task-1 [--note "..."]
    python agent_status.py done   --pid task-1 [--note "..."]
    python agent_status.py interrupt --pid task-1 [--note "..."]
    python agent_status.py idle
    python agent_status.py reconcile            # 把残留 running 标为 interrupted
    python agent_status.py serve [--port 0]     # 本机 HTTP /agent-status，端口写入 <id>.port
    python agent_status.py show                 # 打印当前 JSON
"""

from __future__ import annotations

import argparse
import atexit
import json
import os
import sys
import threading
import time
from datetime import datetime, timedelta, timezone

AGENT_ID = "doubao"
AGENT_NAME = "豆包"

STATUS_RUNNING = "running"
STATUS_DONE = "done"
STATUS_INTERRUPTED = "interrupted"
STATUS_IDLE = "idle"
VALID_STATUS = (STATUS_RUNNING, STATUS_DONE, STATUS_INTERRUPTED, STATUS_IDLE)

# 固定 +08:00（与契约示例一致）
_LOCAL_TZ = timezone(timedelta(hours=8), name="UTC+8")
# running 超过该秒数未刷新视为失联（供 reconcile / watchdog 判断）
STALE_SECONDS = 90


def _now_iso() -> str:
    return datetime.now(_LOCAL_TZ).isoformat(timespec="seconds")


def _default_dir() -> str:
    base = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(base, "AgentStatus")


class AgentStatus:
    """单助手状态上报器。字段名与 UacSentinel 约定一致，不可改名。"""

    def __init__(self, agent_id: str = AGENT_ID, agent_name: str = AGENT_NAME,
                 base_dir: str | None = None, enable_atexit: bool = True):
        self.agent_id = agent_id
        self.agent_name = agent_name
        self.base_dir = base_dir or _default_dir()
        self.file_path = os.path.join(self.base_dir, f"{agent_id}.json")
        self.port_file = os.path.join(self.base_dir, f"{agent_id}.port")
        self._projects: dict[str, dict] = {}
        self._turns = 0
        self._lock = threading.Lock()
        self._http_server = None
        # 崩溃重启后，先把上次残留的 running 改成 interrupted
        self.reconcile_stale_running()
        # 长驻助手进程退出时把 running 标为 interrupted。
        # 命令行一步一进程（CLI）关闭该行为，避免刚 begin 就被自己标中断。
        if enable_atexit:
            atexit.register(self._atexit_cleanup)

    # ---------------------------------------------------------------- 基础 I/O

    def _write(self, data: dict) -> None:
        """原子写入：先写 .tmp，再 os.replace 覆盖。UTF-8 无 BOM。失败静默。"""
        tmp = self.file_path + ".tmp"
        try:
            os.makedirs(self.base_dir, exist_ok=True)
            with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
                json.dump(data, fh, ensure_ascii=False, indent=2)
                fh.write("\n")
            os.replace(tmp, self.file_path)
        except Exception:
            try:
                if os.path.exists(tmp):
                    os.remove(tmp)
            except Exception:
                pass

    def _read(self) -> dict:
        try:
            with open(self.file_path, "r", encoding="utf-8") as fh:
                return json.load(fh)
        except Exception:
            return {"agent": self.agent_name, "updatedAt": _now_iso(), "projects": []}

    def _payload_snapshot(self) -> dict:
        with self._lock:
            return {
                "agent": self.agent_name,
                "updatedAt": _now_iso(),
                "projects": [dict(p) for p in self._projects.values()],
            }

    def _persist(self) -> None:
        self._write(self._payload_snapshot())

    @staticmethod
    def _parse_iso(s) -> float | None:
        if not s:
            return None
        try:
            return datetime.fromisoformat(s).timestamp()
        except Exception:
            return None

    def _write_port(self, port: int) -> None:
        tmp = self.port_file + ".tmp"
        try:
            os.makedirs(self.base_dir, exist_ok=True)
            with open(tmp, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(str(port))
            os.replace(tmp, self.port_file)
        except Exception:
            try:
                if os.path.exists(tmp):
                    os.remove(tmp)
            except Exception:
                pass

    # ------------------------------------------------------------ 状态迁移

    def begin(self, project_id: str, title: str, cwd: str | None = None,
              note: str | None = None) -> None:
        """任务开始：写入 running。"""
        self._turns += 1
        with self._lock:
            self._projects[project_id] = {
                "id": project_id,
                "title": title,
                "status": STATUS_RUNNING,
                "cwd": cwd or os.getcwd(),
                "turns": self._turns,
                "updatedAt": _now_iso(),
                "note": note or "",
            }
        self._persist()

    def _ensure_project(self, project_id: str) -> dict | None:
        """取回项目记录；本进程内没有时回退读磁盘（支持一步一进程的 CLI）。"""
        p = self._projects.get(project_id)
        if p is not None:
            return p
        try:
            for rec in self._read().get("projects", []):
                if rec.get("id") == project_id:
                    self._projects[project_id] = rec
                    return rec
        except Exception:
            pass
        return None

    def update(self, project_id: str, note: str | None = None) -> None:
        """每轮结束：轮数 +1，刷新 updatedAt。"""
        with self._lock:
            p = self._ensure_project(project_id)
            if p is None:
                return
            self._turns += 1
            p["turns"] = self._turns
            p["updatedAt"] = _now_iso()
            if note is not None:
                p["note"] = note
        self._persist()

    def done(self, project_id: str, note: str | None = None) -> None:
        self._set_status(project_id, STATUS_DONE, note)

    def interrupt(self, project_id: str, note: str | None = None) -> None:
        self._set_status(project_id, STATUS_INTERRUPTED, note)

    def _set_status(self, project_id: str, status: str, note: str | None = None) -> None:
        with self._lock:
            p = self._ensure_project(project_id)
            if p is None:
                return
            p["status"] = status
            p["updatedAt"] = _now_iso()
            if note is not None:
                p["note"] = note
        self._persist()

    def idle(self) -> None:
        """无任务：projects 置空。"""
        with self._lock:
            self._projects.clear()
        self._persist()

    def interrupt_all(self) -> int:
        """把当前所有 running 项目改为 interrupted。返回改动数量。"""
        n = 0
        with self._lock:
            for p in self._projects.values():
                if p.get("status") == STATUS_RUNNING:
                    p["status"] = STATUS_INTERRUPTED
                    p["updatedAt"] = _now_iso()
                    n += 1
        if n:
            self._persist()
        return n

    def reconcile_stale_running(self, stale_seconds: int = STALE_SECONDS) -> list:
        """把文件里 updatedAt 过期且仍为 running 的任务标记为 interrupted。

        用于崩溃/重启后清理残留。返回被改写的项目 id 列表。
        """
        changed = []
        try:
            data = self._read()
            now_ts = time.time()
            for p in data.get("projects", []):
                if p.get("status") == STATUS_RUNNING:
                    ts = self._parse_iso(p.get("updatedAt"))
                    if ts is not None and now_ts - ts > stale_seconds:
                        p["status"] = STATUS_INTERRUPTED
                        p["note"] = (p.get("note") or "") + "（进程中断，任务已标记为已中断）"
                        changed.append(p.get("id"))
            if changed:
                self._write(data)
        except Exception:
            pass
        return changed

    # ------------------------------------------------------------ 本地 HTTP 服务

    def serve(self, host: str = "127.0.0.1", port: int = 0) -> int:
        """启动本机回环 HTTP 服务，GET /agent-status 返回当前 JSON。

        仅监听 127.0.0.1，不发任何外网请求。返回实际监听端口；失败返回 -1。
        """
        from http.server import BaseHTTPRequestHandler, HTTPServer

        owner = self

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                if self.path.rstrip("/") in ("/agent-status", "/agent_status"):
                    body = json.dumps(owner._payload_snapshot(),
                                      ensure_ascii=False).encode("utf-8")
                    self.send_response(200)
                    self.send_header("Content-Type", "application/json; charset=utf-8")
                    self.send_header("Content-Length", str(len(body)))
                    self.end_headers()
                    try:
                        self.wfile.write(body)
                    except Exception:
                        pass
                else:
                    self.send_response(404)
                    self.end_headers()

            def log_message(self, *args):  # 静默访问日志
                pass

        try:
            server = HTTPServer((host, port), Handler)
        except Exception:
            return -1
        self._http_server = server
        real_port = server.server_address[1]
        self._write_port(real_port)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        return real_port

    # ------------------------------------------------------------ 退出清理

    def _atexit_cleanup(self) -> None:
        try:
            self.interrupt_all()
            if self._http_server is not None:
                try:
                    self._http_server.shutdown()
                except Exception:
                    pass
        except Exception:
            pass


# ================================================================ 命令行入口

def _load(args) -> AgentStatus:
    # CLI 是「一步一进程」的一次性调用，不注册 atexit 退出清理，
    # 否则每个 CLI 进程退出都会把刚写入的 running 标成 interrupted。
    return AgentStatus(agent_id=args.id, base_dir=args.base_dir, enable_atexit=False)


def main(argv=None) -> int:
    p = argparse.ArgumentParser(
        prog="agent_status", description="UacSentinel 状态上报 CLI（零依赖，静默失败）")
    p.add_argument("action", choices=["begin", "update", "done", "interrupt",
                                      "idle", "reconcile", "serve", "show"])
    p.add_argument("--id", default=AGENT_ID, help="助手英文 id，默认 doubao")
    p.add_argument("--pid", default=None, help="project id，如 task-20261105-001")
    p.add_argument("--title", default=None, help="任务标题")
    p.add_argument("--cwd", default=None, help="工作目录，默认取当前目录")
    p.add_argument("--note", default=None, help="备注")
    p.add_argument("--port", type=int, default=0, help="HTTP 端口，0=自动")
    p.add_argument("--base-dir", default=None, help="覆盖状态目录（默认 %LOCALAPPDATA%\\AgentStatus）")
    args = p.parse_args(argv)

    if args.action == "show":
        st = _load(args)
        print(json.dumps(st._read(), ensure_ascii=False, indent=2))
        return 0

    if args.action == "serve":
        st = _load(args)
        port = st.serve(port=args.port)
        if port <= 0:
            print("HTTP 服务启动失败", file=sys.stderr)
            return 1
        print(f"agent-status: http://127.0.0.1:{port}/agent-status  (端口已写入 {st.port_file})")
        while True:
            time.sleep(3600)
        return 0

    if args.action == "reconcile":
        st = _load(args)
        changed = st.reconcile_stale_running()
        print("已标记为 interrupted:", changed or "（无）")
        return 0

    if not args.pid:
        p.error("--pid 必填（begin/update/done/interrupt）")
    if args.action == "begin" and not args.title:
        p.error("begin 需要 --title")

    st = _load(args)
    if args.action == "begin":
        st.begin(args.pid, args.title, cwd=args.cwd, note=args.note)
    elif args.action == "update":
        st.update(args.pid, note=args.note)
    elif args.action == "done":
        st.done(args.pid, note=args.note)
    elif args.action == "interrupt":
        st.interrupt(args.pid, note=args.note)
    elif args.action == "idle":
        st.idle()
    print(f"[{args.action}] {args.pid} -> {st.file_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
