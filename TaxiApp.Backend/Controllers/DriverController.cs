using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxiApp.Backend.Core.Interfaces;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin,SuperAdmin")] // 🛡️ حماية: فقط الآدمن مسموح له بالدخول
    public class DriverController : ControllerBase
    {
        private readonly IDriverRepository _driverRepository;

        public DriverController(IDriverRepository driverRepository)
        {
            _driverRepository = driverRepository;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingDrivers()
        {
            var drivers = await _driverRepository.GetPendingDriversAsync();
            return Ok(drivers);
        }

        [HttpPost("approve/{id}")]
        public async Task<IActionResult> ApproveDriver(string id)
        {
            var result = await _driverRepository.ApproveDriverAsync(id);
            if (!result) return NotFound("Driver not found");

            return Ok(new { message = "Driver approved successfully!" });
        }
    }
}
