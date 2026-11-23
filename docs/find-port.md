# Find your local proxy port

[Русский](find-port.ru.md) · [README](../README.md)

## 1. Check your VPN client

Connect the client and look for a local **HTTP proxy**, **SOCKS5 proxy**, or **Mixed port**. On the same computer, the address is usually `127.0.0.1`. Copy the actual port from the app. The numbers below are examples.

| Client | Where to look |
| --- | --- |
| Koala Clash or Clash Verge Rev | In the client settings, look for the local proxy port or the port configuration panel. In a Mihomo configuration, `mixed-port` accepts HTTP and SOCKS5, `port` is HTTP, and `socks-port` is SOCKS5. Keep `external-controller` for the client API. [Mihomo port reference](https://wiki.metacubex.one/en/config/inbound/port/) |
| Happ | In the app settings, look for HTTP and SOCKS ports. Keep LAN access off for same-machine use. [Happ desktop releases](https://github.com/Happ-proxy/happ-desktop/releases) |
| v2rayN | Check the local listening port under Settings and whether it is a mixed or protocol-specific listener. [v2rayN wiki](https://github.com/2dust/v2rayN/wiki/Description-of-some-ui) |
| Other client | Look for a local proxy setting or ask the provider whether its client offers one. A VPN client that offers only a system tunnel needs a separate local proxy to work with this extension. |

## 2. Check the system if the client hides the port

- **Windows.** Go to **Settings → Network & internet → Proxy**. If the client has enabled a manual system proxy, look at its address and port. If the client shows no port, find its or its network core's PID in Task Manager → Details. Run `Get-NetTCPConnection -State Listen -OwningProcess 1234` in PowerShell with the actual PID. Look for `LocalPort` on `127.0.0.1` or `::1`. [Windows proxy settings](https://support.microsoft.com/en-us/windows/experience/connectivity-networking/use-a-proxy-server-in-windows) · [Get-NetTCPConnection](https://learn.microsoft.com/en-us/powershell/module/nettcpip/get-nettcpconnection)
- **macOS.** Go to **System Settings → Network → your connection → Details → Proxies**, or run `scutil --proxy` in Terminal. For an enabled HTTP proxy, look for `HTTPProxy` and `HTTPPort`. To find listeners by PID from Activity Monitor, run `lsof -nP -a -p 1234 -iTCP -sTCP:LISTEN` with the actual PID. [macOS proxy settings](https://support.apple.com/en-us/guide/mac-help/mchlp2591/mac) · [lsof reference](https://github.com/lsof-org/lsof/blob/master/docs/manpage.md)

You may see several ports for one client. A listening port by itself doesn't identify the protocol. Check the app's settings and test the matching port.

## 3. Test the port and enter it in Chrome

In macOS Terminal, run one of these with your proxy's port. In Windows Command Prompt (`cmd.exe`), use `curl.exe` in place of `curl`. The test sends a single request to `example.com` through the local proxy. [curl reference](https://curl.se/docs/manpage.html)

```sh
curl -q --noproxy "" --proxy http://127.0.0.1:7890 --max-time 10 --head https://example.com/
curl -q --noproxy "" --proxy socks5h://127.0.0.1:10808 --max-time 10 --head https://example.com/
```

If you get a response from the test site, enter `http://127.0.0.1:7890` or `socks5://127.0.0.1:10808` with your actual port under **Configure proxy**. Use `[::1]` in the address if the proxy listens only on IPv6. A successful test checks the proxy connection. To verify that it goes through the VPN, check the route in your VPN client's connection log.

- **Connection refused or timeout.** Verify that the client is running and check the port and protocol.
- **HTTP 407.** The HTTP proxy requires authentication. Chrome may prompt for credentials. For SOCKS5, choose a trusted local proxy that doesn't require SOCKS5 authentication, or use the HTTP port. [Chromium proxy protocols](https://chromium.googlesource.com/chromium/src/+/main/net/docs/proxy.md#proxy-server-schemes)
