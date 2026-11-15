using Mo.Engine.Archives;
using Mo.Engine.Spreadsheets;
using Mo.Engine.Subtitles;

namespace Mo.Engine.Docs;

/// <summary>Registers documents, spreadsheets, presentations, archives and subtitles.</summary>
public static class DocsModule
{
    public static void Register(OperationRegistry registry)
    {
        registry
            .Add(new DocumentConvertOperation("pdf", "PDF", "\uEA90", 10))
            .Add(new DocumentConvertOperation("docx", "DOCX", "\uE8A5", 20))
            .Add(new DocumentConvertOperation("odt", "ODT", "\uE8A5", 30))
            .Add(new DocumentConvertOperation("md", "MD", "\uE943", 40))
            .Add(new DocumentConvertOperation("txt", "TXT", "\uE8A5", 50))
            .Add(new DocumentConvertOperation("html", "HTML", "\uE774", 60))
            .Add(new DocumentConvertOperation("epub", "EPUB", "\uE82D", 70))
            .Add(new ToXlsxOperation())
            .Add(new ToCsvOperation())
            .Add(new OfficeConvertOperation("sheet.to-pdf", "pdf", "pdf", "PDF", "\uEA90", 30, FileCategory.Spreadsheet, "xlsx", "xls", "ods", "csv", "tsv"))
            .Add(new OfficeConvertOperation("sheet.to-ods", "ods", "ods", "ODS", "\uE80A", 40, FileCategory.Spreadsheet, "xlsx", "xls", "csv"))
            .Add(new OfficeConvertOperation("slides.to-pdf", "pdf", "pdf", "PDF", "\uEA90", 10, FileCategory.Presentation, "pptx", "ppt", "odp"))
            .Add(new SlidesToImagesOperation())
            .Add(new OfficeConvertOperation("slides.to-pptx", "pptx", "pptx", "PPTX", "\uE7F4", 30, FileCategory.Presentation, "ppt", "odp"))
            .Add(new ExtractArchiveOperation())
            .Add(new ArchiveToZipOperation())
            .Add(new SubtitleConvertOperation("srt", "SRT", 10))
            .Add(new SubtitleConvertOperation("vtt", "VTT", 20))
            .Add(new SubtitleConvertOperation("ass", "ASS", 30))
            .Add(new SubtitleConvertOperation("txt", "TXT", 40));
    }
}
