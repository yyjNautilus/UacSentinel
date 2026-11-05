# -*- coding: utf-8 -*-
"""selftest_agent_status — 上报模块验收自测（零依赖，仅标准库）。

覆盖验收点（对照 docs/agent-status-prompt.md）：
  1. begin → 文件出现 running 任务，中文标题不乱码，无 BOM，无残留 .tmp（原子写入）
  2. update → 轮数 +1，updatedAt 刷新
  3. done → 变 done
  4. interrupt / 崩溃重启 reconcile → running 变 interrupted
  5. idle → projects 置空
  6. 可选 HTTP：GET /agent-status 返回同样 JSON，端口写入 <id>.port
  7. 静默失败：写失败不抛异常，不影响主流程

运行：python selftest_agent_status.py
"""
from __future__ import annotations

import json
import os
import tempfile
import urllib.request

from agent_status import AgentStatus

BASE_TMP = tempfile.mkdtemp(prefix="agent_status_selftest_")


def _load(base_dir: str) -> dict:
    with open(os.path.join(base_dir, "doubao.json"), encoding="utf-8") as fh:
        return json.load(fh)


def _raw(base_dir: str) -> bytes:
    with open(os.path.join(base_dir, "doubao.json"), "rb") as fh:
        return fh.read()


def test_begin_update_done():
    base = os.path.join(BASE_TMP, "a")
    a = AgentStatus(agent_id="doubao", base_dir=base)

    a.begin("task-20261105-001", "整理《标识符命名法则》笔记",
            cwd=r"C:\Users\me\Documents\项目目录", note="正在整理第 2 节")

    # 文件存在、字段名正确、状态 running、中文不乱码
    data = _load(base)
    assert data["agent"] == "豆包"
    assert "updatedAt" in data
    p = data["projects"][0]
    assert p["id"] == "task-20261105-001"
    assert p["title"] == "整理《标识符命名法则》笔记"
    assert p["status"] == "running"
    assert p["cwd"] == r"C:\Users\me\Documents\项目目录"
    assert p["turns"] == 1
    # 无 BOM、无残留 .tmp（原子写入）
    assert not _raw(base).startswith(b"\xef\xbb\xbf"), "不应有 UTF-8 BOM"
    assert not os.path.exists(os.path.join(base, "doubao.json.tmp")), "不应残留 .tmp"

    # update → 轮数 +1
    a.update("task-20261105-001", note="正在整理第 3 节")
    data = _load(base)
    assert data["projects"][0]["turns"] == 2
    assert data["projects"][0]["status"] == "running"

    # done → 变 done
    a.done("task-20261105-001", note="完成")
    data = _load(base)
    assert data["projects"][0]["status"] == "done"
    print("PASS  begin/update/done + 中文 + 无BOM + 原子写入")


def test_interrupt_and_reconcile():
    base = os.path.join(BASE_TMP, "b")
    a = AgentStatus(agent_id="doubao", base_dir=base)
    a.begin("task-crash", "会崩溃的任务", cwd=os.getcwd())

    # 模拟崩溃：把 updatedAt 改成很旧的时间，仍为 running
    path = os.path.join(base, "doubao.json")
    data = _load(base)
    data["projects"][0]["updatedAt"] = "2020-01-01T00:00:00+08:00"
    with open(path, "w", encoding="utf-8") as fh:
        json.dump(data, fh, ensure_ascii=False)

    # 新进程（新 AgentStatus 实例）启动时 reconcile → 残留 running 变 interrupted
    b = AgentStatus(agent_id="doubao", base_dir=base)
    data = _load(base)
    assert data["projects"][0]["status"] == "interrupted"
    print("PASS  崩溃重启 reconcile：running -> interrupted")

    # 显式 interrupt
    c = AgentStatus(agent_id="doubao", base_dir=base)
    c.begin("task-x", "中断的任务", cwd=os.getcwd())
    c.interrupt("task-x", note="报错")
    data = _load(base)
    assert data["projects"][0]["status"] == "interrupted"
    print("PASS  interrupt()")


def test_idle():
    base = os.path.join(BASE_TMP, "c")
    a = AgentStatus(agent_id="doubao", base_dir=base)
    a.begin("t", "任务", cwd=os.getcwd())
    a.idle()
    data = _load(base)
    assert data["projects"] == []
    print("PASS  idle")


def test_http():
    base = os.path.join(BASE_TMP, "d")
    a = AgentStatus(agent_id="doubao", base_dir=base)
    a.begin("task-http", "HTTP 上报", cwd=os.getcwd())
    port = a.serve(port=0)
    assert port > 0

    # .port 文件写入了端口
    with open(os.path.join(base, "doubao.port"), encoding="utf-8") as fh:
        assert int(fh.read().strip()) == port

    with urllib.request.urlopen(f"http://127.0.0.1:{port}/agent-status", timeout=5) as r:
        got = json.loads(r.read().decode("utf-8"))
    assert got["agent"] == "豆包"
    assert got["projects"][0]["status"] == "running"
    assert got["projects"][0]["title"] == "HTTP 上报"
    print(f"PASS  HTTP /agent-status (127.0.0.1:{port}) + .port 文件")


def test_silent_failure():
    # 非法 base_dir（路径是一个已存在的文件）→ 写失败但不抛异常
    blocker = os.path.join(BASE_TMP, "blocker_file")
    with open(blocker, "w", encoding="utf-8") as fh:
        fh.write("x")
    a = AgentStatus(agent_id="doubao", base_dir=blocker)
    try:
        a.begin("t", "静默失败测试", cwd=os.getcwd())  # 不应抛异常
        print("PASS  静默失败（写失败不影响主流程）")
    except Exception as exc:  # pragma: no cover
        raise AssertionError(f"应静默失败却抛了异常: {exc}")


def main() -> None:
    test_begin_update_done()
    test_interrupt_and_reconcile()
    test_idle()
    test_http()
    test_silent_failure()
    print("\n全部自测通过。")


if __name__ == "__main__":
    main()
