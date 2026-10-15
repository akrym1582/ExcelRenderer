using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>Identifies the exact uncommitted source snapshot used for validation.</summary>
internal static class Evidence
{
    /// <summary>Captures commit, source hash, platform, SDK and culture without creating a commit.</summary>
    /// <returns>Serializable validation provenance.</returns>
    internal static object Capture()
    {
        var root = Command("git", "rev-parse", "--show-toplevel");
        var directories = new[] { "src/ExcelRenderer.OpenXml", "tests/ExcelRenderer.OpenXml.Tests", "tools/ExcelRenderer.OpenXml.Validation" };
        var files = directories.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
            .Where(p => !Path.GetRelativePath(root, p).Split(Path.DirectorySeparatorChar).Any(c => c == "bin" || c == "obj"))
            .Concat(new[] { "Directory.Build.props", "ExcelRenderer.slnx", "scripts/check-core-dependencies.py" }.Select(p => Path.Combine(root, p)))
            .OrderBy(p => Path.GetRelativePath(root, p), StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/') + "\0"));
            hash.AppendData(File.ReadAllBytes(path));
            hash.AppendData(new byte[] { 0 });
        }

        return new
        {
            ImplementationBaseSha = Command("git", "rev-parse", "HEAD"),
            WorkingTreeChanges = Command("git", "status", "--porcelain").Length != 0,
            ValidationSourceSha256 = Convert.ToHexString(hash.GetHashAndReset()),
            OS = RuntimeInformation.OSDescription,
            Runtime = RuntimeInformation.FrameworkDescription,
            Sdk = Command("dotnet", "--version"),
            Culture = CultureInfo.CurrentCulture.Name,
        };
    }

    private static string Command(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var text = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Failed to capture validation provenance.");
        }

        return text;
    }
}
