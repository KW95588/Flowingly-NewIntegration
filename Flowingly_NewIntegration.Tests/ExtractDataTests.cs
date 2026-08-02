using Flowingly_NewIntegration.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Flowingly_NewIntegration.Tests
{
    public class ExtractDataFromTagsTests
    {
        private readonly ExtractData _service;

        public ExtractDataFromTagsTests()
        {
            _service = new ExtractData(NullLogger<ExtractData>.Instance);
        }

        [Fact]
        public async Task ExtractDataFromTags_Example1_ReturnsExpectedValues()
        {
            var input = @"Please create an expense claim for the below. Relevant details are marked
up as requested…
<expense><cost_centre>DEV632</cost_centre><total>35,000</total><payment_method>personal
card</payment_method></expense>
Please create a reservation for 10 at the <vendor>Seaside Steakhouse</vendor> for our
<description>development team’s project end celebration</description> on <date>27 April
2022</date> at 7.30pm.";

            var taxRate = 0.15m;

            var json = await _service.ExtractDataFromTags(input, taxRate);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("DEV632", root.GetProperty("cost_centre").GetString());
            Assert.Equal(35000m, root.GetProperty("total").GetDecimal());
            Assert.Equal("personal card", root.GetProperty("payment_method").GetString());
            var expectedSalesTax = 35000m * taxRate;
            var expectedExcl = 35000m - expectedSalesTax;
            Assert.Equal(expectedSalesTax, root.GetProperty("sales_tax").GetDecimal());
            Assert.Equal(expectedExcl, root.GetProperty("total_excluding_tax").GetDecimal());

            // Optional: ensure vendor/description/date are present from the input
            Assert.Equal("Seaside Steakhouse", root.GetProperty("vendor").GetString());
            Assert.Equal("development team’s project end celebration", root.GetProperty("description").GetString());
            Assert.Equal("27 April 2022", root.GetProperty("date").GetString());
        }

        [Fact]
        public async Task ExtractDataFromTags_Example2_MissingTotal_Throws()
        {
            var input = @"Please create an expense claim for the below. Relevant details are marked
up as requested…
<expense><cost_centre>DEV632</cost_centre><payment_method>personal
card</payment_method></expense>";

            var taxRate = 0.15m;

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractDataFromTags(input, taxRate));
        }

        [Fact]
        public async Task ExtractDataFromTags_Example3_MalformedTotalTag_Throws()
        {
            var input = @"Please create an expense claim for the below. Relevant details are marked
up as requested…
<expense><cost_centre>DEV632</cost_centre><total>35,000<payment_method>personal
card</payment_method></expense>";

            var taxRate = 0.15m;

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractDataFromTags(input, taxRate));
        }

        [Fact]
        public async Task ExtractDataFromTags_Example4_NoCostCentre_DefaultsToUnknown()
        {
            var input = @"<expense><total>35,000</total><payment_method>personal
card</payment_method></expense>";
            var taxRate = 0.15m;

            var json = await _service.ExtractDataFromTags(input, taxRate);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("UNKNOWN", root.GetProperty("cost_centre").GetString());
            Assert.Equal(35000m, root.GetProperty("total").GetDecimal());
            Assert.Equal("personal card", root.GetProperty("payment_method").GetString());
            var expectedSalesTax = 35000m * taxRate;
            var expectedExcl = 35000m - expectedSalesTax;
            Assert.Equal(expectedSalesTax, root.GetProperty("sales_tax").GetDecimal());
            Assert.Equal(expectedExcl, root.GetProperty("total_excluding_tax").GetDecimal());
        }
    }
}
