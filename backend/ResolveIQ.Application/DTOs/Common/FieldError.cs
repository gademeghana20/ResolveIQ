namespace ResolveIQ.Application.DTOs.Common
{
    /// <summary>One thing wrong with one field of a request, for example Field "Email", Message "Already in use".</summary>
    public record FieldError(string Field, string Message);
}
