using System.Xml;

namespace EplanMcp.Host.Importing;

/// <summary>XML safety only: S34 has no verified mapping from file records to complete device names (tool spec 5).</summary>
internal static class PlcFilePreflight
{
    public const long MaxBytes = 100L * 1024 * 1024;

    public static async Task ValidateAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes });
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // These are XML inclusion/schema mechanisms, not guesses about PBF or AML device records.
            if ((reader.NamespaceURI == "http://www.w3.org/2001/XInclude" && reader.LocalName == "include")
                || reader.GetAttribute("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance") is not null
                || reader.GetAttribute("noNamespaceSchemaLocation", "http://www.w3.org/2001/XMLSchema-instance") is not null)
            {
                throw new XmlException("External XML references are not allowed.");
            }
        }
    }
}
