"""Spike S13, part 2 driver: runs Claude Code headless in stream-json mode against slow_server.py.
Usage: drive.py <claude.exe> <out dir> interrupt|long
- interrupt: asks for slow_wait(120); 8 s after the server sees tools/call, sends the SDK's interrupt control request.
- long: asks for slow_wait(150) and lets it run, to see what the client does past 2 minutes.
Writes the client's events to <out>/<mode>-events.jsonl and the server's log to <out>/<mode>-server.log."""
import json
import os
import subprocess
import sys
import threading
import time

claude, out, mode = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(out, exist_ok=True)
here = os.path.dirname(os.path.abspath(__file__))
server_log = os.path.join(out, f'{mode}-server.log')
events_path = os.path.join(out, f'{mode}-events.jsonl')
for p in (server_log, events_path):
    if os.path.exists(p):
        os.remove(p)
config = os.path.join(out, f'{mode}-mcp.json')
json.dump({'mcpServers': {'slow': {'command': sys.executable, 'args': ['-I', os.path.join(here, 'slow_server.py'), server_log]}}},
          open(config, 'w', encoding='utf-8'))
seconds = 120 if mode == 'interrupt' else 150
prompt = f'Call the slow_wait tool once with seconds={seconds} and then tell me what it returned. Do nothing else.'

proc = subprocess.Popen(
    [claude, '-p', '--input-format', 'stream-json', '--output-format', 'stream-json', '--verbose',
     '--mcp-config', config, '--strict-mcp-config', '--allowedTools', 'mcp__slow__slow_wait', '--model', 'claude-sonnet-5-5'],
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
start = time.time()
events = open(events_path, 'w', encoding='utf-8')
done = threading.Event()


def read_events():
    for raw in proc.stdout:
        line = raw.decode('utf-8', errors='replace').strip()
        if not line:
            continue
        events.write(f'{time.time() - start:8.2f} {line[:3000]}\n')
        events.flush()
        try:
            if json.loads(line).get('type') == 'result':
                done.set()
        except json.JSONDecodeError:
            pass
    done.set()


threading.Thread(target=read_events, daemon=True).start()


def send(message):
    proc.stdin.write((json.dumps(message) + '\n').encode('utf-8'))
    proc.stdin.flush()


send({'type': 'user', 'message': {'role': 'user', 'content': prompt}})


def server_saw(text):
    try:
        return text in open(server_log, encoding='utf-8').read()
    except FileNotFoundError:
        return False


if mode == 'interrupt':
    while not server_saw('"tools/call"') and time.time() - start < 120 and not done.is_set():
        time.sleep(0.5)
    time.sleep(8)
    print(f'{time.time() - start:6.1f}s sending interrupt', flush=True)
    send({'type': 'control_request', 'request_id': 's13-interrupt-1', 'request': {'subtype': 'interrupt'}})
    done.wait(60)
else:
    done.wait(seconds + 240)

time.sleep(3)
try:
    proc.stdin.close()
except OSError:
    pass
try:
    proc.wait(timeout=30)
except subprocess.TimeoutExpired:
    proc.kill()
print(f'{time.time() - start:6.1f}s exit {proc.returncode}', flush=True)
print('--- server log (C>S methods and notes) ---')
for line in open(server_log, encoding='utf-8'):
    if '"method"' in line and ('C>S' in line) or 'NOTE' in line or ('S>C' in line and '"result"' in line and 'waited' in line):
        print(line.rstrip()[:300])
