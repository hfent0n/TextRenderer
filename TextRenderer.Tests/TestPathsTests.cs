namespace TextRenderer.Tests;

public class TestPathsTests
{
    [Fact]
    public void Finds_sample_fonts()
    {
        Assert.True(File.Exists(TestPaths.Font("DejaVuSans.ttf")));
    }
}
