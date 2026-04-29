using Microsoft.AspNetCore.Mvc;

namespace RoofingLeadGeneration.Controllers
{
    [Route("[controller]")]
    public class LegalController : Controller
    {
        [HttpGet("privacy")]
        public IActionResult Privacy() => View();

        [HttpGet("terms")]
        public IActionResult Terms() => View();
    }
}
