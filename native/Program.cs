using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class Program
{
    private const string Name = "org.siteproxyswitcher.discovery";
    private const string RegistryPath = @"Software\Google\Chrome\NativeMessagingHosts\" + Name;
    private static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SiteProxySwitcher");
    private static readonly string ManifestPath = Path.Combine(InstallDir, "host.json");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 12 };

    public static int Main(string[] args)
    {
        try {
            if (args.Length == 1 && args[0] == "--detect") {
                Console.WriteLine(Json.Serialize(new { ok = true, candidates = Discovery.Find() }));
                return 0;
            }
            if (args.Length == 2 && args[0] == "--install") { Install(args[1]); return 0; }
            if (args.Length == 1 && args[0] == "--unregister") { Unregister(); return 0; }
            if (args.Length == 2 && args[0] == "--extension-id") { Console.WriteLine(GetExtensionId(args[1])); return 0; }
            return HandleBrowser(args);
        } catch (Exception error) {
            if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal)) {
                Console.Error.WriteLine(error.Message);
            } else {
                Console.Error.WriteLine("Local discovery failed.");
            }
            return 1;
        }
    }

    private static int HandleBrowser(string[] args)
    {
        if (args.Length == 0 || !Regex.IsMatch(args[0], @"^chrome-extension://[a-p]{32}/$")) return 1;
        var manifest = Json.Deserialize<HostManifest>(ReadSmallText(ManifestPath));
        if (manifest.name != Name || manifest.allowed_origins == null || manifest.allowed_origins.Length != 1 ||
            !String.Equals(manifest.allowed_origins[0], args[0], StringComparison.Ordinal)) return 1;

        using (var input = new BinaryReader(Console.OpenStandardInput(), Encoding.UTF8))
        using (var output = new BinaryWriter(Console.OpenStandardOutput(), Encoding.UTF8)) {
            var length = input.ReadInt32();
            if (length < 1 || length > 1024) return 1;
            var bytes = input.ReadBytes(length);
            if (bytes.Length != length) return 1;
            Dictionary<string, string> message;
            try { message = Json.Deserialize<Dictionary<string, string>>(new UTF8Encoding(false, true).GetString(bytes)); }
            catch (Exception error) {
                if (!(error is ArgumentException || error is InvalidOperationException)) throw;
                return 1;
            }
            string command;
            if (message == null || message.Count != 1 || !message.TryGetValue("command", out command) || command != "discover") return 1;
            byte[] reply;
            try { reply = Encoding.UTF8.GetBytes(Json.Serialize(new { ok = true, candidates = Discovery.Find() })); }
            catch { reply = Encoding.UTF8.GetBytes("{\"ok\":false}"); }
            output.Write(reply.Length);
            output.Write(reply);
            output.Flush();
            return 0;
        }
    }

    private static string ReadSmallText(string path)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Manifest exceeds its size limit.");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static string GetExtensionId(string manifestPath)
    {
        var current = Json.Deserialize<Dictionary<string, object>>(ReadSmallText(manifestPath));
        object key, name;
        if (!current.TryGetValue("name", out name) || !Object.Equals(name, "Site Proxy Switcher") ||
            !current.TryGetValue("key", out key) || !(key is string)) throw new InvalidDataException("Invalid extension manifest.");
        var bytes = Convert.FromBase64String((string)key);
        if (bytes.Length < 128 || bytes.Length > 768 || bytes[0] != 0x30) throw new InvalidDataException("Invalid public key.");
        using (var sha = SHA256.Create()) {
            return String.Concat(sha.ComputeHash(bytes).Take(16).Select(value => new string(new[] { (char)('a' + (value >> 4)), (char)('a' + (value & 15)) })));
        }
    }

    private static void Install(string extensionManifest)
    {
        var origin = "chrome-extension://" + GetExtensionId(extensionManifest) + "/";
        VerifyRegistration();
        Directory.CreateDirectory(InstallDir);
        var exe = Path.Combine(InstallDir, "SiteProxyDiscovery.exe");
        File.Copy(Process.GetCurrentProcess().MainModule.FileName, exe, true);
        var manifest = new HostManifest {
            name = Name, description = "Local proxy discovery for Site Proxy Switcher",
            path = exe, type = "stdio", allowed_origins = new[] { origin }
        };
        File.WriteAllText(ManifestPath, Json.Serialize(manifest), new UTF8Encoding(false));
        using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath)) key.SetValue("", ManifestPath, RegistryValueKind.String);
        Console.WriteLine("Registered under the current Windows account.");
        Console.WriteLine("Extension: " + origin);
    }

    private static void VerifyRegistration()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath)) {
            if (key != null && !String.Equals(key.GetValue("") as string, ManifestPath, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException("The native host key belongs to another installation.");
            }
        }
    }

    private static void Unregister()
    {
        VerifyRegistration();
        Registry.CurrentUser.DeleteSubKey(RegistryPath, false);
        Console.WriteLine("Discovery registration removed.");
    }

    private sealed class HostManifest
    {
        public string name { get; set; }
        public string description { get; set; }
        public string path { get; set; }
        public string type { get; set; }
        public string[] allowed_origins { get; set; }
    }
}
