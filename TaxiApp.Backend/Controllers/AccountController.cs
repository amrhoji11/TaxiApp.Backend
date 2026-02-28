using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
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

        // 1. طلب كود التحقق للرقم الجديد
        [Authorize]
        [HttpPut("request-change-phone")]
        public async Task<IActionResult> RequestChangePhone([FromBody] ChangePhoneRequest request)
        {
            // جلب الـ ID تبع المستخدم من التوكن (عشان نعرف مين اللي بطلب التغيير)
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _authRepository.RequestChangePhoneNumberAsync(userId, request.NewPhoneNumber);

            if (result.Contains("تم إرسال"))
                return Ok(new { message = result });

            return BadRequest(new { message = result });
        }

        // 2. تأكيد الكود وتغيير الرقم والـ UserName فعلياً
        [Authorize]
        [HttpPut("confirm-change-phone")]
        public async Task<IActionResult> ConfirmChangePhone([FromBody] ConfirmChangePhoneRequest request)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _authRepository.ConfirmChangePhoneNumberAsync(userId, request.NewPhoneNumber, request.Token);

            if (success)
                return Ok(new { message = "تم تغيير الرقم بنجاح. استخدم الرقم الجديد في تسجيل الدخول القادم." });

            return BadRequest(new { message = "الرمز غير صحيح أو انتهت صلاحيته." });
        }
    }
}
