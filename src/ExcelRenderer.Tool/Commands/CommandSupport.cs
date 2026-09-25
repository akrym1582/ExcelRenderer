using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// CommandSupport が表すデータと操作を提供します.
/// </summary>
internal static class CommandSupport
{
    /// <summary>
    /// InputArgument を実行します.
    /// </summary>
    /// <returns>処理によって得られた結果を返します。</returns>
    internal static Argument<string> InputArgument() => new("input")
    {
        Description = "Path to the input .xlsx file.",
    };

    /// <summary>
    /// OutputOption を実行します.
    /// </summary>
    /// <param name="description">description に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    internal static Option<string> OutputOption(string description)
    {
        var option = new Option<string>("--output") { Description = description, Required = true };
        option.Aliases.Add("-o");
        return option;
    }

    /// <summary>
    /// RunAsync を実行します.
    /// </summary>
    /// <param name="conversion">conversion に渡す値です。</param>
    /// <returns>処理によって得られた結果を返します。</returns>
    internal static async Task<int> RunAsync(Func<Task> conversion)
    {
        try
        {
            await conversion().ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Conversion was cancelled.");
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
