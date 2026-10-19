using Xunit;
using FRAMLab.Shared;

namespace FRAMLab.Tests.Fmi
{
    public class FmiValueTests
    {
        [Theory]
        [InlineData(40.0, "40")]
        [InlineData(0.5, "0.5")]
        [InlineData(-3.25, "-3.25")]
        public void Number_RoundTrips(double value, string legacy)
        {
            Assert.Equal(legacy, FmiValue.Number(value).ToLegacyString());
            Assert.Equal(value, FmiValue.FromLegacyString(legacy).AsNumber());
        }

        [Theory]
        [InlineData(true, "1")]
        [InlineData(false, "0")]
        public void Bool_RoundTrips(bool value, string legacy)
        {
            Assert.Equal(legacy, FmiValue.Bool(value).ToLegacyString());
            Assert.Equal(value, FmiValue.FromLegacyString(legacy, FmiTypeDescriptor.Boolean).AsBool());
        }

        [Fact]
        public void Array_RoundTrips_AsCsv()
        {
            var value = FmiValue.Array(new List<FmiValue> { FmiValue.Number(1), FmiValue.Number(2), FmiValue.Number(3) });
            Assert.Equal("1,2,3", value.ToLegacyString());
            FmiTypeDescriptor.TryParse("array<number>", out var type, out _);
            var parsed = FmiValue.FromLegacyString("1,2,3", type);
            Assert.Equal(FmiValueType.Array, parsed.Type);
            Assert.Equal(new[] { 1.0, 2.0, 3.0 }, parsed.AsList()!.Select(v => v.AsNumber()));
        }

        [Fact]
        public void Dictionary_RoundTrips_AsKeyValueCsv()
        {
            var value = FmiValue.Dict(new Dictionary<string, FmiValue>
            {
                ["low"] = FmiValue.Number(0.2),
                ["high"] = FmiValue.Number(0.8)
            });
            Assert.Equal("low:0.2,high:0.8", value.ToLegacyString());
            FmiTypeDescriptor.TryParse("dictionary", out var type, out _);
            var parsed = FmiValue.FromLegacyString("low:0.2,high:0.8", type);
            Assert.Equal(FmiValueType.Dictionary, parsed.Type);
            Assert.Equal(0.2, parsed.AsDictionary()!["low"].AsNumber());
            Assert.Equal(0.8, parsed.AsDictionary()!["high"].AsNumber());
        }

        [Fact]
        public void NestedArray_FlattensOnSerialize_PinningKnownLimitation()
        {
            //V4: nested arrays serialize flattened; no escaping scheme in the legacy bridge
            var value = FmiValue.Array(new List<FmiValue>
            {
                FmiValue.Array(new List<FmiValue> { FmiValue.Number(1), FmiValue.Number(2) }),
                FmiValue.Array(new List<FmiValue> { FmiValue.Number(3), FmiValue.Number(4) })
            });
            Assert.Equal("1,2,3,4", value.ToLegacyString());
        }

        [Theory]
        [InlineData("", FmiValueType.Empty)]
        [InlineData("42", FmiValueType.Number)]
        [InlineData("-1.5", FmiValueType.Number)]
        [InlineData("true", FmiValueType.Boolean)]
        [InlineData("FALSE", FmiValueType.Boolean)]
        [InlineData("abc", FmiValueType.String)]
        public void UntypedInference(string raw, FmiValueType expected)
        {
            Assert.Equal(expected, FmiValue.FromLegacyString(raw).Type);
        }

        [Fact]
        public void NumberCoercionFailure_PreservesRaw_AsString()
        {
            var value = FmiValue.FromLegacyString("abc", FmiTypeDescriptor.Number, out var coerced);
            Assert.False(coerced);
            Assert.Equal(FmiValueType.String, value.Type);
            Assert.Equal("abc", value.AsString());
        }

        [Fact]
        public void BooleanCoercion_OnlyCleanTokensSucceed()
        {
            Assert.True(FmiValue.FromLegacyString("0", FmiTypeDescriptor.Boolean, out _).Type == FmiValueType.Boolean);
            Assert.True(FmiValue.FromLegacyString("1", FmiTypeDescriptor.Boolean, out _).Type == FmiValueType.Boolean);
            Assert.True(FmiValue.FromLegacyString("true", FmiTypeDescriptor.Boolean, out _).Type == FmiValueType.Boolean);
            Assert.True(FmiValue.FromLegacyString("false", FmiTypeDescriptor.Boolean, out _).Type == FmiValueType.Boolean);

            var value = FmiValue.FromLegacyString("2", FmiTypeDescriptor.Boolean, out var coerced);
            Assert.False(coerced);
            Assert.Equal("2", value.AsString());
        }

        [Fact]
        public void Dictionary_EmptyKeySegment_IsFlaggedAndSkipped()
        {
            FmiTypeDescriptor.TryParse("dictionary", out var type, out _);
            var value = FmiValue.FromLegacyString("low:0.2,:5", type, out var coerced);
            Assert.False(coerced);
            Assert.Single(value.AsDictionary()!);
            Assert.Equal(0.2, value.AsDictionary()!["low"].AsNumber());
        }

        [Fact]
        public void Dictionary_SegmentWithoutColon_IsFlaggedAndSkipped()
        {
            FmiTypeDescriptor.TryParse("dictionary", out var type, out _);
            var value = FmiValue.FromLegacyString("low:0.2,bogus", type, out var coerced);
            Assert.False(coerced);
            Assert.Single(value.AsDictionary()!);
        }
    }
}
