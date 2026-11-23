"""Local proxy discovery and per-user Chrome Native Messaging registration."""

import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import re
import shlex
import socket
import struct
import subprocess
import sys
import tempfile


HOST_NAME = "org.siteproxyswitcher.discovery"
ORIGIN = "chrome-extension://hbgkchkhcgnplackadelhkchdacdgeod/"
MAX_CANDIDATES = 24
LOOPBACK = {"127.0.0.1", "[::1]"}


def system_output(*args):
    try:
        return subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
                              text=True, errors="replace", timeout=3, check=False).stdout
    except (OSError, subprocess.TimeoutExpired):
        return ""


def process_table(output):
    processes = {}
    for line in output.splitlines():
        fields = line.split(maxsplit=2)
        if len(fields) == 3 and fields[0].isdigit() and fields[1].isdigit():
            processes[int(fields[0])] = (int(fields[1]), fields[2])
    return processes


def client_for(pid, processes):
    for _ in range(8):
        if pid not in processes:
            return None
        pid, command = processes[pid]
        path = command.casefold()
        name = path.rsplit("/", 1)[-1]
        if name in {"happ", "happ-desktop"} or "/happ.app/" in path:
            return "happ"
        if name in {"koala clash", "koala-clash"} or "/koala clash.app/" in path:
            return "koala"
        if name in {"clash verge", "clash-verge", "verge-mihomo", "verge-mihomo-alpha"} or "/clash verge.app/" in path:
            return "verge"
        if name == "v2rayn" or "/v2rayn.app/" in path:
            return "v2rayn"
    return None


def listeners(output):
    pid, family = None, None
    for line in output.splitlines():
        field, value = line[:1], line[1:]
        if field == "p":
            pid = int(value) if value.isdigit() else None
            family = None
        elif field == "t":
            family = value
        elif field == "n" and pid is not None:
            host, sep, port_text = value.rpartition(":")
            if host == "*":
                host = {"IPv4": "127.0.0.1", "IPv6": "[::1]"}.get(family)
            if sep and host in LOOPBACK and port_text.isdigit() and 0 < int(port_text) <= 65535:
                yield pid, host, int(port_text)


def system_proxy(output):
    # Match only top-level properties. Nested entries can describe scoped interfaces.
    properties = dict(re.findall(r"^  (HTTPEnable|HTTPProxy|HTTPPort) : (\S+)\s*$", output, re.MULTILINE))
    if properties.get("HTTPEnable") != "1":
        return None
    host = properties.get("HTTPProxy", "").lower()
    if host == "localhost":
        host = "127.0.0.1"
    elif host == "::1":
        host = "[::1]"
    port = properties.get("HTTPPort", "")
    if host in LOOPBACK and port.isdigit() and 0 < int(port) <= 65535:
        return host, int(port)
    return None


def probe(listener, http_proxy):
    client, host, port = listener
    address = (host.strip("[]"), port)
    found = []

    def add(protocol, auth_required, source):
        found.append({"client": client, "host": host, "port": port, "protocol": protocol,
                      "authRequired": auth_required, "source": source})

    try:
        with socket.create_connection(address, timeout=0.25) as conn:
            conn.sendall(b"\x05\x02\x00\x02")
            hello = conn.recv(2)
            if len(hello) == 1:
                hello += conn.recv(1)
            if hello in (b"\x05\x00", b"\x05\x02"):
                add("socks5", hello == b"\x05\x02", "socks5-handshake")
    except OSError:
        pass

    try:
        with socket.create_connection(address, timeout=0.25) as conn:
            conn.sendall(b"OPTIONS * HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n")
            response = conn.recv(256)
            status = re.match(rb"^HTTP/1\.[01] (\d{3})(?: |\r\n)", response)
            auth_required = bool(status and status[1] == b"407")
            if status and (auth_required or (host, port) == http_proxy):
                add("http", auth_required, "system-proxy" if (host, port) == http_proxy else "http-407")
    except OSError:
        pass
    return found


def discover():
    processes = process_table(system_output("/bin/ps", "-ax", "-o", "pid=", "-o", "ppid=", "-o", "comm="))
    open_ports = system_output("/usr/sbin/lsof", "-nP", "-a", "-u", str(os.getuid()),
                               "-iTCP", "-sTCP:LISTEN", "-Fpnt")
    http_proxy = system_proxy(system_output("/usr/sbin/scutil", "--proxy"))
    local = {}
    for pid, host, port in listeners(open_ports):
        client = client_for(pid, processes)
        if client:
            local[(host, port)] = (client, host, port)
    ordered = sorted(local, key=lambda endpoint: (endpoint != http_proxy, endpoint))[:MAX_CANDIDATES]
    with ThreadPoolExecutor(max_workers=8) as pool:
        groups = pool.map(lambda endpoint: probe(local[endpoint], http_proxy), ordered)
        return [item for group in groups for item in group][:MAX_CANDIDATES]


def read_json(path):
    with path.open("rb") as file:
        data = file.read(65537)
    if len(data) > 65536:
        raise ValueError("Manifest exceeds its size limit.")
    return json.loads(data.decode("utf-8"))


def replace_file(path, content, mode=0o600):
    fd, name = tempfile.mkstemp(dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as file:
            os.chmod(name, mode)
            file.write(content)
        os.replace(name, path)
    finally:
        Path(name).unlink(missing_ok=True)


def installation_paths(support):
    helper = support / "SiteProxySwitcher"
    hosts = support / "Google" / "Chrome" / "NativeMessagingHosts"
    entry = helper / "site-proxy-discovery"
    source = helper / "discovery.py"
    manifest = hosts / f"{HOST_NAME}.json"
    if any(path.is_symlink() for path in (helper, hosts, manifest)):
        raise ValueError("Unexpected symlink in the discovery installation paths.")
    if manifest.exists():
        existing = read_json(manifest)
        if existing.get("name") != HOST_NAME or existing.get("path") != str(entry) or existing.get("allowed_origins") != [ORIGIN]:
            raise ValueError("The native host manifest belongs to another installation.")
    return entry, source, manifest


def install(extension_manifest, support):
    extension = read_json(extension_manifest)
    if extension.get("name") != "Site Proxy Switcher":
        raise ValueError("Unexpected extension manifest.")
    key = base64.b64decode(extension.get("key", ""), validate=True)
    extension_id = hashlib.sha256(key).hexdigest()[:32].translate(str.maketrans("0123456789abcdef", "abcdefghijklmnop"))
    if f"chrome-extension://{extension_id}/" != ORIGIN:
        raise ValueError("Unexpected extension ID.")

    entry, source, manifest = installation_paths(support)
    entry.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    manifest.parent.mkdir(parents=True, exist_ok=True)
    python = shlex.quote(str(Path(sys.executable).resolve()))
    command = f'#!/bin/sh\nexec {python} -I -B {shlex.quote(str(source))} "$@"\n'
    replace_file(source, Path(__file__).read_bytes())
    replace_file(entry, command.encode("utf-8"), mode=0o700)
    registration = {"name": HOST_NAME, "description": "Local proxy discovery for Site Proxy Switcher",
                    "path": str(entry), "type": "stdio", "allowed_origins": [ORIGIN]}
    replace_file(manifest, json.dumps(registration, indent=2).encode("utf-8"))
    print("Discovery helper registered for the current macOS account.")


def uninstall(support):
    entry, source, manifest = installation_paths(support)
    for path in (manifest, entry, source):
        path.unlink(missing_ok=True)
    print("Discovery helper removed from the current macOS account.")


def handle(origin, stdin, stdout):
    if origin != ORIGIN:
        return 1
    header = stdin.read(4)
    if len(header) != 4:
        return 1
    length, = struct.unpack("<I", header)
    if not 1 <= length <= 1024:
        return 1
    payload = stdin.read(length)
    if len(payload) != length:
        return 1
    try:
        request = json.loads(payload.decode("utf-8"), object_pairs_hook=list)
    except (ValueError, RecursionError):
        return 1
    if request != [("command", "discover")]:
        return 1
    try:
        reply = json.dumps({"ok": True, "candidates": discover()}).encode("utf-8")
    except (OSError, ValueError):
        reply = b'{"ok":false}'
    stdout.write(struct.pack("<I", len(reply)))
    stdout.write(reply)
    stdout.flush()
    return 0


def main(args):
    if sys.platform != "darwin" or sys.version_info < (3, 8):
        print("This helper requires macOS and Python 3.8 or newer.", file=sys.stderr)
        return 1
    if args and args[0] in ("--install", "--uninstall"):
        try:
            support = Path.home() / "Library" / "Application Support"
            if args[0] == "--install" and len(args) == 2:
                install(Path(args[1]), support)
            elif args == ["--uninstall"]:
                uninstall(support)
            else:
                return 1
        except (AttributeError, OSError, ValueError) as error:
            print(error, file=sys.stderr)
            return 1
        return 0
    return handle(args[0], sys.stdin.buffer, sys.stdout.buffer) if args else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
