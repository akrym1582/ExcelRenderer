using ReportEngine.Abstractions;
using ReportEngine.Model;

namespace ReportEngine.Layout;

public sealed record ColumnLayout(int Column, double X, double Width);
