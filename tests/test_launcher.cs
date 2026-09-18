using System;
using System.IO;
using System.Linq;
using AntigravityProxy;

class LauncherTests {
    static int checks;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    static void Fails(Action action) {
        bool failed = false;
        try { action(); } catch { failed = true; }
        Check(failed, "Expected refusal");
    }
    static void Pe(string path, ushort machine) {
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write((ushort)0x5a4d); w.BaseStream.Position = 0x3c; w.Write(0x40);
            w.Write((uint)0x4550); w.Write(machine);
        }
    }
    static void Main(string[] args) {
        string area = args[0], root = Path.Combine(area, "package"), target = Path.Combine(area, "app");
        Directory.CreateDirectory(Path.Combine(root, "ide")); Directory.CreateDirectory(target);
        string exe = Path.Combine(target, "Antigravity.exe"), dll = Path.Combine(root, "ide", "version.dll");
        string config = Path.Combine(root, "ide", "config.json"), deployed = Path.Combine(target, "config.json");
        Pe(exe, 0x8664); Pe(dll, 0x8664);
        File.WriteAllText(Path.Combine(root, "version.dll.sha256"), Launcher.Hash(dll));
        File.WriteAllText(config, "{\"proxy\":{\"port\":7890}}");
        Fails(() => Launcher.Repair(root, exe, p => true));
        Check(!File.Exists(deployed), "Running application must not be changed");
        File.AppendAllText(dll, "corrupt");
        Fails(() => Launcher.Repair(root, exe, p => false));
        Check(!File.Exists(deployed), "Corrupt DLL must not partially deploy config");
        Pe(dll, 0x8664);
        Pe(exe, 0x14c); Fails(() => Launcher.Repair(root, exe, p => false)); Pe(exe, 0x8664);
        File.WriteAllText(config, "invalid"); Fails(() => Launcher.Repair(root, exe, p => false));
        File.WriteAllText(config, "{\"proxy\":{\"port\":7890}}");
        Launcher.Repair(root, exe, p => false);
        Check(Launcher.Hash(dll) == Launcher.Hash(Path.Combine(target, "version.dll")), "DLL restoration");
        Check(File.ReadAllText(deployed) == File.ReadAllText(config), "Config restoration");
        File.WriteAllText(deployed, "custom config");
        File.WriteAllText(Path.Combine(target, "version.dll"), "different installed DLL");
        Launcher.Repair(root, exe, p => true);
        Check(File.ReadAllText(deployed) == "custom config", "Preserve user config");
        Check(File.ReadAllText(Path.Combine(target, "version.dll")) == "different installed DLL", "Preserve installed DLL");
        File.Delete(Path.Combine(target, "version.dll"));
        Launcher.Repair(root, exe, p => false);
        Check(File.ReadAllText(deployed) == "custom config", "DLL-only repair preserves config");
        File.Delete(deployed);
        Launcher.Repair(root, exe, p => false);
        Check(File.ReadAllText(deployed) == File.ReadAllText(config), "Config-only recovery");
        File.Delete(deployed);
        int calls = 0;
        Fails(() => Launcher.Repair(root, exe, p => ++calls > 1));
        Check(!File.Exists(deployed), "Refuse process-start race before publication");
        Launcher.Repair(root, exe, p => false);
        Fails(() => Launcher.Repair(target, exe, p => false));
        Fails(() => Launcher.Repair(Path.Combine(target, "nested"), exe, p => false));
        Check(!Directory.GetFiles(target, "*.tmp").Any(), "No temporary files left");
        Fails(() => Launcher.Repair(root, Path.Combine(target, "missing.exe"), p => false));
        File.Delete(deployed);
        File.Move(config, config + ".backup");
        Fails(() => Launcher.Repair(root, exe, p => false));
        Check(!File.Exists(deployed), "Missing recovery source must not deploy");
        File.Move(config + ".backup", config);
        calls = 0;
        Fails(() => Launcher.Repair(root, exe, p => {
            if (++calls == 2) File.WriteAllText(deployed, "concurrent writer");
            return false;
        }));
        Check(File.ReadAllText(deployed) == "concurrent writer", "Never overwrite a concurrent writer");
        Check(!Directory.GetFiles(target, "*.tmp").Any(), "Cleanup after move failure");
        File.Delete(deployed);
        Directory.CreateDirectory(deployed);
        Fails(() => Launcher.Repair(root, exe, p => false));
        Check(!Directory.GetFiles(target, "*.tmp").Any(), "Cleanup after directory collision");
        Directory.Delete(deployed);
        File.Delete(Path.Combine(target, "version.dll"));
        File.WriteAllText(exe, "not a PE");
        Fails(() => Launcher.Repair(root, exe, p => false));
        Check(!File.Exists(deployed), "Bad executable rejected before writing");
        Pe(exe, 0x14c); Pe(dll, 0x14c);
        File.WriteAllText(Path.Combine(root, "version.dll.sha256"), Launcher.Hash(dll));
        Launcher.Repair(root, exe, p => false);
        Check(Launcher.Machine(Path.Combine(target, "version.dll")) == 0x14c, "Matching x86 repair");
        string detect = Path.Combine(area, "detect");
        Directory.CreateDirectory(Path.Combine(detect, "Antigravity"));
        File.WriteAllText(Path.Combine(detect, "Antigravity", "Antigravity.exe"), "fixture");
        Check(Launcher.FindTargets(new[] { detect, detect }).Count == 1, "Deduplicate installations");
        Directory.CreateDirectory(Path.Combine(detect, "Antigravity IDE"));
        File.WriteAllText(Path.Combine(detect, "Antigravity IDE", "Antigravity IDE.exe"), "fixture");
        Check(Launcher.FindTargets(new[] { detect }).Count == 2, "Detect both editions");
        Check(Launcher.FindTargets(new[] { Path.Combine(area, "absent") }).Count == 0, "Missing installation");
        Console.WriteLine("Launcher core: " + checks + " checks passed.");
    }
}
