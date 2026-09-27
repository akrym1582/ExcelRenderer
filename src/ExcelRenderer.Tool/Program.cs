using System.CommandLine;
using ExcelRenderer.Tool.Commands;

var root = new RootCommand("Convert Excel files to PDF, PNG, SVG and Markdown.");
root.Subcommands.Add(PdfCommand.Create());
root.Subcommands.Add(ImageCommand.Create());
root.Subcommands.Add(SvgCommand.Create());
root.Subcommands.Add(MarkdownCommand.Create());
return await root.Parse(args).InvokeAsync();
