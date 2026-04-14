using Microsoft.AspNetCore.Mvc;

namespace RoofingLeadGeneration.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class RoofHealthController : ControllerBase
    {
        [HttpGet("Check")]
        public IActionResult CheckRoofHealth(string address)
        {
            // Replace this with logic to analyze roof health
            if (string.IsNullOrWhiteSpace(address))
            {
                return BadRequest("Address is required.");
            }

            // Placeholder for actual logic
            var result = "Roof health report for address: " + address;
            return Ok(result);
        }
    }
}