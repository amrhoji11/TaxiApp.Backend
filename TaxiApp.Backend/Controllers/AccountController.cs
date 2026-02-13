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

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                var respones = await _authRepository.LoginAsync(request);

                return Ok(respones);
            }

            catch (Exception ex)
            {
                return BadRequest(ex.Message);
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
