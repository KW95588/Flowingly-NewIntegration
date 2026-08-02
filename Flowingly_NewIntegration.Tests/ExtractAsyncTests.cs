using Flowingly_NewIntegration.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flowingly_NewIntegration.Tests
{
    public class ExtractAsyncTests
    {
        private readonly ExtractData _service;

        public ExtractAsyncTests()
        {
            _service = new ExtractData(NullLogger<ExtractData>.Instance);
        }

        [Fact]
        public async Task ExtractAsync_ReturnsExpectedOutput_ForValidInput()
        {
            var input = @"Hi Patricia,
Please create an expense claim for the below. Relevant details are marked
up as requested…
<expense><cost_centre>DEV632</cost_centre><total>35,000</total><payment_method>personal card</payment_method></expense>
From: William Steele
Sent: Friday, 16 June 2022 10:32 AM";

            var decimalTaxRate = 0.15m;

            var result = await _service.ExtractAsync(input, decimalTaxRate);

            Assert.NotNull(result);
            Assert.Single(result);
            var first = result[0];
            Assert.Equal("DEV632", first.CostCentre);
            Assert.Equal(35000m, first.Total);
            Assert.Equal("personal card", first.PaymentMethod);
        }

        [Fact]
        public async Task ExtractAsync_Throws_WhenMissingTotal()
        {
            var input = @"<expense><cost_centre>DEV632</cost_centre><payment_method>personal card</payment_method></expense>";
            var decimalTaxRate = 0.15m;
            //var result = await _service.ExtractAsync(input, decimalTaxRate);
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractAsync(input, decimalTaxRate));
        }

        [Fact]
        public async Task ExtractAsync_SetsUnknown_WhenMissingCostCentreOrPaymentMethod()
        {
            var input = @"<expense><total>100</total></expense>";
            var decimalTaxRate = 0.15m;
            var result = await _service.ExtractAsync(input, decimalTaxRate);

            Assert.Single(result);
            var first = result[0];
            Assert.Equal("UNKNOWN", first.CostCentre);
            Assert.Equal("UNKNOWN", first.PaymentMethod);
            Assert.Equal(100m, first.Total);
        }

        [Fact]
        public async Task ExtractAsync_Throws_OnUnmatchedTags()
        {
            var input = @"<expense><total>100</total>"; // no closing tag for expense
            var decimalTaxRate = 0.15m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractAsync(input, decimalTaxRate));
        }

        [Fact]
        public async Task ExtractAsync_ReturnsMultipleFragments()
        {
            var input = @"<expense><cost_centre>DEV1</cost_centre><total>10</total></expense>
<expense><cost_centre>DEV2</cost_centre><total>20</total></expense>";
            var decimalTaxRate = 0.15m;
            var result = await _service.ExtractAsync(input, decimalTaxRate);

            Assert.Equal(2, result.Length);
            Assert.Equal("DEV1", result[0].CostCentre);
            Assert.Equal(10m, result[0].Total);
            Assert.Equal("DEV2", result[1].CostCentre);
            Assert.Equal(20m, result[1].Total);
        }

        [Fact]
        public async Task ExtractAsync_Throws_WhenOneFragmentMissingTotal()
        {
            var input = @"<expense><cost_centre>DEV1</cost_centre><total>10</total></expense>
<expense><cost_centre>DEV2</cost_centre></expense>";
            var decimalTaxRate = 0.15m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractAsync(input, decimalTaxRate));
        }

        [Fact]
        public async Task ExtractAsync_Parses_DecimalsWithCommas()
        {
            var input = @"<expense><total>1,234.56</total></expense>";
            var decimalTaxRate = 0.15m;
            var result = await _service.ExtractAsync(input, decimalTaxRate);

            Assert.Single(result);
            Assert.Equal(1234.56m, result[0].Total);
        }

        [Fact]
        public async Task ExtractAsync_Throws_OnMalformedXmlFragment()
        {
            var input = @"<expense><total>100<total></expense>"; // malformed nested tag
            var decimalTaxRate = 0.15m;

            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractAsync(input, decimalTaxRate));
        }

        [Fact]
        public async Task ExtractAsync_Throws_OnEmptyInput()
        {
            var decimalTaxRate = 0.15m;
            await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ExtractAsync(string.Empty, decimalTaxRate));
        }


    }
}