using ResolveIQ.Application.Validation;

namespace ResolveIQ.Tests.Validation
{
    public class EmailWithDomainTests
    {
        [Theory]
        [InlineData("name@example.com")]
        [InlineData("first.last@example.com")]
        [InlineData("name+tag@example.com")]
        [InlineData("name@mail.example.com")]
        [InlineData("name@example.co.uk")]
        [InlineData("name@example.io")]
        [InlineData("n@e.co")]
        public void AFullAddressIsAccepted(string email)
        {
            Assert.True(EmailWithDomainAttribute.IsValidEmail(email));
        }

        [Theory]
        [InlineData("test1@example")]            // no ending: the case that slipped through
        [InlineData("name@localhost")]
        [InlineData("name@gmail")]
        [InlineData("name@example.")]            // dot with nothing after it
        [InlineData("name@.com")]                // nothing before the dot
        [InlineData("name@example..com")]        // an empty part in the middle
        [InlineData("name@example.c")]           // an ending of one character
        [InlineData("@example.com")]             // nobody before the @
        [InlineData("name@")]
        [InlineData("name")]
        [InlineData("na me@example.com")]        // a space
        [InlineData("name@exa mple.com")]
        [InlineData("name@@example.com")]
        [InlineData("")]
        [InlineData("   ")]
        public void AnythingElseIsRefused(string email)
        {
            Assert.False(EmailWithDomainAttribute.IsValidEmail(email));
        }

        [Fact]
        public void AMissingValueIsLeftToRequired_LikeTheBuiltInChecks()
        {
            Assert.True(new EmailWithDomainAttribute().IsValid(null));
        }

        [Fact]
        public void AValueThatIsNotTextIsRefused()
        {
            Assert.False(new EmailWithDomainAttribute().IsValid(42));
        }

        [Fact]
        public void TheMessageSaysWhatToDo()
        {
            // FormatErrorMessage is what a person actually sees when the check fails.
            Assert.Contains("name@example.com", new EmailWithDomainAttribute().FormatErrorMessage("Email"));
        }

        [Fact]
        public void ItDoesNotHangOnAHostileInput()
        {
            var hostile = new string('a', 50_000) + "@" + string.Join(".", Enumerable.Repeat("b", 5_000));

            var watch = System.Diagnostics.Stopwatch.StartNew();
            _ = EmailWithDomainAttribute.IsValidEmail(hostile);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        }
    }
}
