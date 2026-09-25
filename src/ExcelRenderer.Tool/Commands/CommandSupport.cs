using System.CommandLine;

namespace ExcelRenderer.Tool.Commands;

/// <summary>
/// 各変換コマンドで共通して使用する引数、オプション、および終了コード処理を提供します。
/// </summary>
internal static class CommandSupport
{
    /// <summary>
    /// 変換元となる Excel ファイルのパスを受け取る位置引数を作成します。
    /// </summary>
    /// <returns><c>input</c> という名前を持つ必須の文字列引数。</returns>
    internal static Argument<string> InputArgument() => new("input")
    {
        Description = "Path to the input .xlsx file.",
    };

    /// <summary>
    /// 変換結果の出力先を受け取る必須オプションを作成します。
    /// </summary>
    /// <param name="description">ヘルプに表示する出力先の説明。</param>
    /// <returns><c>--output</c> および <c>-o</c> で指定できる必須の文字列オプション。</returns>
    internal static Option<string> OutputOption(string description)
    {
        var option = new Option<string>("--output") { Description = description, Required = true };
        option.Aliases.Add("-o");
        return option;
    }

    /// <summary>
    /// 変換処理を実行し、キャンセルまたは例外を標準エラーへ通知して終了コードへ変換します。
    /// </summary>
    /// <param name="conversion">実行する非同期の変換処理。</param>
    /// <returns>正常に完了した場合は <c>0</c>、キャンセルまたは例外が発生した場合は <c>1</c>。</returns>
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
