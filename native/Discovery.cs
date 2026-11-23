using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class Discovery
{
    private const int Timeout = 450;
    private static readonly Dictionary<string, string> ClientNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
        { "happ.exe", "happ" }, { "happ-service.exe", "happ" },
        { "koala clash.exe", "koala" },
        { "clash-verge.exe", "verge" }, { "clash-verge-service.exe", "verge" },
        { "clash_verge_service.exe", "verge" }, { "verge-mihomo.exe", "verge" },
        { "v2rayN.exe", "v2rayn" }
    };
    private static readonly HashSet<string> CoreNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "xray.exe", "happ-xray.exe", "v2ray.exe", "sing-box.exe", "mihomo.exe", "mihomo-alpha.exe", "clash-meta.exe"
    };

    public static Candidate[] Find()
    {
        var processes = SnapshotProcesses();
        var listeners = ReadListeners()
            .Select(item => new { item.Port, Client = Identify(item.Pid, processes) })
            .Where(item => item.Client != null)
            .GroupBy(item => new { item.Client, item.Port }).Select(group => group.Key)
            .ToArray();
        var tasks = new Dictionary<string, Task<Dictionary<string, HashSet<int>>>>();
        if (listeners.Any(item => item.Client == "koala")) tasks["koala"] = Task.Run(() => ReadMihomoPorts(@"Koala-Clash\mihomo"));
        if (listeners.Any(item => item.Client == "verge")) tasks["verge"] = Task.Run(() => ReadMihomoPorts("verge-mihomo"));
        var systemPorts = ReadWindowsProxy();
        Task.WaitAll(tasks.Values.Cast<Task>().ToArray(), 900);

        var checks = listeners.OrderBy(item => item.Port).Take(24).Select(item => Task.Run(() => {
            var known = tasks.ContainsKey(item.Client) && tasks[item.Client].Status == TaskStatus.RanToCompletion
                ? tasks[item.Client].Result : null;
            return Inspect(item.Client, item.Port, known, systemPorts.Contains(item.Port));
        })).ToArray();
        Task.WaitAll(checks.Cast<Task>().ToArray(), 2800);
        return checks.Where(check => check.Status == TaskStatus.RanToCompletion).SelectMany(check => check.Result)
            .OrderBy(result => result.client).ThenBy(result => result.authRequired)
            .ThenBy(result => result.protocol).ThenBy(result => result.port).ToArray();
    }

    private static List<Candidate> Inspect(string client, int port, Dictionary<string, HashSet<int>> known, bool systemProxy)
    {
        var results = new List<Candidate>();
        var socks = ProbeSocks(port);
        if (socks != null) results.Add(new Candidate(client, port, "socks5", socks == 2, "socks-greeting"));
        var httpInConfig = known != null && known["http"].Contains(port);
        if (httpInConfig || systemProxy) {
            var auth = RequiresHttpAuth(port);
            results.Add(new Candidate(client, port, "http", auth, httpInConfig ? "mihomo-config" : "windows-proxy"));
        } else if (RequiresHttpAuth(port)) {
            results.Add(new Candidate(client, port, "http", true, "http-auth-response"));
        }
        return results;
    }

    private static TcpClient Connect(int port)
    {
        var connection = new TcpClient(AddressFamily.InterNetwork);
        try {
            var pending = connection.BeginConnect(IPAddress.Loopback, port, null, null);
            using (var ready = pending.AsyncWaitHandle) {
                if (!ready.WaitOne(Timeout)) throw new IOException("Connection timeout");
                connection.EndConnect(pending);
            }
            connection.ReceiveTimeout = Timeout;
            connection.SendTimeout = Timeout;
            return connection;
        } catch { connection.Close(); throw; }
    }

    private static int? ProbeSocks(int port)
    {
        try {
            using (var tcp = Connect(port))
            using (var stream = tcp.GetStream()) {
                // A SOCKS greeting negotiates an auth method without a destination or credentials.
                stream.Write(new byte[] { 5, 2, 0, 2 }, 0, 4);
                if (stream.ReadByte() != 5) return null;
                var method = stream.ReadByte();
                return method == 0 || method == 2 ? (int?)method : null;
            }
        } catch (IOException) { return null; } catch (SocketException) { return null; }
    }

    private static bool RequiresHttpAuth(int port)
    {
        try {
            using (var tcp = Connect(port))
            using (var stream = tcp.GetStream()) {
                // This request targets the listener itself. It contains no CONNECT or remote URL.
                var request = Encoding.ASCII.GetBytes("OPTIONS * HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
                stream.Write(request, 0, request.Length);
                var buffer = new byte[512];
                var size = stream.Read(buffer, 0, buffer.Length);
                return Regex.IsMatch(Encoding.ASCII.GetString(buffer, 0, size), @"^HTTP/1\.[01] 407(?: |\r)");
            }
        } catch (IOException) { return false; } catch (SocketException) { return false; }
    }

    private static Dictionary<string, HashSet<int>> ReadMihomoPorts(string name)
    {
        var ports = new Dictionary<string, HashSet<int>> { { "http", new HashSet<int>() } };
        try {
            using (var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous)) {
                pipe.Connect(300);
                var request = Encoding.ASCII.GetBytes("GET /configs HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n");
                if (!pipe.WriteAsync(request, 0, request.Length).Wait(250)) return ports;
                var buffer = new byte[16384];
                var response = new StringBuilder();
                var watch = Stopwatch.StartNew();
                while (response.Length < 65536 && watch.ElapsedMilliseconds < Timeout) {
                    var read = pipe.ReadAsync(buffer, 0, buffer.Length);
                    if (!read.Wait(Math.Max(1, Timeout - (int)watch.ElapsedMilliseconds))) return ports;
                    if (read.Result == 0) break;
                    response.Append(Encoding.UTF8.GetString(buffer, 0, read.Result));
                }
                var raw = response.ToString();
                var start = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (!Regex.IsMatch(raw, @"^HTTP/1\.[01] 200 ") || start < 0 || raw.Length > 65536) return ports;
                var config = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(raw.Substring(start + 4));
                foreach (var key in new[] { "port", "mixed-port" }) {
                    object value;
                    int port;
                    if (config.TryGetValue(key, out value) && Int32.TryParse(Convert.ToString(value), out port) && port > 0 && port <= 65535) ports["http"].Add(port);
                }
            }
        } catch (Exception error) {
            if (!(error is IOException || error is TimeoutException || error is ArgumentException ||
                  error is InvalidOperationException || error is UnauthorizedAccessException || error is AggregateException)) throw;
        }
        return ports;
    }

    private static HashSet<int> ReadWindowsProxy()
    {
        var result = new HashSet<int>();
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings")) {
            if (key == null || !Object.Equals(key.GetValue("ProxyEnable"), 1)) return result;
            var value = key.GetValue("ProxyServer") as string ?? "";
            foreach (var part in value.Split(';')) {
                var match = Regex.Match(part.Trim(), @"^(?:(?:https?)=)?(?:http://)?(?:127\.0\.0\.1|localhost):(\d{1,5})$", RegexOptions.IgnoreCase);
                int port;
                if (match.Success && Int32.TryParse(match.Groups[1].Value, out port) && port > 0 && port <= 65535) result.Add(port);
            }
        }
        return result;
    }

    private static string Identify(int pid, Dictionary<int, ProcessEntry> processes)
    {
        ProcessEntry entry;
        if (!processes.TryGetValue(pid, out entry)) return null;
        string client;
        if (ClientNames.TryGetValue(entry.Name, out client)) return client;
        if (!CoreNames.Contains(entry.Name)) return null;
        // Only known core executables are attributed to a parent VPN client.
        if (MatchesInstallPath(pid, out client)) return client;
        for (int i = 0; i < 8 && processes.TryGetValue(entry.Parent, out entry); i++) {
            if (ClientNames.TryGetValue(entry.Name, out client)) return client;
        }
        return null;
    }

    private static bool MatchesInstallPath(int pid, out string client)
    {
        client = null;
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return false;
        try {
            var path = new StringBuilder(2048);
            var length = path.Capacity;
            if (!QueryFullProcessImageName(handle, 0, path, ref length)) return false;
            var fullpath = path.ToString();
            if (fullpath.IndexOf(@"\FlyFrogLLC\Happ\", StringComparison.OrdinalIgnoreCase) >= 0) client = "happ";
            else if (fullpath.IndexOf(@"\Koala Clash\", StringComparison.OrdinalIgnoreCase) >= 0) client = "koala";
            else if (Regex.IsMatch(fullpath, @"\\Clash[ -]Verge(?: Rev)?\\", RegexOptions.IgnoreCase)) client = "verge";
            else if (fullpath.IndexOf(@"\v2rayN\", StringComparison.OrdinalIgnoreCase) >= 0) client = "v2rayn";
            return client != null;
        } finally { CloseHandle(handle); }
    }

    private static Dictionary<int, ProcessEntry> SnapshotProcesses()
    {
        var result = new Dictionary<int, ProcessEntry>();
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) return result;
        try {
            var entry = new ProcessEntry { Size = Marshal.SizeOf(typeof(ProcessEntry)) };
            if (Process32First(handle, ref entry)) {
                do { result[entry.Pid] = entry; } while (Process32Next(handle, ref entry));
            }
        } finally { CloseHandle(handle); }
        return result;
    }

    private static List<Listener> ReadListeners()
    {
        var result = new List<Listener>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        for (int attempt = 0; attempt < 3 && size > 0 && size <= 4 * 1024 * 1024; attempt++) {
            var capacity = size;
            var table = Marshal.AllocHGlobal(capacity);
            try {
                var code = GetExtendedTcpTable(table, ref size, false, 2, 3, 0);
                if (code == 122) continue;
                if (code != 0) return result;
                var count = Math.Min(Marshal.ReadInt32(table), (capacity - 4) / 24);
                for (int index = 0; index < count; index++) {
                    var row = 4 + 24 * index;
                    int address = Marshal.ReadInt32(table, row + 4);
                    // 0.0.0.0 accepts loopback too. No connection is made to the LAN address.
                    if (address != 0 && address != 0x0100007f) continue;
                    int port = (ushort)IPAddress.NetworkToHostOrder((short)Marshal.ReadInt32(table, row + 8));
                    if (port > 0) result.Add(new Listener { Port = port, Pid = Marshal.ReadInt32(table, row + 20) });
                }
                return result;
            } finally { Marshal.FreeHGlobal(table); }
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry {
        public int Size, Usage, Pid;
        public IntPtr Heap;
        public int Module, Threads, Parent, Priority, Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    private struct Listener { public int Port, Pid; }

    [DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int kind, int reserved);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(int access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder output, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class Candidate
{
    public string client { get; private set; }
    public string host { get { return "127.0.0.1"; } }
    public int port { get; private set; }
    public string protocol { get; private set; }
    public bool authRequired { get; private set; }
    public string source { get; private set; }

    public Candidate(string client, int port, string protocol, bool authRequired, string source)
    {
        this.client = client; this.port = port; this.protocol = protocol; this.authRequired = authRequired; this.source = source;
    }
}
