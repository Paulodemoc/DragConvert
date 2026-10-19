using DragConvert.Core;

namespace DragConvert.Core.Tests;

public class ProductInfoTests
{
    [Fact]
    public void Name_is_DragConvert() => Assert.Equal("DragConvert", ProductInfo.Name);
}
