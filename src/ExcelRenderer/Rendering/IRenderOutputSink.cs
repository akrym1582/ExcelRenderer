namespace ExcelRenderer.Rendering;

/// <summary><see cref="ExcelConverter.RenderAsync"/> が生成した生成物を受け取ります。</summary>
/// <remarks>
/// <see cref="OpenAsync"/> が返すストリームの所有権はシンクに残り、レンダラーは破棄しません。
/// 呼び出しは直列化されます。正常なオープンの後には、<see cref="CompleteAsync"/> または
/// <see cref="AbortAsync"/> のいずれかがちょうど一度呼び出されます。
/// </remarks>
public interface IRenderOutputSink
{
    /// <summary>生成物の内容を書き込むストリームを開きます。</summary>
    /// <param name="artifact">これから書き込む生成物の識別情報です。</param>
    /// <param name="cancellationToken">オープン処理のキャンセルを通知するトークンです。</param>
    /// <returns>生成物を書き込むストリームを返します。ストリームの所有権はシンクが保持します。</returns>
    /// <exception cref="OperationCanceledException">オープン処理がキャンセルされた場合にスローされます。</exception>
    ValueTask<Stream> OpenAsync(ArtifactDescriptor artifact, CancellationToken cancellationToken);

    /// <summary>生成物の書き込みが完了したことをシンクへ通知します。</summary>
    /// <param name="artifact">書き込みが完了した生成物の識別情報です。</param>
    /// <param name="byteLength">生成物へ書き込まれたバイト数です。</param>
    /// <param name="cancellationToken">完了処理のキャンセルを通知するトークンです。</param>
    /// <returns>完了処理が終了したときに完了する値タスクを返します。</returns>
    /// <exception cref="OperationCanceledException">完了処理がキャンセルされた場合にスローされます。</exception>
    ValueTask CompleteAsync(ArtifactDescriptor artifact, long byteLength, CancellationToken cancellationToken);

    /// <summary>生成途中の生成物を破棄し、失敗をシンクへ通知します。</summary>
    /// <param name="artifact">破棄する生成物の識別情報です。</param>
    /// <param name="error">生成を中断した例外です。</param>
    /// <param name="cancellationToken">中断処理のキャンセルを通知するトークンです。</param>
    /// <returns>中断処理が終了したときに完了する値タスクを返します。</returns>
    /// <exception cref="OperationCanceledException">中断処理がキャンセルされた場合にスローされます。</exception>
    ValueTask AbortAsync(ArtifactDescriptor artifact, Exception error, CancellationToken cancellationToken);
}
