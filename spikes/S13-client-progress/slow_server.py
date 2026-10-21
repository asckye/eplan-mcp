"""Spike S13, part 2: a stdio MCP server with one slow tool, for watching what a client does on an interrupt and on a
call longer than 2 minutes. It logs every message with its time to the file given as argv[1]; it writes nothing else
to stdout. Tool slow_wait(seconds) sends a progress notification every 5 s while it waits (when the call has a
progress token) and stops early when notifications/cancelled names its request."""
import json
import sys
import threading
import time

log_path = sys.argv[1]
log = open(log_path, 'a', encoding='utf-8')
lock = threading.Lock()
start = time.time()
cancelled = set()


def write_log(direction, payload):
    with lock:
        log.write(f'{time.time() - start:8.2f} {direction} {json.dumps(payload, ensure_ascii=False)[:2000]}\n')
        log.flush()


def send(message):
    write_log('S>C', message)
    with lock:
        sys.stdout.write(json.dumps(message) + '\n')
        sys.stdout.flush()


def slow_call(request_id, arguments, token):
    seconds = float(arguments.get('seconds', 10))
    began = time.time()
    step = 0
    while time.time() - began < seconds:
        if request_id in cancelled:
            write_log('NOTE', {'stopped': request_id, 'after': round(time.time() - began, 1)})
            return  # a cancelled request gets no response (MCP cancellation)
        time.sleep(0.5)
        if token is not None and time.time() - began >= (step + 1) * 5:
            step += 1
            send({'jsonrpc': '2.0', 'method': 'notifications/progress',
                  'params': {'progressToken': token, 'progress': step, 'message': f'waiting ({step * 5} s)'}})
    send({'jsonrpc': '2.0', 'id': request_id,
          'result': {'content': [{'type': 'text', 'text': f'waited {seconds:.0f} s'}], 'isError': False}})


for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    message = json.loads(line)
    write_log('C>S', message)
    method = message.get('method')
    if method == 'initialize':
        send({'jsonrpc': '2.0', 'id': message['id'], 'result': {
            'protocolVersion': message['params'].get('protocolVersion', '2025-06-18'),
            'capabilities': {'tools': {}},
            'serverInfo': {'name': 's13-slow', 'version': '0'}}})
    elif method == 'tools/list':
        send({'jsonrpc': '2.0', 'id': message['id'], 'result': {'tools': [{
            'name': 'slow_wait',
            'description': 'Waits the given number of seconds and then returns. A test tool.',
            'inputSchema': {'type': 'object', 'properties': {'seconds': {'type': 'number'}}, 'required': ['seconds']}}]}})
    elif method == 'tools/call':
        params = message.get('params', {})
        token = (params.get('_meta') or {}).get('progressToken')
        threading.Thread(target=slow_call, args=(message['id'], params.get('arguments', {}), token), daemon=True).start()
    elif method == 'notifications/cancelled':
        cancelled.add(message['params'].get('requestId'))
    elif 'id' in message and method is not None:
        send({'jsonrpc': '2.0', 'id': message['id'], 'result': {}})
