namespace Mo.Engine;

/// <summary>One line per feature area. A module is a static class with Register(OperationRegistry).</summary>
internal static class Modules
{
    public static void RegisterAll(OperationRegistry registry)
    {
        Images.ImageModule.Register(registry);
        Pdf.PdfModule.Register(registry);
        Media.MediaModule.Register(registry);
        Docs.DocsModule.Register(registry);
    }
}

