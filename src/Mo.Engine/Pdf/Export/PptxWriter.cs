using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace Mo.Engine.Pdf.Export;

/// <summary>A rendered PDF page for a slide.</summary>
public sealed record SlideImage(byte[] Png, double WidthPt, double HeightPt);

/// <summary>
/// PowerPoint file with one slide per PDF page, each showing the page as a picture — the layout stays exactly
/// as in the PDF. The slide size follows the first page.
/// </summary>
public static class PptxWriter
{
    private const long EmuPerPoint = 12700;
    private const string Ns =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string EmptyTree =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>" +
        "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";

    public static void Write(IReadOnlyList<SlideImage> pages, string path, string? title)
    {
        if (pages.Count == 0) throw new ArgumentException("No pages.", nameof(pages));
        // PowerPoint accepts slide sides between 1 and 56 inches.
        var slideCx = Math.Clamp((long)(pages[0].WidthPt * EmuPerPoint), 914400L, 51206400L);
        var slideCy = Math.Clamp((long)(pages[0].HeightPt * EmuPerPoint), 914400L, 51206400L);

        using var document = PresentationDocument.Create(path, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        var presentation = document.AddPresentationPart();

        var master = presentation.AddNewPart<SlideMasterPart>("rIdMaster");
        var layout = master.AddNewPart<SlideLayoutPart>("rIdLayout");
        layout.AddPart(master, "rIdMaster");
        var theme = master.AddNewPart<ThemePart>("rIdTheme");
        presentation.AddPart(theme, "rIdTheme");
        Feed(theme, Theme);
        Feed(layout, $"<p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\"><p:spTree>{EmptyTree}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        Feed(master,
            $"<p:sldMaster {Ns}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>{EmptyTree}</p:spTree></p:cSld>" +
            "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rIdLayout\"/></p:sldLayoutIdLst></p:sldMaster>");

        var slideIds = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            var id = $"rIdSlide{i + 1}";
            var slide = presentation.AddNewPart<SlidePart>(id);
            slide.AddPart(layout, "rIdLayout");
            var image = slide.AddImagePart(ImagePartType.Png, "rIdImage");
            using (var stream = new MemoryStream(pages[i].Png)) image.FeedData(stream);

            // Fit the page into the slide, keeping its proportions, centred.
            var scale = Math.Min(slideCx / (pages[i].WidthPt * EmuPerPoint), slideCy / (pages[i].HeightPt * EmuPerPoint));
            var cx = (long)(pages[i].WidthPt * EmuPerPoint * scale);
            var cy = (long)(pages[i].HeightPt * EmuPerPoint * scale);
            var x = (slideCx - cx) / 2;
            var y = (slideCy - cy) / 2;
            Feed(slide,
                $"<p:sld {Ns}><p:cSld><p:spTree>{EmptyTree}" +
                $"<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Page {i + 1}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                "<p:blipFill><a:blip r:embed=\"rIdImage\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            slideIds.Append($"<p:sldId id=\"{256 + i}\" r:id=\"{id}\"/>");
        }

        Feed(presentation,
            $"<p:presentation {Ns} saveSubsetFonts=\"1\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rIdMaster\"/></p:sldMasterIdLst>" +
            $"<p:sldIdLst>{slideIds}</p:sldIdLst><p:sldSz cx=\"{slideCx}\" cy=\"{slideCy}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");

        if (title is not null) document.PackageProperties.Title = title;
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml));
        part.FeedData(stream);
    }

    private const string Theme =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Mo Converter\"><a:themeElements>" +
        "<a:clrScheme name=\"Mo\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"1B1D26\"/></a:dk2><a:lt2><a:srgbClr val=\"E9EBF2\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4B46D8\"/></a:accent1><a:accent2><a:srgbClr val=\"E97132\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"196B24\"/></a:accent3><a:accent4><a:srgbClr val=\"0F9ED5\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"A02B93\"/></a:accent5><a:accent6><a:srgbClr val=\"4EA72E\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"467886\"/></a:hlink><a:folHlink><a:srgbClr val=\"96607D\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"Mo\"><a:majorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"Mo\">" +
        "<a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
        "<a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst>" +
        "</a:fmtScheme></a:themeElements></a:theme>";
}
