using System.Globalization;
using System.Text.Json;

/// <summary>Runs controlled-fixture comparisons and isolated-process benchmarks.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("Expected compare, generate, benchmark or worker.");
            }

            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < args.Length; i += 2)
            {
                options.Add(args[i], args[i + 1]);
            }

            switch (args[0])
            {
                case "compare":
                    return Comparison.Run(Path.GetFullPath(options["--repository-root"]), Path.GetFullPath(options["--output"]));
                case "generate":
                    FixtureGenerator.Run(Path.GetFullPath(options["--output"]));
                    return 0;
                case "benchmark":
                    Benchmarks.Run(Path.GetFullPath(options["--input"]), Path.GetFullPath(options["--output"]));
                    return 0;
                case "worker":
                    Console.WriteLine(JsonSerializer.Serialize(Benchmarks.Measure(options["--input"], options["--reader"], options["--mode"])));
                    return 0;
                default:
                    throw new ArgumentException("Unknown command.");
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }
}
