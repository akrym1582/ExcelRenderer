using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;

namespace ExcelRenderer.OpenXml;

/// <summary>Checks relationship references before editing and before publishing a staged package.</summary>
internal static class WorkbookRelationships
{
    /// <summary>Validates relationship identifiers in every XML part without loading worksheet DOMs.</summary>
    /// <param name="document">Source or staged workbook.</param>
    /// <param name="token">Cooperative cancellation.</param>
    internal static void Validate(SpreadsheetDocument document, CancellationToken token)
    {
        var pending = new Stack<OpenXmlPart>(document.Parts.Select(pair => pair.OpenXmlPart));
        var visited = new HashSet<Uri>();
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var part = pending.Pop();
            if (!visited.Add(part.Uri))
            {
                continue;
            }

            var ids = new HashSet<string>(part.Parts.Select(pair => pair.RelationshipId).Concat(part.ExternalRelationships.Select(item => item.Id)).Concat(part.HyperlinkRelationships.Select(item => item.Id)).Concat(part.DataPartReferenceRelationships.Select(item => item.Id)), StringComparer.Ordinal);
            foreach (var child in part.Parts)
            {
                pending.Push(child.OpenXmlPart);
            }

            if (!part.ContentType.EndsWith("xml", StringComparison.OrdinalIgnoreCase) && part is not VmlDrawingPart)
            {
                continue;
            }

            using var input = part.GetStream(FileMode.Open, FileAccess.Read);
            using var xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var drawingIds = new HashSet<string>(StringComparer.Ordinal);
            var commentCells = new HashSet<string>(StringComparer.Ordinal);
            while (xml.Read())
            {
                token.ThrowIfCancellationRequested();
                if (xml.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if ((xml.LocalName == "cNvPr" || (part is VmlDrawingPart && xml.LocalName == "shape")) && xml.GetAttribute("id") is { } drawingId && !drawingIds.Add(drawingId))
                {
                    throw new InvalidDataException("Duplicate drawing identifier in " + part.Uri + ".");
                }

                if (part is WorksheetCommentsPart && xml.LocalName == "comment" && xml.GetAttribute("ref") is { } cell && !commentCells.Add(cell))
                {
                    throw new InvalidDataException("Duplicate comment reference in " + part.Uri + ".");
                }

                while (xml.MoveToNextAttribute())
                {
                    if (xml.NamespaceURI == "http://schemas.openxmlformats.org/officeDocument/2006/relationships" && !ids.Contains(xml.Value))
                    {
                        throw new InvalidDataException("Missing relationship '" + xml.Value + "' in " + part.Uri + ".");
                    }
                }

                xml.MoveToElement();
            }
        }
    }
}
