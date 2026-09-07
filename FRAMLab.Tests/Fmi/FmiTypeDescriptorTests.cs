using Xunit;
using FRAMLab.Shared;

namespace FRAMLab.Tests.Fmi
{
    public class FmiTypeDescriptorTests
    {
        [Theory]
        [InlineData("number", FmiValueType.Number)]
        [InlineData("Number", FmiValueType.Number)]
        [InlineData(" string ", FmiValueType.String)]
        [InlineData("bool", FmiValueType.Boolean)]
        [InlineData("boolean", FmiValueType.Boolean)] //alias, accepted on parse
        [InlineData("Boolean", FmiValueType.Boolean)]
        [InlineData("list", FmiValueType.List)]
        [InlineData("dictionary", FmiValueType.Dictionary)]
        public void TryParse_CanonicalStrings(string input, FmiValueType expected)
        {
            Assert.True(FmiTypeDescriptor.TryParse(input, out var descriptor, out var error));
            Assert.Equal(expected, descriptor!.Kind);
            Assert.Null(error);
        }

        [Theory]
        [InlineData("array<number>", FmiValueType.Number)]
        [InlineData("array< number >", FmiValueType.Number)] //whitespace inside brackets
        [InlineData("ARRAY<STRING>", FmiValueType.String)]
        [InlineData("array<boolean>", FmiValueType.Boolean)]
        public void TryParse_Arrays(string input, FmiValueType elementKind)
        {
            Assert.True(FmiTypeDescriptor.TryParse(input, out var descriptor, out _));
            Assert.Equal(FmiValueType.Array, descriptor!.Kind);
            Assert.Equal(elementKind, descriptor.ElementType!.Kind);
        }

        [Fact]
        public void TryParse_NestedArray_RoundTrips()
        {
            Assert.True(FmiTypeDescriptor.TryParse("array<array<string>>", out var descriptor, out _));
            Assert.Equal(FmiValueType.Array, descriptor!.Kind);
            Assert.Equal(FmiValueType.Array, descriptor.ElementType!.Kind);
            Assert.Equal(FmiValueType.String, descriptor.ElementType.ElementType!.Kind);
            Assert.Equal("array<array<string>>", descriptor.ToString());
        }

        [Fact]
        public void ToString_EmitsCanonicalLowercase_BoolIsCanonical()
        {
            //Decision: parse both "bool"/"boolean", always emit "bool"
            Assert.Equal("bool", FmiTypeDescriptor.Boolean.ToString());
            Assert.Equal("number", FmiTypeDescriptor.Number.ToString());
            Assert.Equal("string", FmiTypeDescriptor.String.ToString());
            Assert.True(FmiTypeDescriptor.TryParse("boolean", out var descriptor, out _));
            Assert.Equal("bool", descriptor!.ToString());
        }

        [Theory]
        [InlineData("array<")]
        [InlineData("table")]
        [InlineData("")]
        [InlineData("array<bogus>")]
        public void TryParse_Failures_ReturnFalseWithError(string input)
        {
            Assert.False(FmiTypeDescriptor.TryParse(input, out var descriptor, out var error));
            Assert.Null(descriptor);
            Assert.NotNull(error);
        }
    }
}
