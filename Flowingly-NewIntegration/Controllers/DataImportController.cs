using Flowingly_NewIntegration.Model;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Flowingly_NewIntegration.Services;
using Flowingly_NewIntegration.IServices;
using Flowingly_NewIntegration.Common;


namespace Flowingly_NewIntegration.Controllers
{
    [EnableCors("AllowOrigin")]
    [Route("api/[controller]")]
    [ApiController]
    public class DataImportController : ControllerBase
    {
        private readonly IExtractData _extractData;
        public DataImportController(IExtractData extractData)
        {
            _extractData = extractData;
        }

        [HttpPost("import")]
        public async Task<IActionResult> Import([FromBody] Data payload)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            if (string.IsNullOrWhiteSpace(payload.Message))
            {
                
                return BadRequest(new { success = false, message = "Payload is required." });
            }

            try {
                var output = await _extractData.ExtractAsync(payload.Message, (decimal)ProjectConstants.TaxRate);
                return Ok(output[0]);

            }catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }

            catch (Exception ex)
            {
                return StatusCode(500, "Internal Server Error message");
            }
            
        }

        [HttpPost("importData")]
        public async Task<IActionResult> ImportData([FromBody] Data payload)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            if (string.IsNullOrWhiteSpace(payload.Message))
            {

                return BadRequest(new { success = false, message = "Payload is required." });
            }

            try
            {
                var output = await _extractData.ExtractDataFromTags(payload.Message, (decimal)ProjectConstants.TaxRate);
                return Ok(output);

            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }

            catch (Exception ex)
            {
                return StatusCode(500, "Internal Server Error message");
            }

        }
    }
}
