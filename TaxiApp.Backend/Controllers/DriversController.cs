using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Infrastructure.Repositories;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles ="Driver")]
    public class DriversController : ControllerBase
    {
        private readonly IDriverRepository driverRepository;

        public DriversController(IDriverRepository driverRepository)
        {
            this.driverRepository = driverRepository;
        }

        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile( [FromForm] UpdateDriverRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Unauthorized();
            }
            var result = await driverRepository.UpdateDriverProfileAsync(userId, request);

            if (!result) return NotFound("المستخدم غير موجود");

            return Ok("تم تحديث بيانات السائق بنجاح");
        }

      
       
    }
}
