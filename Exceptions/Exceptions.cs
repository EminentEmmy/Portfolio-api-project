namespace PortfolioApi.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string detail) : base(detail) { }
}

public class ConflictException : Exception
{
    public string Title { get; }

    public ConflictException(string title, string detail) : base(detail)
    {
        Title = title;
    }
}

public class RequestValidationException : Exception
{
    public IDictionary<string, string[]> Errors { get; }

    public RequestValidationException(IDictionary<string, string[]> errors)
        : base("Validation failed") => Errors = errors;

    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = new[] { message } }) { }
}