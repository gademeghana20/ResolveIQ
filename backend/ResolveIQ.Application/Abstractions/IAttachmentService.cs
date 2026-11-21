using ResolveIQ.Application.DTOs.Attachments;

namespace ResolveIQ.Application.Abstractions
{
    public interface IAttachmentService
    {
        /// <summary>The limits and the allowed kinds of file, and whether uploads are on at all.</summary>
        AttachmentSettingsResponse GetSettings();

        /// <summary>
        /// An incident's files, oldest first, or null if the incident does not exist or the user may not see it.
        /// </summary>
        Task<IReadOnlyList<AttachmentResponse>?> ListAsync(
            Guid incidentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches a file to an incident, or to one of its comments when commentId is given (only that comment's author
        /// may do that). Anyone who can see the incident may attach, except on a closed incident or when uploads are off.
        /// </summary>
        Task<AttachmentUploadResult> UploadAsync(
            Guid incidentId,
            Guid? commentId,
            string fileName,
            byte[] content,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>The file itself, or null if it does not exist or its incident is not visible to the user.</summary>
        Task<AttachmentFile?> DownloadAsync(
            Guid attachmentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a file. Allowed for the person who attached it and for Managers and Admins, never on a closed incident.
        /// </summary>
        Task<AttachmentRemoveResult> RemoveAsync(
            Guid attachmentId,
            string userId,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);
    }
}
