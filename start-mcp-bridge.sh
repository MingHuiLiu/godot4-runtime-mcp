#!/usr/bin/env python3
"""
Godot MCP Stdio Bridge v2.0
Bridges between MCP stdio transport (Claude Desktop) and Godot MCP HTTP+SSE server.
No .NET McpServer needed - connects directly to the Godot plugin.
"""

import json
import sys
import urllib.request
import urllib.error
import re
import threading
import time

GODOT_HTTP_URL = "http://127.0.0.1:7777"
SESSION_ID = None
SSE_THREAD = None
responses = {}
responses_lock = threading.Lock()
response_event = threading.Event()


def get_session():
    """Establish SSE connection and get session_id."""
    global SESSION_ID, SSE_THREAD
    
    try:
        req = urllib.request.Request(f"{GODOT_HTTP_URL}/sse")
        req.method = "GET"
        
        # We need to read the first SSE event to get the endpoint
        # Use a short connection to grab the initial event
        response = urllib.request.urlopen(req, timeout=5)
        
        # Read the first line to get endpoint event
        data = b""
        while True:
            line = response.readline()
            if not line:
                break
            data += line
            decoded = data.decode("utf-8")
            
            # Look for endpoint event
            if "event: endpoint" in decoded:
                # Next line should have the data
                continue
            if "data: /messages" in decoded:
                match = re.search(r'session_id=([a-f0-9]+)', decoded)
                if match:
                    SESSION_ID = match.group(1)
                    print(f"[Bridge] Connected to Godot MCP - session: {SESSION_ID}", file=sys.stderr)
                    response.close()
                    return True
                    
        response.close()
    except Exception as e:
        print(f"[Bridge] SSE connection failed: {e}", file=sys.stderr)
    
    return False


def read_sse():
    """Read SSE stream for responses (for future use)."""
    global SESSION_ID
    try:
        req = urllib.request.Request(f"{GODOT_HTTP_URL}/sse")
        req.method = "GET"
        response = urllib.request.urlopen(req, timeout=None)
        
        buffer = ""
        for line_bytes in response:
            line = line_bytes.decode("utf-8").strip()
            
            if line.startswith("event: endpoint"):
                buffer = ""
            elif line.startswith("data: /messages"):
                match = re.search(r'session_id=([a-f0-9]+)', line)
                if match:
                    SESSION_ID = match.group(1)
                    print(f"[Bridge] SSE session renewed: {SESSION_ID}", file=sys.stderr)
            elif line.startswith("data: "):
                data_str = line[6:]
                try:
                    msg = json.loads(data_str)
                    if "id" in msg and msg["id"] is not None:
                        with responses_lock:
                            responses[str(msg["id"])] = msg
                            response_event.set()
                except json.JSONDecodeError:
                    pass
    except Exception as e:
        print(f"[Bridge] SSE reader error: {e}", file=sys.stderr)


def send_mcp_request(request):
    """Send MCP JSON-RPC request to Godot and return response."""
    if not SESSION_ID:
        return {"jsonrpc": "2.0", "id": request.get("id"), "error": {"code": -32000, "message": "No session"}}
    
    url = f"{GODOT_HTTP_URL}/messages?session_id={SESSION_ID}"
    data = json.dumps(request).encode("utf-8")
    
    try:
        req = urllib.request.Request(url, data=data, headers={"Content-Type": "application/json"})
        req.method = "POST"
        response = urllib.request.urlopen(req, timeout=30)
        result = json.loads(response.read().decode("utf-8"))
        return result
    except urllib.error.HTTPError as e:
        error_body = e.read().decode("utf-8")
        try:
            return json.loads(error_body)
        except:
            return {"jsonrpc": "2.0", "id": request.get("id"), "error": {"code": e.code, "message": error_body}}
    except Exception as e:
        return {"jsonrpc": "2.0", "id": request.get("id"), "error": {"code": -32000, "message": str(e)}}


def reconnect():
    """Try to reconnect and reinitialize."""
    global SESSION_ID, SSE_THREAD
    SESSION_ID = None
    
    for attempt in range(30):
        print(f"[Bridge] Reconnecting (attempt {attempt + 1})...", file=sys.stderr)
        if get_session():
            # Re-initialize
            init_request = {
                "jsonrpc": "2.0",
                "id": "init",
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "godot-mcp-bridge", "version": "2.0.0"}
                }
            }
            result = send_mcp_request(init_request)
            if result and "result" in result:
                print(f"[Bridge] Re-initialized successfully", file=sys.stderr)
                return True
        time.sleep(1)
    
    return False


def main():
    # Startup banner
    print("[Bridge] Godot MCP Stdio Bridge v2.0", file=sys.stderr)
    print("[Bridge] Connecting to Godot MCP server...", file=sys.stderr)
    print("[Bridge] Make sure Godot is running with the plugin enabled.", file=sys.stderr)
    print("[Bridge] Target: " + GODOT_HTTP_URL, file=sys.stderr)
    
    # Connect to Godot MCP server
    if not get_session():
        print("[Bridge] WARNING: Could not connect to Godot. Will retry on first request.", file=sys.stderr)
    
    # Read stdin for JSON-RPC messages
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        
        try:
            request = json.loads(line)
        except json.JSONDecodeError:
            continue
        
        request_id = request.get("id")
        
        # Handle notifications (no id)
        if request_id is None:
            continue
        
        # Try to reconnect if we lost the session
        if not SESSION_ID:
            print("[Bridge] No session, reconnecting...", file=sys.stderr)
            if not reconnect():
                error_response = {
                    "jsonrpc": "2.0",
                    "id": str(request_id),
                    "error": {"code": -32000, "message": "Cannot connect to Godot. Make sure Godot is running."}
                }
                print(json.dumps(error_response), flush=True)
                continue
        
        # Send to Godot and get response
        result = send_mcp_request(request)
        
        if result:
            print(json.dumps(result), flush=True)
        else:
            error_response = {
                "jsonrpc": "2.0",
                "id": str(request_id),
                "error": {"code": -32000, "message": "No response from Godot"}
            }
            print(json.dumps(error_response), flush=True)


if __name__ == "__main__":
    main()
