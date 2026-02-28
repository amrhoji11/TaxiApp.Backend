using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.Interfaces;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PassengerController : ControllerBase
    {
        private readonly IAuthRepository authRepository;

        // شلنا الـ Passenger من هون لأنها غلط
        public PassengerController(IAuthRepository authRepository)
        {
            this.authRepository = authRepository;
        }

        [HttpPut("update-profile/{userId}")]
        public async Task<IActionResult> UpdateProfile(string userId, [FromForm] UpdatePassengerRequest request)
        {
            // وظيفة الكنترولر فقط يستلم الطلب ويبعته للـ Repository
            var result = await authRepository.UpdatePassengerProfileAsync(userId, request);

            if (!result) return NotFound("المستخدم غير موجود");

            return Ok("تم تحديث البيانات بنجاح");
        }
    }
}