# Security

[Русский](SECURITY.ru.md) · [README](README.md)

## Chrome permissions

| Permission | Purpose |
| --- | --- |
| `activeTab` | Reads the active tab's address after you open the extension menu and reloads that tab after a rule change. The extension saves only the domain. |
| `proxy` | Applies proxy rules to regular tabs in this Chrome profile. |
| `storage` | Stores the proxy address and selected domains in `chrome.storage.local`. |
| `nativeMessaging` | Chrome requests this optional permission when you click **Find local proxies**. The extension asks Chrome to remove it after the attempt. Manual entry works without it. |

The extension has no history, cookie, or persistent website access permissions. It doesn't inject scripts into pages or collect telemetry. [Chrome permissions](https://developer.chrome.com/docs/extensions/reference/api/permissions) · [activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab)

## Discovery helpers

- Both helpers run as the current user when Chrome starts them through Native Messaging. They use standard input and output and register no background service or network listener.
- The host manifest allows one Chrome extension ID. The helper checks the caller's origin and accepts only a `discover` request up to 1,024 bytes. The request cannot supply a command line, filename, destination, or port.
- Windows compiles the C# helper locally with .NET Framework, installs it under `%LOCALAPPDATA%\SiteProxySwitcher`, and registers one host in `HKCU`. Discovery uses local IPv4 TCP listeners, process ownership, and the Windows system HTTP proxy. For compatible Koala Clash and Clash Verge Rev versions, it reads only the `port` and `mixed-port` values from the local named pipe's `GET /configs` response.
- macOS installs the Python source and launcher in `~/Library/Application Support/SiteProxySwitcher` and the registration in Chrome's per-user Native Messaging folder. Discovery reads process names, your account's TCP listeners, and the system HTTP proxy settings. It doesn't read VPN client configurations.
- Both helpers probe selected loopback listeners with a SOCKS5 greeting or `OPTIONS *`. These requests carry no target website, page data, or credentials. Neither helper opens subscription files. The reply contains the recognized client, loopback address, port, protocol, authentication indicator, and source of the detection.

[Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging) · [Windows TCP table](https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable)

## Routing and trust

- The address field accepts `127.0.0.1`, `localhost` (normalized to `127.0.0.1`), or `[::1]` and an explicit port. It rejects usernames, passwords, remote addresses, and paths. Domain names go into the PAC script as JSON and appear as text in the UI.
- The PAC rule for a selected domain has just one proxy and no direct fallback. Disabling or removing the extension clears its configuration. While the rule is active, Chrome uses a direct route for other domains. This can override a separate system HTTP proxy.
- System VPN/TUN, DNS, and WebRTC have their own routing. Discovery doesn't measure the external IP, confirm use of a VPN tunnel, or test for leaks. Cross-origin dependencies take their own domain rules.
- A different local process can take the saved port after the VPN client exits. The extension does not continuously verify the owner. Process names are a discovery hint. The public manifest key makes the unpacked extension ID stable and does not authenticate the source build.
- Chrome still enforces its normal HTTPS certificate checks. The client behind the saved port handles selected-site traffic. Use a client you trust and keep the proxy bound to loopback for local use.

[Chromium proxy behavior](https://chromium.googlesource.com/chromium/src/+/main/net/docs/proxy.md) · [Chrome WebRTC network policy](https://developer.chrome.com/docs/extensions/reference/api/privacy#property-network)

## Report a vulnerability

In the GitHub repository, open **Security → Report a vulnerability**. Include the extension version, Chrome version, operating system, and steps using a test profile. Replace VPN keys, subscription URLs, and profile contents with dummy values before attaching files. [GitHub private vulnerability reporting](https://docs.github.com/en/code-security/how-tos/report-and-fix-vulnerabilities/report-privately)
