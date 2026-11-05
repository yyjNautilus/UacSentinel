# 给其他 AI 助手的接入提示词（复制即用）

> 用途：让豆包 / 千问 / WorkBuddy / 其他本地智能体把「我正在跑什么、完成了没有」写进本机约定目录，
> 提权哨兵的「项目状态」列表会自动汇总显示。

---

## 提示词正文（从下一行开始复制）

你是一名负责给桌面 AI 助手增加「状态上报」能力的工程师。我的电脑上装了一个本地看板程序（UacSentinel），
它会读取约定目录里的 JSON，汇总显示所有 AI 助手正在运行/已完成的任务。请在我的代码库里增加这个上报能力。

### 必须实现

1. 状态文件路径：`%LOCALAPPDATA%\AgentStatus\<你的助手英文id>.json`
   （例如 `doubao.json`、`qwen.json`、`workbuddy.json`；id 用你的产品名小写）

2. 写入方式必须是原子替换：先写同目录的 `.tmp` 文件，再用 `File.Move` / `os.replace` 覆盖；
   编码 UTF-8、不带 BOM；写入失败要静默，不能影响主流程。

3. JSON 格式（严格遵守，字段名不要改）：

```json
{
  "agent": "豆包",
  "updatedAt": "2026-11-05T20:30:00+08:00",
  "projects": [
    {
      "id": "task-20261105-001",
      "title": "整理《标识符命名法则》笔记",
      "status": "running",
      "cwd": "C:\\Users\\me\\Documents\\项目目录",
      "turns": 3,
      "updatedAt": "2026-11-05T20:30:00+08:00",
      "note": "正在整理第 2 节"
    }
  ]
}
```

4. 字段语义：
   - `status`：`running`（正在执行，含等模型/等工具）、`done`（已完成）、`interrupted`（用户中断或报错）、`idle`（没有任务）
   - `turns`：已经进行的轮数，没有就填 0
   - `title`：给人看的任务名，≤40 字，不要放对话原文
   - `cwd`：任务涉及的工作目录，没有就填 null
   - `note`：可选，一句话进度

5. 更新时机：任务开始、每轮结束、完成/中断时各写一次；`running` 期间至少每 30 秒刷新一次 `updatedAt`。

6. 退出清理：程序正常退出或崩溃重启时，把残留的 `running` 改成 `interrupted`，避免看板一直显示在跑。

### 可选（如果你的程序不能写文件）

提供本地 HTTP 接口：`GET http://127.0.0.1:<端口>/agent-status`，返回上面同样的 JSON；
再把端口写到 `%LOCALAPPDATA%\AgentStatus\<id>.port`。看板会读取它。

### 硬性约束

- 只上报：标题、状态、工作目录、轮数。**禁止**上报对话内容、文件内容、密钥、账号、IP。
- 只写本机文件，不发任何网络请求。
- 不要改动现有功能，不要引入重型依赖；用你现有语言的标准库即可。
- 所有异常吞掉（记日志可以），绝不能因为上报失败影响用户任务。

### 验收标准

1. 开始一个任务 → 5 秒内看板出现该任务，状态「运行中」。
2. 任务完成 → 5 秒内变「已完成」。
3. 强制结束助手 → 状态变「已中断」。
4. 中文标题不在看板里乱码。
5. 看板关闭时，你的程序仍然正常工作。

### 交付

- 改动文件清单
- 关键代码（含原子写入）
- 自测方法：怎么触发 running、done、interrupted 三种状态

---

## 参考实现（Python，直接用仓库里的模块）

仓库已附带一份完整、自测通过的零依赖 Python 上报模块，字段与看板契约严格对齐：

- 模块：`agent-status/agent_status.py`
- 自测：`agent-status/selftest_agent_status.py`（`python agent-status/selftest_agent_status.py`）
- 可选守护进程（强制结束助手 → 自动变「已中断」）：`agent-status/agent_status_watchdog.py`

用法：

```python
from agent_status import AgentStatus
st = AgentStatus()                                  # id 默认 doubao
st.begin("task-20261105-001", "整理《标识符命名法则》笔记", note="开始")
st.update("task-20261105-001", note="第 2 节完成")   # 每轮结束调一次
st.done("task-20261105-001", note="完成")
st.interrupt("task-20261105-001", note="报错")
```

也支持命令行一步一进程：`begin / update / done / interrupt / idle / reconcile / serve / show`，
见 `agent_status.py` 头部注释。若只想看最小可抄的核心，等价于：

```python
import json, os, tempfile, datetime

STATUS_DIR = os.path.join(os.environ["LOCALAPPDATA"], "AgentStatus")
AGENT = "豆包"
STATE = {"projects": []}

def report():
    os.makedirs(STATUS_DIR, exist_ok=True)
    payload = {
        "agent": AGENT,
        "updatedAt": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
        "projects": STATE["projects"],
    }
    path = os.path.join(STATUS_DIR, "doubao.json")
    fd, tmp = tempfile.mkstemp(dir=STATUS_DIR, suffix=".tmp")
    with os.fdopen(fd, "w", encoding="utf-8", newline="\n") as f:
        json.dump(payload, f, ensure_ascii=False)
    os.replace(tmp, path)

def start(task_id, title, cwd):
    STATE["projects"] = [p for p in STATE["projects"] if p["id"] != task_id]
    STATE["projects"].append({
        "id": task_id, "title": title, "status": "running",
        "cwd": cwd, "turns": 0, "note": "",
        "updatedAt": datetime.datetime.now().astimezone().isoformat(timespec="seconds"),
    })
    report()

def finish(task_id, ok=True):
    for p in STATE["projects"]:
        if p["id"] == task_id:
            p["status"] = "done" if ok else "interrupted"
            p["updatedAt"] = datetime.datetime.now().astimezone().isoformat(timespec="seconds")
    report()
```

## 参考实现（Node / Electron）

```js
const fs = require("fs");
const path = require("path");
const dir = path.join(process.env.LOCALAPPDATA, "AgentStatus");

function report(projects) {
  fs.mkdirSync(dir, { recursive: true });
  const data = { agent: "WorkBuddy", updatedAt: new Date().toISOString(), projects };
  const tmp = path.join(dir, `workbuddy.${process.pid}.tmp`);
  fs.writeFileSync(tmp, JSON.stringify(data), "utf8");
  fs.renameSync(tmp, path.join(dir, "workbuddy.json"));
}
```
