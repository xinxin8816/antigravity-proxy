using System;
using System.IO;
class Mock {
    static void Main(string[] args) {
        File.WriteAllLines(Environment.GetEnvironmentVariable("LAUNCHER_TEST_CAPTURE"),
            new[] { Environment.CurrentDirectory }.ConcatValues(args));
        System.Threading.Thread.Sleep(3000);
    }
}
static class Arrays {
    internal static string[] ConcatValues(this string[] first, string[] second) {
        var result = new string[first.Length + second.Length];
        first.CopyTo(result, 0); second.CopyTo(result, first.Length); return result;
    }
}
