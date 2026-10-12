namespace FimpleCaseStudy.Domain;

public enum ErrorKind { Validation, Conflict, NotFound }

public sealed class DomainException(string code, string message, ErrorKind kind = ErrorKind.Validation)
    : Exception(message)
{
    public string Code { get; } = code;
    public ErrorKind Kind { get; } = kind;
}
