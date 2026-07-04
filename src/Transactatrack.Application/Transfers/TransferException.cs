namespace Transactatrack.Application.Transfers;

public class TransferException : Exception
{
    public int StatusCode { get; }
    public string Title { get; }

    public TransferException(int statusCode, string title) : base(title)
    {
        StatusCode = statusCode;
        Title = title;
    }
}
