"""Minimal stdio MCP client: launch the UnityMCP server exactly as Claude Code does, then
list tools or call one tool. Used to verify the Claude Code -> server -> Unity path
without restarting the Claude session.

  python3 .claude/tools/unity-mcp-call.py list [filter]
  python3 .claude/tools/unity-mcp-call.py schema <tool>
  python3 .claude/tools/unity-mcp-call.py call <tool> '<json args>'
"""
import json
import os
import subprocess
import sys
import threading
import queue

CMD = ["/Users/lawrence/.local/bin/uvx", "--from", "mcpforunityserver==10.2.0",
       "mcp-for-unity", "--transport", "stdio"]
ENV = dict(os.environ, UNITY_MCP_DISABLE_TELEMETRY="true")


def main():
    proc = subprocess.Popen(CMD, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.DEVNULL, env=ENV, text=True, bufsize=1)
    lines = queue.Queue()
    threading.Thread(target=lambda: [lines.put(l) for l in proc.stdout], daemon=True).start()

    def send(obj):
        proc.stdin.write(json.dumps(obj) + "\n")
        proc.stdin.flush()

    def request(req_id, method, params, timeout=90):
        send({"jsonrpc": "2.0", "id": req_id, "method": method, "params": params})
        while True:
            msg = json.loads(lines.get(timeout=timeout))
            if msg.get("id") == req_id:
                return msg

    try:
        init = request(1, "initialize", {"protocolVersion": "2025-06-18", "capabilities": {},
                                         "clientInfo": {"name": "smoke-test", "version": "0"}})
        info = init.get("result", {}).get("serverInfo", {})
        print(f"server: {info.get('name')} {info.get('version')}")
        send({"jsonrpc": "2.0", "method": "notifications/initialized"})

        mode = sys.argv[1] if len(sys.argv) > 1 else "list"
        tools = request(2, "tools/list", {})["result"]["tools"]
        if mode == "list":
            flt = sys.argv[2] if len(sys.argv) > 2 else ""
            names = sorted(t["name"] for t in tools if flt in t["name"])
            print(f"{len(tools)} tools; {len(names)} match '{flt}':")
            print("  " + "\n  ".join(names))
        elif mode == "schema":
            tool = next(t for t in tools if t["name"] == sys.argv[2])
            print(tool.get("description", "")[:600])
            print(json.dumps(tool.get("inputSchema", {}), indent=1)[:2500])
        elif mode == "call":
            args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
            res = request(3, "tools/call", {"name": sys.argv[2], "arguments": args}, timeout=120)
            print(json.dumps(res.get("result", res.get("error")), ensure_ascii=False, indent=1)[:4000])
    finally:
        proc.terminate()


if __name__ == "__main__":
    main()
