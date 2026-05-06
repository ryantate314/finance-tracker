namespace Transactatrack.Application.Imports;

public class ImportException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }

    public ImportException(int statusCode, string title) : base(title)
    {
        StatusCode = statusCode;
        Title = title;
    }
}
