using Microsoft.EntityFrameworkCore;
using ResolveIQ.Application.Abstractions;
using ResolveIQ.Application.DTOs.Sla;
using ResolveIQ.Domain.Constants;
using ResolveIQ.Domain.Entities;
using ResolveIQ.Domain.Enums;
using ResolveIQ.Infrastructure.Data;

namespace ResolveIQ.Infrastructure.Services
{
    public class SlaPolicyService : ISlaPolicyService
    {
        private readonly ApplicationDbContext _db;
        private readonly IAuditLog _audit;

        public SlaPolicyService(ApplicationDbContext db, IAuditLog audit)
        {
            _db = db;
            _audit = audit;
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

            var oldHours = policy.ResolutionTargetHours;
            var oldActive = policy.IsActive;

            policy.ResolutionTargetHours = request.ResolutionTargetHours;
            policy.IsActive = request.IsActive;
            await _db.SaveChangesAsync(cancellationToken);

            var changes = new List<string>();
            if (oldHours != policy.ResolutionTargetHours)
            {
                changes.Add($"Target: {oldHours} h → {policy.ResolutionTargetHours} h");
            }

            if (oldActive != policy.IsActive)
            {
                changes.Add(policy.IsActive ? "Switched on" : "Switched off");
            }

            if (changes.Count > 0)
            {
                await _audit.RecordAsync(
                    AuditActions.SlaPolicyUpdated, AuditActions.TargetSlaPolicy, policy.Priority.ToString(), $"{policy.Priority} priority",
                    string.Join("; ", changes), cancellationToken: cancellationToken);
            }

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
