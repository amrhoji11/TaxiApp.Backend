using Mapster;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")] // 🛡️ حماية: فقط الآدمن مسموح له بالدخول
    public class DriverApprovalsController : ControllerBase
    {
        private readonly IDriverApprovalRepository _driverApprovalRepository;

        public DriverApprovalsController(IDriverApprovalRepository driverApprovalRepository)
        {
            _driverApprovalRepository = driverApprovalRepository;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPendingDrivers()
        {
            var drivers = await _driverApprovalRepository.GetPendingDriversAsync();
            return Ok(drivers);
        }

        [HttpPost("approve/{id}")]
        public async Task<IActionResult> ApproveDriver([FromRoute] string id)
        {
            var officeId = User.FindFirstValue("UserId");
            if (officeId == null)
            {
                return Unauthorized();

            }
            var result = await _driverApprovalRepository.ApproveDriverAsync(officeId,id);
            if (!result) return NotFound("Driver not found");

            return Ok(new { message = "Driver approved successfully!" });
        }
    }
}
