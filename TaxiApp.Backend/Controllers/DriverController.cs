using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.Interfaces;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DriverController : ControllerBase
    {
        private readonly IAuthRepository authRepository;

        public DriverController(IAuthRepository authRepository)
        {
            this.authRepository = authRepository;
        }

        [HttpPut("update-profile/{userId}")]
        public async Task<IActionResult> UpdateProfile(string userId, [FromForm] UpdateDriverRequest request)
        {
            
            var result = await authRepository.UpdateDriverProfileAsync(userId, request);

            if (!result) return NotFound("المستخدم غير موجود");

            return Ok("تم تحديث بيانات السائق بنجاح");
        }
    }
}
