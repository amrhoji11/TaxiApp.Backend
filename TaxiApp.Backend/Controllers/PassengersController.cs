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
    [Authorize(Roles = "Passenger")]
    public class PassengersController : ControllerBase
    {
        private readonly IPassengerRepository passengerRepository;

        // شلنا الـ Passenger من هون لأنها غلط
        public PassengersController(IPassengerRepository passengerRepository)
        {
            this.passengerRepository = passengerRepository;
        }

        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile( [FromForm] UpdatePassengerRequest request)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
            {
                return Unauthorized();
            }
            // وظيفة الكنترولر فقط يستلم الطلب ويبعته للـ Repository
            var result = await passengerRepository.UpdatePassengerProfileAsync(userId, request);

            if (!result) return NotFound("المستخدم غير موجود");

            return Ok("تم تحديث البيانات بنجاح");
        }

       


    }
}