using ResolveIQ.Application.Abstractions;

namespace ResolveIQ.API.Security
{
    /// <summary>The signed-in person making this request, read from the "sub" claim of their token.</summary>
    public class CurrentActor : ICurrentActor
    {
        private readonly IHttpContextAccessor _http;

        public CurrentActor(IHttpContextAccessor http)
        {
            _http = http;
        }

        public string? UserId =>
            _http.HttpContext?.User.FindFirst("sub")?.Value is { Length: > 0 } id ? id : null;
    }
}
