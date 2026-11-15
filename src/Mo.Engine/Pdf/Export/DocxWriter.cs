using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Mo.Engine.Pdf.Layout;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Mo.Engine.Pdf.Export;

/// <summary>Word document: headings as real heading styles, tables as Word tables, pictures inline, a page break per PDF page.</summary>
public static class DocxWriter
{
    private const long EmuPerPoint = 12700;

    public static void Write(LayoutDocument layout, string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = document.AddMainDocumentPart();
        AddStyles(main);
        var body = new Body();
        main.Document = new Document(body);

        var first = layout.Pages.FirstOrDefault();
        var pageWidth = first?.WidthPt ?? 595;
        var pageHeight = first?.HeightPt ?? 842;
        var contentWidth = Math.Max(pageWidth - 144, 144); // one-inch margins

        var imageId = 1u;
        for (var p = 0; p < layout.Pages.Count; p++)
        {
            foreach (var block in layout.Pages[p].Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        body.Append(Paragraph(paragraph));
                        break;
                    case TableBlock table:
                        body.Append(Table(table));
                        body.Append(new Paragraph()); // Word needs something between two tables
                        break;
                    case ImageBlock image:
                        body.Append(Picture(main, image, contentWidth, imageId++));
                        break;
                }
            }
            if (p < layout.Pages.Count - 1) body.Append(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
        }

        body.Append(new SectionProperties(
            new PageSize { Width = (UInt32Value)(uint)Math.Round(pageWidth * 20), Height = (UInt32Value)(uint)Math.Round(pageHeight * 20) },
            new PageMargin { Top = 1440, Bottom = 1440, Left = 1440U, Right = 1440U, Header = 720U, Footer = 720U, Gutter = 0U }));

        if (layout.Title is { } title) document.PackageProperties.Title = title;
        main.Document.Save();
    }

    private static Paragraph Paragraph(ParagraphBlock block)
    {
        var properties = new ParagraphProperties();
        if (block.HeadingLevel > 0) properties.Append(new ParagraphStyleId { Val = "Heading" + block.HeadingLevel });
        if (block.RightToLeft) properties.Append(new BiDi());

        var runProperties = new RunProperties();
        if (block.Bold) runProperties.Append(new Bold());
        if (block.RightToLeft) runProperties.Append(new RightToLeftText());

        return new Paragraph(properties, new Run(runProperties, new Text(block.Text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table Table(TableBlock block)
    {
        var border = () => new EnumValue<BorderValues>(BorderValues.Single);
        var properties = new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                // The schema fixes the order: top, left, bottom, right, inside horizontal, inside vertical.
                new TopBorder { Val = border(), Size = 4 },
                new LeftBorder { Val = border(), Size = 4 },
                new BottomBorder { Val = border(), Size = 4 },
                new RightBorder { Val = border(), Size = 4 },
                new InsideHorizontalBorder { Val = border(), Size = 4 },
                new InsideVerticalBorder { Val = border(), Size = 4 }));
        if (block.RightToLeft) properties.Append(new BiDiVisual());

        var table = new Table(properties);
        var grid = new TableGrid();
        for (var c = 0; c < block.ColumnCount; c++) grid.Append(new GridColumn());
        table.Append(grid);

        for (var r = 0; r < block.Rows.Count; r++)
        {
            var row = new TableRow();
            foreach (var text in block.Rows[r])
            {
                var paragraphProperties = new ParagraphProperties();
                if (block.RightToLeft) paragraphProperties.Append(new BiDi());
                var runProperties = new RunProperties();
                if (r == 0) runProperties.Append(new Bold()); // the first row is usually the header
                if (block.RightToLeft) runProperties.Append(new RightToLeftText());
                row.Append(new TableCell(new Paragraph(paragraphProperties,
                    new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }))));
            }
            table.Append(row);
        }
        return table;
    }

    private static Paragraph Picture(MainDocumentPart main, ImageBlock block, double maxWidthPt, uint id)
    {
        var part = main.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(block.Png)) part.FeedData(stream);
        var relationship = main.GetIdOfPart(part);

        var scale = block.WidthPt > maxWidthPt ? maxWidthPt / block.WidthPt : 1;
        var cx = (long)(block.WidthPt * scale * EmuPerPoint);
        var cy = (long)(block.HeightPt * scale * EmuPerPoint);
        var name = $"Picture {id}";

        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = name + ".png" },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relationship },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
                            new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U,
            });
        return new Paragraph(new Run(drawing));
    }

    private static void AddStyles(MainDocumentPart main)
    {
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Arial" },
                    new FontSize { Val = "22" }, new FontSizeComplexScript { Val = "22" })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "120", Line = "276", LineRule = LineSpacingRuleValues.Auto }))),
            new Style(new StyleName { Val = "Normal" }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
            Heading(1, "36"),
            Heading(2, "28"));
        styles.Styles.Save();
    }

    private static Style Heading(int level, string halfPoints) =>
        new(
            new StyleName { Val = "heading " + level },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new KeepNext(),
                new SpacingBetweenLines { Before = level == 1 ? "360" : "240", After = "120" },
                new OutlineLevel { Val = level - 1 }),
            new StyleRunProperties(new Bold(), new FontSize { Val = halfPoints }, new FontSizeComplexScript { Val = halfPoints }))
        { Type = StyleValues.Paragraph, StyleId = "Heading" + level };
}
