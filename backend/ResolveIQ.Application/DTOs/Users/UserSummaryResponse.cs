namespace ResolveIQ.Application.DTOs.Users
{
    public class UserSummaryResponse
    {
        public string Id { get; set; } = string.Empty;

        public string? Email { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();
    }
}
