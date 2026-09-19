using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

internal static class CommandSupport
{
    internal static Argument<string> InputArgument() => new("input")
    {
        Description = "Path to the input .xlsx file."
    };

    internal static Option<string> OutputOption(string description)
    {
        var option = new Option<string>("--output") { Description = description, Required = true };
        option.Aliases.Add("-o");
        return option;
    }

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
