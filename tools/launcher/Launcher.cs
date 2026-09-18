using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AntigravityProxy {
    internal static class Launcher {
        internal static string Hash(string path) {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        internal static ushort Machine(string path) {
            using (var reader = new BinaryReader(File.OpenRead(path))) {
                if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Not a PE file: " + path);
                reader.BaseStream.Position = 0x3c;
                int offset = reader.ReadInt32();
                if (offset < 0x40 || offset > reader.BaseStream.Length - 6)
                    throw new InvalidDataException("Invalid PE header: " + path);
                reader.BaseStream.Position = offset;
                if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Invalid PE signature: " + path);
                return reader.ReadUInt16();
            }
        }

        internal static string EscapeArgument(string value) {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        internal static List<string> FindTargets(IEnumerable<string> roots) {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots.Where(p => !String.IsNullOrWhiteSpace(p)))
                foreach (string folder in new[] { "Antigravity", "Antigravity IDE" })
                    foreach (string file in new[] { "Antigravity.exe", "Antigravity IDE.exe" }) {
                        string path = Path.GetFullPath(Path.Combine(root, folder, file));
                        if (File.Exists(path)) found.Add(path);
                    }
            return found.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Conservatively refuse repairs when a same-name process cannot be inspected.
        internal static bool IsRunning(string exe) {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))) {
                using (process) {
                    try {
                        if (String.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
                    } catch (InvalidOperationException) { /* process exited */ }
                    catch (System.ComponentModel.Win32Exception) { return true; }
                }
            }
            return false;
        }

        internal static void Repair(string root, string exe, Func<string, bool> running) {
            exe = Path.GetFullPath(exe);
            if (!File.Exists(exe)) throw new FileNotFoundException("Application not found", exe);
            string target = Path.GetDirectoryName(exe);
            string source = Path.Combine(root, "ide");
            string rootPrefix = target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).Equals(target, StringComparison.OrdinalIgnoreCase)
                || Path.GetFullPath(root).StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Keep the extracted launcher package outside the application installation directory.");
            string[] names = { "config.json", "version.dll" };
            string[] missing = names.Where(n => !File.Exists(Path.Combine(target, n))).ToArray();
            if (missing.Length == 0) return;
            if (running(exe)) throw new IOException("Save your work and fully exit Antigravity before repairing missing proxy files.");

            // Validate every required source before writing anything. Config remains user-editable.
            foreach (string name in missing) {
                string path = Path.Combine(source, name);
                if (name == "version.dll") {
                    string expected = File.ReadAllText(Path.Combine(root, "version.dll.sha256")).Trim();
                    if (!String.Equals(Hash(path), expected, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Bundled DLL checksum mismatch. Extract a clean release package.");
                    ushort machine = Machine(exe);
                    if ((machine != 0x8664 && machine != 0x14c) || machine != Machine(path))
                        throw new InvalidDataException("Application/DLL architecture mismatch. Use the matching x64 or x86 package.");
                } else {
                    var config = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
                    if (config == null || !config.ContainsKey("proxy"))
                        throw new InvalidDataException("The recovery config must be a JSON object containing proxy settings.");
                }
            }
            foreach (string name in missing) {
                string destination = Path.Combine(target, name);
                string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try {
                    File.Copy(Path.Combine(source, name), temp);
                    if (Hash(temp) != Hash(Path.Combine(source, name))) throw new IOException("Copy verification failed: " + name);
                    if (running(exe)) throw new IOException("Antigravity started during repair. Exit it and retry.");
                    // Same-directory move publishes a complete file and refuses to overwrite races.
                    File.Move(temp, destination);
                } finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }

        [STAThread]
        internal static int Main(string[] args) {
            bool quiet = args.TakeWhile(a => a != "--").Contains("--quiet");
            try {
                string root = AppDomain.CurrentDomain.BaseDirectory;
                string exe = null;
                bool repairOnly = false;
                var forwarded = new List<string>();
                for (int i = 0; i < args.Length; i++) {
                    if (args[i] == "--") { forwarded.AddRange(args.Skip(i + 1)); break; }
                    if (args[i] == "--target" && i + 1 < args.Length) exe = Path.GetFullPath(args[++i]);
                    else if (args[i] == "--repair-only") repairOnly = true;
                    else if (args[i] == "--quiet") { }
                    else throw new ArgumentException("Usage: AntigravityProxyLauncher.exe [--target <exe>] [--repair-only] [--quiet] [-- <application arguments>]");
                }
                if (exe == null) {
                    var targets = FindTargets(new[] {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                    });
                    if (targets.Count == 1) exe = targets[0];
                    else {
                        if (quiet) throw new IOException("No unique installation found. Specify --target <exe>.");
                        using (var picker = new OpenFileDialog()) {
                            picker.Title = "Select Antigravity executable (use --target in a shortcut to remember it)";
                            picker.Filter = "Antigravity|Antigravity.exe;Antigravity IDE.exe";
                            if (picker.ShowDialog() != DialogResult.OK) return 2;
                            exe = picker.FileName;
                        }
                    }
                }
                string name = Path.GetFileName(exe);
                if (!name.Equals("Antigravity.exe", StringComparison.OrdinalIgnoreCase)
                    && !name.Equals("Antigravity IDE.exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Select Antigravity.exe or Antigravity IDE.exe.");
                // Lock only during repair/start. No wait for the application lifetime.
                using (var mutex = new Mutex(false, "Local\\AntigravityProxyRepairLauncher")) {
                    bool acquired;
                    try { acquired = mutex.WaitOne(10000); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Another launcher is busy. Please retry.");
                    try {
                        Repair(root, exe, IsRunning);
                        if (!repairOnly) {
                            using (Process child = Process.Start(new ProcessStartInfo(exe) {
                                UseShellExecute = false,
                                WorkingDirectory = Environment.CurrentDirectory,
                                Arguments = String.Join(" ", forwarded.Select(EscapeArgument).ToArray())
                            })) { }
                        }
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception error) {
                if (!quiet) MessageBox.Show(error.Message, "Antigravity Proxy Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
    }
}
