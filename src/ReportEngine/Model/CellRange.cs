namespace ReportEngine.Model;

public readonly record struct CellRange(CellAddress First, CellAddress Last)
{
    public bool Contains(CellAddress address) =>
        address.Row >= First.Row && address.Row <= Last.Row &&
        address.Column >= First.Column && address.Column <= Last.Column;
}
