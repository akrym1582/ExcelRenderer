using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ExcelRenderer;
using ExcelRenderer.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>Measures saved Release runtimes using identical fixtures, including staged and direct Kestrel output.</summary>
internal static class AsyncPdfBenchmark
{
    private static Dictionary<string, double> latestMetrics = new();

    /// <summary>Runs one warm-up and three measured rounds per workload and output mode.</summary>
    /// <param name="args">Command, repository root, output JSONL, and optional baseline flag.</param>
    /// <returns>The benchmark completion.</returns>
    internal static async Task RunAsync(string[] args)
    {
        var root = Path.GetFullPath(args[1]);
        var outputPath = Path.GetFullPath(args[2]);
        var baseline = args.Length > 3 && args[3] == "baseline";
        var baselineCommit = args.Length > 4 ? args[4] : "f73676c437bc991384dc04c8b19d48d6f1fbff4e";
        var font = Path.Combine(root, "third_party/NotoSansJP/NotoSansJP-Regular.ttf");
        var cases = new[] { "01-japanese.xlsx", "06-layout-and-pagination.xlsx", "02-image.xlsx", "02-image.xlsx" };
        var inputs = cases.Select(name => File.ReadAllBytes(Path.Combine(root, "tests/ExcelRenderer.Tests/SampleInputs", name))).ToArray();
        var temporary = Directory.CreateTempSubdirectory("async-pdf-benchmark-");
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.AllowSynchronousIO = false).UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        app.MapGet("/{mode}/{index:int}", async (Microsoft.AspNetCore.Http.HttpContext context, string mode, int index) =>
        {
            var metrics = new Dictionary<string, double>();
            ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
            try
            {
                context.Response.ContentType = "application/pdf";
                if (mode == "staged")
                {
                    var staged = Path.Combine(temporary.FullName, Guid.NewGuid() + ".pdf");
                    try
                    {
                        using (var file = File.Create(staged))
                        {
                            await Convert(index, file);
                        }

                        using var source = File.OpenRead(staged);
                        metrics["extraFinalFileBytes"] = source.Length;
                        await source.CopyToAsync(context.Response.Body, context.RequestAborted);
                    }
                    finally
                    {
                        File.Delete(staged);
                    }
                }
                else
                {
                    await Convert(index, context.Response.Body);
                }

                // Trailers are unnecessary for this sequential local probe; the client waits for response completion.
                latestMetrics = metrics;
            }
            finally
            {
                ConversionMetrics.Observer = null;
            }
        });
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var records = new StreamWriter(outputPath);
            await records.WriteLineAsync(JsonSerializer.Serialize(new
            {
                Kind = "environment",
                Baseline = baseline,
                Runtime = Environment.Version.ToString(),
                Commit = baseline ? baselineCommit : "feat/async-pdf-stream",
                FontSha256 = Hash(File.ReadAllBytes(font)),
                Inputs = cases.Select((name, index) => new { Name = name, Spill = index == 3, Sha256 = Hash(inputs[index]) }),
                Dlls = new[] { "ExcelRenderer.dll", "ExcelRenderer.Core.dll" }.Select(name => new { Name = name, Sha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name))) }),
            }));
            for (var round = -1; round < 3; round++)
            {
                for (var index = 0; index < cases.Length; index++)
                {
                    foreach (var mode in baseline ? new[] { "file", "staged" } : new[] { "file", "staged", "direct" })
                    {
                        latestMetrics = new();
                        var allocation = GC.GetTotalAllocatedBytes(true);
                        var timer = Stopwatch.StartNew();
                        long size;
                        if (mode == "file")
                        {
                            var metrics = new Dictionary<string, double>();
                            ConversionMetrics.Observer = (name, value) => metrics[name] = metrics.GetValueOrDefault(name) + value;
                            try
                            {
                                using var file = File.Create(Path.Combine(temporary.FullName, "output.pdf"));
                                await Convert(index, file);
                                size = file.Length;
                                latestMetrics = metrics;
                            }
                            finally
                            {
                                ConversionMetrics.Observer = null;
                            }
                        }
                        else
                        {
                            using var response = await client.GetAsync($"/{mode}/{index}");
                            response.EnsureSuccessStatusCode();
                            size = (await response.Content.ReadAsByteArrayAsync()).LongLength;
                        }

                        timer.Stop();
                        var allocated = GC.GetTotalAllocatedBytes(true) - allocation;
                        using var process = Process.GetCurrentProcess();
                        await records.WriteLineAsync(JsonSerializer.Serialize(new
                        {
                            Kind = "sample", Warmup = round < 0, Round = round, Case = cases[index], Spill = index == 3, Mode = mode,
                            ElapsedMs = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocated, OutputBytes = size,
                            FinalSaveMs = latestMetrics.GetValueOrDefault("pdfFinalSave.ms"), Spills = latestMetrics.GetValueOrDefault("buffer.spillCount"),
                            ExtraFinalFileBytes = latestMetrics.GetValueOrDefault("extraFinalFileBytes"), Threads = process.Threads.Count,
                        }));
                    }
                }
            }
        }
        finally
        {
            await app.StopAsync();
            temporary.Delete(true);
        }

        async Task Convert(int index, Stream destination)
        {
            using var input = new MemoryStream(inputs[index], false);
            await ExcelConverter.RenderAsync(
                input,
                new()
                {
                    OutputFormat = OutputFormat.Pdf,
                    FontOptions = new() { FontFiles = [font], UseFontPack = false, AllowSystemFonts = false },
                    Buffering = new() { MemoryThresholdBytes = index == 3 ? 1 : 8 * 1024 * 1024, TemporaryDirectory = temporary.FullName },
                },
                new SingleStreamOutputSink(destination));
        }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
