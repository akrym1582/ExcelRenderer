using System.CommandLine;
using ExcelRenderer.Tool.Commands;

var root = new RootCommand("Convert Excel files to PDF, PostScript, PNG, SVG and Markdown.");
root.Subcommands.Add(XlsxCommand.Create());
root.Subcommands.Add(PdfCommand.Create());
root.Subcommands.Add(PostScriptCommand.Create());
root.Subcommands.Add(ImageCommand.Create());
root.Subcommands.Add(SvgCommand.Create());
root.Subcommands.Add(MarkdownCommand.Create());
root.Subcommands.Add(RenderCommand.Create());
return await root.Parse(args).InvokeAsync();
