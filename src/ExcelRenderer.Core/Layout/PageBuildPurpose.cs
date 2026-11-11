namespace ExcelRenderer.Core.Layout;

/// <summary>Defines the payload required by one page build.</summary>
internal enum PageBuildPurpose
{
    /// <summary>Builds all content for rendering, trimming or diagnostics.</summary>
    Render,

    /// <summary>Builds only Core cell text for font collection.</summary>
    FontCollection,

    /// <summary>Collects product text while retaining source regions and object metadata.</summary>
    Preflight,
}
