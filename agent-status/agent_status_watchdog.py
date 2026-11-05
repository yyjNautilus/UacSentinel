# -*- coding: utf-8 -*-
"""agent_status_watchdog — 后台守护进程（可选）。

用途：当助手被「强制结束」（进程被杀，来不及写清理）时，看板上的任务会一直是
running。本守护进程轮询 %LOCALAPPDATA%\\AgentStatus\\<id>.json，把 updatedAt
过期（默认 90 秒）且仍为 running 的任务标记为 interrupted，让看板尽快变「已中断」。

这不是必须的：正常退出由 agent_status 内部的 atexit 清理，崩溃重启由
reconcile_stale_running() 在下次启动时清理。守护进程是为了在「看板一直开着、
助手却被强杀」的场景下也能自动收敛（对应验收标准第 3 条）。

运行：
    python agent_status_watchdog.py --id doubao --stale 90 --interval 5

参数：
    --id        助手英文 id，默认 doubao
    --stale     超过该秒数未刷新视为失联，默认 90
    --interval  轮询间隔秒数，默认 5
    --base-dir  覆盖状态目录
"""
from __future__ import annotations

import argparse
import time

from agent_status import AgentStatus, STALE_SECONDS


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description="UacSentinel 状态守护进程")
    p.add_argument("--id", default="doubao")
    p.add_argument("--stale", type=int, default=STALE_SECONDS)
    p.add_argument("--interval", type=int, default=5)
    p.add_argument("--base-dir", default=None)
    args = p.parse_args(argv)

    st = AgentStatus(agent_id=args.id, base_dir=args.base_dir)
    print(f"watchdog: 监控 {st.file_path}，stale={args.stale}s，interval={args.interval}s")
    try:
        while True:
            changed = st.reconcile_stale_running(stale_seconds=args.stale)
            if changed:
                print(time.strftime("%H:%M:%S"), "标记为 interrupted:", changed, flush=True)
            time.sleep(args.interval)
    except KeyboardInterrupt:
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
