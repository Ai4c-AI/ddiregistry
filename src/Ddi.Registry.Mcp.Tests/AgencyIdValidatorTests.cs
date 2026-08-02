using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Mcp.Tests
{
    public class AgencyIdValidatorTests
    {
        [Theory]
        [InlineData("us.foo", "Foo", true)]
        [InlineData("uk.foo", "Foo", true)]
        [InlineData("int.foo", "Foo", true)]
        [InlineData("zz.foo", "Foo", false)]          // 非法 2 字符码
        [InlineData("usa.foo", "Foo", false)]          // 非 int 的 3 字符码
        [InlineData("us.", "Foo", false)]              // 缺少名称
        [InlineData("u.foo", "Foo", false)]            // 前缀太短
        [InlineData("us.foo.bar", "Foo", false)]       // 嵌套点号（正则禁止）
        [InlineData("us.foobaroverfiftytwocharacterslongggggggggggggggggggggggg", "Foo", false)] // >50
        public void Validate_ReturnsExpected(string id, string label, bool expectedOk)
            => Assert.Equal(expectedOk, AgencyIdValidator.Validate(id, label).Ok);

        [Fact] public void Validate_NullLabel_Fails()
            => Assert.False(AgencyIdValidator.Validate("us.foo", null).Ok);

        [Fact] public void Validate_UnknownTwoCharCode_Fails_WhenIsoPresent()
            => Assert.False(AgencyIdValidator.Validate("zz.foo", "Foo").Ok); // 经嵌入的 iso 数据校验
    }
}
