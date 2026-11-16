using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Sla;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class SlaPolicyService : ISlaPolicyService
    {
        private readonly ApplicationDbContext _db;

        public SlaPolicyService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<SlaPolicyResponse>> GetPoliciesAsync(
            CancellationToken cancellationToken = default)
        {
            var policies = await _db.SlaPolicies.AsNoTracking().ToListAsync(cancellationToken);

            return policies
                .OrderByDescending(p => p.Priority)
                .Select(ToResponse)
                .ToList();
        }

        public async Task<SlaPolicyResponse?> UpdatePolicyAsync(
            IncidentPriority priority,
            UpdateSlaPolicyRequest request,
            CancellationToken cancellationToken = default)
        {
            var policy = await _db.SlaPolicies.FirstOrDefaultAsync(p => p.Priority == priority, cancellationToken);
            if (policy is null)
            {
                return null;
            }

            policy.ResolutionTargetHours = request.ResolutionTargetHours;
            policy.IsActive = request.IsActive;
            await _db.SaveChangesAsync(cancellationToken);

            return ToResponse(policy);
        }

        private static SlaPolicyResponse ToResponse(SlaPolicy p) => new()
        {
            Priority = p.Priority,
            ResolutionTargetHours = p.ResolutionTargetHours,
            IsActive = p.IsActive
        };
    }
}
