using OpenTelemetry;

namespace MyImapDownloader.Core.Telemetry;

public abstract class JsonLinesExporter<T>(JsonLinesFileWriter writer) : BaseExporter<T>
    where T : class
{
    public sealed override ExportResult Export(in Batch<T> batch)
    {
        try
        {
            var buffer = new JsonLinesBuffer();
            foreach (var item in batch)
            {
                Append(item, buffer);
            }

            writer.Append(buffer.WrittenSpan);
            return ExportResult.Success;
        }
        catch (Exception)
        {
            return ExportResult.Failure;
        }
    }

    protected abstract void Append(T item, JsonLinesBuffer buffer);
}
