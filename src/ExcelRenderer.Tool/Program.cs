using ExcelRenderer.Tool.Commands;
using System.CommandLine;

var root = new RootCommand("Convert Excel files to PDF, images and Markdown.");
root.Subcommands.Add(PdfCommand.Create());
root.Subcommands.Add(ImageCommand.Create());
root.Subcommands.Add(MarkdownCommand.Create());
return await root.Parse(args).InvokeAsync();
