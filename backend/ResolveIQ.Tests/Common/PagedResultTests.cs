using ResolveIQ.Application.DTOs.Common;

namespace ResolveIQ.Tests.Common
{
    public class PagedResultTests
    {
        [Theory]
        [InlineData(0, 20, 0)]    // nothing found: no pages
        [InlineData(1, 20, 1)]
        [InlineData(20, 20, 1)]   // exactly one full page
        [InlineData(21, 20, 2)]   // one over spills onto a second page
        [InlineData(95, 10, 10)]
        [InlineData(100, 100, 1)]
        public void TotalPagesRoundsUp(int totalCount, int pageSize, int expectedPages)
        {
            var result = new PagedResult<string> { TotalCount = totalCount, PageSize = pageSize };

            Assert.Equal(expectedPages, result.TotalPages);
        }

        [Fact]
        public void AZeroPageSizeDoesNotDivideByZero()
        {
            var result = new PagedResult<string> { TotalCount = 5, PageSize = 0 };

            Assert.Equal(0, result.TotalPages);
        }
    }
}
