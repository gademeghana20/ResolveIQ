using System.Text;

namespace ResolveIQ.Domain.Attachments
{
    /// <summary>
    /// What may be attached to an incident or comment. Pure code, so it is easy to test.
    ///
    /// A file name and a content type are only what the sender claims, so neither is trusted. A file is accepted only
    /// when its extension is on the short list below AND its first bytes really look like that kind of file. Anything
    /// that a browser could run (scripts, web pages, SVG pictures) or that is a program is not on the list.
    /// </summary>
    public static class AttachmentRules
    {
        /// <summary>The only kinds allowed: extension to the content type the app itself will use when serving it.</summary>
        public static readonly IReadOnlyDictionary<string, string> AllowedTypes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [".png"] = "image/png",
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".gif"] = "image/gif",
                [".webp"] = "image/webp",
                [".pdf"] = "application/pdf",
                [".txt"] = "text/plain",
            };

        public const int MaxFileNameLength = 150;

        public static string AllowedExtensionsText => string.Join(", ", AllowedTypes.Keys.OrderBy(k => k));

        /// <summary>
        /// A safe name to store and show: no folder parts, no control characters, no characters Windows forbids,
        /// no leading or trailing dots and spaces, and not too long (the extension is kept when it is shortened).
        /// </summary>
        public static string CleanFileName(string? name)
        {
            var text = name ?? string.Empty;

            // Browsers can send "C:\folder\file.png"; only the last part is the name.
            var slash = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
            if (slash >= 0)
            {
                text = text[(slash + 1)..];
            }

            var invalid = new HashSet<char>("<>:\"|?*");
            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (c >= ' ' && c != '\u007f' && !invalid.Contains(c))
                {
                    builder.Append(c);
                }
            }

            var cleaned = builder.ToString().Trim().Trim('.').Trim();
            if (cleaned.Length == 0)
            {
                return "file";
            }

            if (cleaned.Length > MaxFileNameLength)
            {
                var extension = Path.GetExtension(cleaned);
                var keep = Math.Max(1, MaxFileNameLength - extension.Length);
                cleaned = cleaned[..keep] + extension;
            }

            return cleaned;
        }

        public static string? ContentTypeFor(string fileName) =>
            AllowedTypes.TryGetValue(Path.GetExtension(fileName), out var type) ? type : null;

        /// <summary>
        /// Checks the file. Returns null when it is acceptable, otherwise a sentence that says why not.
        /// </summary>
        public static string? Check(string fileName, ReadOnlySpan<byte> content)
        {
            var extension = Path.GetExtension(fileName);
            if (!AllowedTypes.ContainsKey(extension))
            {
                return $"That type of file is not allowed. You can attach: {AllowedExtensionsText}.";
            }

            if (content.Length == 0)
            {
                return "The file is empty.";
            }

            var looksRight = extension.ToLowerInvariant() switch
            {
                ".png" => StartsWith(content, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
                ".jpg" or ".jpeg" => StartsWith(content, 0xFF, 0xD8, 0xFF),
                ".gif" => StartsWithText(content, 0, "GIF87a") || StartsWithText(content, 0, "GIF89a"),
                ".webp" => StartsWithText(content, 0, "RIFF") && StartsWithText(content, 8, "WEBP"),
                ".pdf" => StartsWithText(content, 0, "%PDF-"),
                ".txt" => LooksLikeText(content),
                _ => false,
            };

            return looksRight
                ? null
                : $"The file's contents do not match its \"{extension}\" name, so it was not accepted.";
        }

        private static bool StartsWith(ReadOnlySpan<byte> content, params byte[] signature) =>
            content.Length >= signature.Length && content[..signature.Length].SequenceEqual(signature);

        private static bool StartsWithText(ReadOnlySpan<byte> content, int offset, string text)
        {
            if (content.Length < offset + text.Length)
            {
                return false;
            }

            for (var i = 0; i < text.Length; i++)
            {
                if (content[offset + i] != (byte)text[i])
                {
                    return false;
                }
            }

            return true;
        }

        // Plain text has no zero bytes and no control characters other than tab, line breaks and form feed.
        private static bool LooksLikeText(ReadOnlySpan<byte> content)
        {
            var sample = content.Length > 8192 ? content[..8192] : content;
            foreach (var b in sample)
            {
                if (b == 0 || (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D && b != 0x0C))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
