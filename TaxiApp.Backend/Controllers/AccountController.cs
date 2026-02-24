using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TaxiApp.Backend.Core.DTO_S.AuthDto;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController : ControllerBase
    {

        // 1. تعريف الحقل الخاص
        private readonly IAuthRepository _authRepository;
        private readonly UserManager<ApplicationUser> userManager;

        // 2. تمرير الـ Repository عبر الـ Constructor
        public AccountController(IAuthRepository authRepository, UserManager<ApplicationUser> userManager)
        {
            _authRepository = authRepository;
            this.userManager = userManager;
        }
        [HttpPost("registerPassenger")]
        public async Task<IActionResult> RegisterPassenger([FromBody] RegisterPassengerRequest request)
        {
            // استدعاء الـ Repository لتنفيذ عملية الحفظ
            var response = await _authRepository.RegisterPassengerAsync(request);

            // الآن ستجد البيانات في قاعدة البيانات
            return Ok(response);
        }

        [HttpPost("registerDriver")]
        public async Task<IActionResult> RegisterDriver([FromBody] RegisterDriverRequest request)
        {
            // استدعاء الـ Repository لتنفيذ عملية الحفظ
            var response = await _authRepository.RegisterDriverAsync(request);

            // الآن ستجد البيانات في قاعدة البيانات
            return Ok(response);
        }

        // 1. طلب تسجيل الدخول (يرسل الرمز للهاتف)
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var message = await _authRepository.LoginAsync(request);
                // ملاحظة: في مرحلة التطوير الرمز يرجع في الـ Response لسهولة التجربة
                return Ok(new { Message = message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }

        // 2. التحقق من الرمز والحصول على التوكن (للمكتب والجميع)
        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            try
            {
                var response = await _authRepository.VerifyOtpAndLoginAsync(request);
                return Ok(response);
            }
            catch (Exception ex)
            {
                // إذا كان الرمز خطأ أو منتهي الصلاحية سيصل هنا
                return Unauthorized(new { Error = ex.Message });
            }
        }

        //[HttpPost("logout")]
        //public async Task<IActionResult> Logout()
        //{
        //    await
        //    return Ok(new { Message = "Logged out successfully" });


        //}
    }
}
