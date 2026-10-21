"""Spike S13: a stdio pass-through around eplanmcp that writes every JSON-RPC line, with its direction and time, to a
log file, so the client's behaviour (progress tokens, cancellation) can be read afterwards. Writes nothing else to stdout.
Usage (as an MCP server command): python -I mcp_tap.py <log file> <eplanmcp.exe>"""
import subprocess
import sys
import threading
import time

log_path, exe = sys.argv[1], sys.argv[2]
log = open(log_path, 'a', encoding='utf-8')
lock = threading.Lock()
start = time.time()


def write(direction, line):
    text = line.decode('utf-8', errors='replace').rstrip('\n')
    if len(text) > 4000:
        text = text[:4000] + ' …(' + str(len(text)) + ' chars)'
    with lock:
        log.write(f'{time.time() - start:8.2f} {direction} {text}\n')
        log.flush()


child = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)


def client_to_server():
    for line in sys.stdin.buffer:
        write('C>S', line)
        child.stdin.write(line)
        child.stdin.flush()
    child.stdin.close()


threading.Thread(target=client_to_server, daemon=True).start()
for line in child.stdout:
    write('S>C', line)
    sys.stdout.buffer.write(line)
    sys.stdout.buffer.flush()
child.wait()
