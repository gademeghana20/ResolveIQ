using System.Text;
using ResolveIQ.Domain.Attachments;
using ResolveIQ.Infrastructure.Services;

namespace ResolveIQ.Tests.Validation
{
    /// <summary>Which files may be attached, judged by their name AND their first bytes (never by what the sender claims).</summary>
    public class AttachmentRulesTests
    {
        public static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
        public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0 };
        public static readonly byte[] Gif = Encoding.ASCII.GetBytes("GIF89a....");
        public static readonly byte[] Webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");
        public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj");
        public static readonly byte[] Text = Encoding.UTF8.GetBytes("Steps to reproduce:\r\n1. open the report\r\n2. crash\tÅ");

        [Theory]
        [InlineData("screenshot.png")]
        [InlineData("SCREENSHOT.PNG")]
        [InlineData("photo.jpg")]
        [InlineData("photo.jpeg")]
        [InlineData("anim.gif")]
        [InlineData("pic.webp")]
        [InlineData("report.pdf")]
        [InlineData("notes.txt")]
        public void EachAllowedKind_IsAcceptedWhenItsBytesMatch(string name)
        {
            var bytes = Path.GetExtension(name).ToLowerInvariant() switch
            {
                ".png" => Png,
                ".jpg" or ".jpeg" => Jpeg,
                ".gif" => Gif,
                ".webp" => Webp,
                ".pdf" => Pdf,
                _ => Text
            };

            Assert.Null(AttachmentRules.Check(name, bytes));
        }

        [Theory]
        [InlineData("program.exe")]
        [InlineData("script.js")]
        [InlineData("page.html")]
        [InlineData("picture.svg")]
        [InlineData("macro.docm")]
        [InlineData("archive.zip")]
        [InlineData("no-extension")]
        [InlineData("double.png.exe")]
        public void AnythingNotOnTheList_IsRefused_WhateverItsBytesAre(string name)
        {
            var problem = AttachmentRules.Check(name, Png);

            Assert.NotNull(problem);
            Assert.Contains("not allowed", problem);
        }

        [Fact]
        public void ADisguisedFile_IsRefused_BecauseTheBytesDoNotMatchTheName()
        {
            // A script renamed to look like a picture, and a picture renamed to look like a document.
            Assert.Contains("do not match", AttachmentRules.Check("cute.png", Encoding.ASCII.GetBytes("<script>alert(1)</script>"))!);
            Assert.Contains("do not match", AttachmentRules.Check("invoice.pdf", Png)!);
            Assert.Contains("do not match", AttachmentRules.Check("photo.jpg", Png)!);
            Assert.Contains("do not match", AttachmentRules.Check("pic.webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WAVEfmt "))!);
        }

        [Fact]
        public void ATextFile_WithBinaryContent_IsRefused()
        {
            Assert.Contains("do not match", AttachmentRules.Check("notes.txt", new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 })!);
            Assert.Contains("do not match", AttachmentRules.Check("notes.txt", Png)!);
        }

        [Fact]
        public void AnEmptyFile_IsRefused()
        {
            Assert.Equal("The file is empty.", AttachmentRules.Check("notes.txt", Array.Empty<byte>()));
        }

        [Theory]
        [InlineData("report.pdf", "report.pdf")]
        [InlineData(@"C:\Users\me\Desktop\report.pdf", "report.pdf")]
        [InlineData("../../etc/passwd.txt", "passwd.txt")]
        [InlineData("..\\..\\evil<name>.png", "evilname.png")]
        [InlineData("what?.png", "what.png")]
        [InlineData("  spaced  .txt", "spaced  .txt")]
        [InlineData("trailing dots...", "trailing dots")]
        [InlineData("", "file")]
        [InlineData(null, "file")]
        [InlineData("???", "file")]
        public void FileNames_AreCleanedBeforeTheyAreStoredOrShown(string? given, string expected)
        {
            Assert.Equal(expected, AttachmentRules.CleanFileName(given));
        }

        [Fact]
        public void ControlCharacters_AreRemovedFromNames()
        {
            Assert.Equal("ab.png", AttachmentRules.CleanFileName("a\r\nb\0.png"));
        }

        [Fact]
        public void ALongName_IsShortened_ButKeepsItsExtension()
        {
            var cleaned = AttachmentRules.CleanFileName(new string('a', 400) + ".pdf");

            Assert.Equal(AttachmentRules.MaxFileNameLength, cleaned.Length);
            Assert.EndsWith(".pdf", cleaned);
        }

        [Fact]
        public void TheContentType_ComesFromTheExtension_NotFromTheSender()
        {
            Assert.Equal("image/png", AttachmentRules.ContentTypeFor("x.PNG"));
            Assert.Equal("application/pdf", AttachmentRules.ContentTypeFor("x.pdf"));
            Assert.Null(AttachmentRules.ContentTypeFor("x.exe"));
        }
    }

    /// <summary>When uploads are on: switched on by default, off by default on a public demo, and always overridable.</summary>
    public class AttachmentSettingsTests
    {
        [Theory]
        [InlineData(null, false, true)]    // a normal site: on
        [InlineData(null, true, false)]    // a public demo site: off, so strangers cannot put files on it
        [InlineData(true, true, true)]     // decided explicitly: on, even on a demo
        [InlineData(false, false, false)]  // decided explicitly: off
        public void WhetherUploadsAreOn(bool? setting, bool demoProtected, bool expected)
        {
            var settings = new AttachmentSettings { Enabled = setting };

            Assert.Equal(expected, settings.IsEnabled(new DemoSettings { ProtectAccounts = demoProtected }));
        }

        [Theory]
        [InlineData(5L * 1024 * 1024, 5L * 1024 * 1024)]
        [InlineData(500L * 1024 * 1024, AttachmentSettings.HardMaxFileBytes)]   // never above the hard ceiling
        [InlineData(1, 1024)]                                                   // never silly small
        public void TheSizeLimit_IsKeptSensible(long configured, long expected)
        {
            Assert.Equal(expected, new AttachmentSettings { MaxFileBytes = configured }.EffectiveMaxFileBytes);
        }
    }
}
