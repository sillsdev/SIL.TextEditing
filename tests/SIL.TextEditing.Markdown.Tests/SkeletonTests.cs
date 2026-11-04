using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    public class SkeletonTests
    {
        [Fact]
        public void TheConverterAssemblyLoadsOnEveryTarget()
        {
            Assert.Equal("0", MarkdownConverter.Version);
            Assert.NotNull(typeof(Markdig.Markdown));
        }
    }
}
