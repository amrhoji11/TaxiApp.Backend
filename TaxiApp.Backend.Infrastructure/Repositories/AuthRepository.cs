using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core;
using TaxiApp.Backend.Core.DTO_S.AuthDto;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Responses;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class AuthRepository : IAuthRepository
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly ApplicationDbContext _context;
        private readonly JwtService jwtService;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly RoleManager<IdentityRole> roleManager;

        public AuthRepository(UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            JwtService jwtService,
            IHttpContextAccessor httpContextAccessor,
            RoleManager<IdentityRole> roleManager)
        {
            this.userManager = userManager;
            this._context = context;
            this.jwtService = jwtService;
            this.httpContextAccessor = httpContextAccessor;
            this.roleManager = roleManager;
        }

        // 1. تسجيل الراكب (Passenger)
        public async Task<RegisterPassengerResponse> RegisterPassengerAsync(RegisterPassengerRequest request)
        {
            var existingUser = await userManager.FindByNameAsync(request.PhoneNumber);

            if (existingUser != null)
                return new RegisterPassengerResponse
                {
                    UserId = existingUser.Id,
                    FullName = $"{existingUser.FirstName} {existingUser.LastName}",
                    PhoneNumber = existingUser.PhoneNumber,
                    Message = "رقم الهاتف مسجل مسبقاً، يرجى تسجيل الدخول."
                };

            var user = new ApplicationUser
            {
                UserName = request.PhoneNumber,
                PhoneNumber = request.PhoneNumber,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user);

            if (result.Succeeded)
            {
                // ✅ إسناد دور الراكب للمستخدم
                await userManager.AddToRoleAsync(user, "Passenger");

                var passenger = new Passenger
                {
                    UserId = user.Id
                };

                _context.Passengers.Add(passenger);
                await _context.SaveChangesAsync();

                return new RegisterPassengerResponse
                {
                    UserId = user.Id.ToString(),
                    FullName = $"{user.FirstName} {user.LastName}",
                    PhoneNumber = user.PhoneNumber,
                    Message= "تم إنشاء الحساب بنجاح."
                };
            }

            // إذا فشل الإنشاء، نضع كل الأخطاء في Message مفصلة
            var errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));

            return new RegisterPassengerResponse
            {
                Message = $"فشل إنشاء الحساب: {errors}"
            };
        }

        // 2. تسجيل السائق (Driver) - تم تحديثه ✅
        public async Task<RegisterDriverResponse> RegisterDriverAsync(RegisterDriverRequest request)
        {
            var existingUser = await userManager.FindByNameAsync(request.PhoneNumber);

            if (existingUser != null)
                return new RegisterDriverResponse
                {
                    UserId=existingUser.Id,
                    FullName =$"{existingUser.FirstName} {existingUser.LastName}",
                    PhoneNumber=existingUser.PhoneNumber,
                    Message = "رقم الهاتف مسجل مسبقاً، يرجى تسجيل الدخول."
                };

            var user = new ApplicationUser
            {
                UserName = request.PhoneNumber,
                PhoneNumber = request.PhoneNumber,
                FirstName = request.FirstName,
                LastName = request.LastName,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(user);

            if (result.Succeeded)
            {
                // ✅ إسناد دور السائق للمستخدم
                await userManager.AddToRoleAsync(user, "Driver");

                var driver = new Driver
                {
                    UserId = user.Id,
                    Status = DriverStatus.offline // السائق يبدأ بحالة معلق بانتظار الموافقة
                };

                // إنشاء Approval تلقائياً
                var approval = new DriverApproval
                {
                    DriverId = user.Id,
                    Status = ApprovalStatus.pending
                };

                _context.Drivers.Add(driver);
                _context.DriverApprovals.Add(approval);
                await _context.SaveChangesAsync();

                return new RegisterDriverResponse
                {
                    UserId = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    PhoneNumber = user.PhoneNumber,
                    Message = "تم إنشاء الحساب بنجاح، بانتظار موافقة المكتب."
                };
            }

            // إذا فشل الإنشاء، نضع كل الأخطاء في Message مفصلة
            var errors = string.Join(", ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));

            return new RegisterDriverResponse
            {
                Message = $"فشل إنشاء الحساب: {errors}"
            };
        }

        // 3. تسجيل الدخول (Login)
        public async Task<string> LoginAsync(LoginRequest request)
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (user == null) return"رقم الهاتف غير مسجل.";

            // 🔎 جلب أدوار المستخدم
            var userRoles = await userManager.GetRolesAsync(user);
            var role = userRoles.FirstOrDefault();

            // 🚫 إذا كان Driver نتحقق من الموافقة أولاً
            if (role == "Driver")
            {
                var approval = await _context.DriverApprovals
                    .FirstOrDefaultAsync(a => a.DriverId == user.Id);

                if (approval == null)
                   return "طلب السائق غير موجود.";

                if (approval.Status == ApprovalStatus.pending)
                    return "لا يمكنك تسجيل الدخول حتى تتم الموافقة عليك من قبل المكتب.";

                if (approval.Status == ApprovalStatus.rejected)
                    return "تم رفض طلب تسجيلك من قبل المكتب.";
            }

            // توليد الرمز المدمج (سيستخدم إعداد الـ 5 دقائق الذي وضعناه)
            var otpCode = await userManager.GenerateTwoFactorTokenAsync(user, "Phone");

            // هنا يتم إرسال الـ otpCode للمكتب
            return $"تم إرسال رمز الدخول: {otpCode}";
        }


        public async Task<LoginResponse> VerifyOtpAndLoginAsync(VerifyOtpRequest request)
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);
            if (user == null) throw new Exception("المستخدم غير موجود.");

            // التحقق الرسمي من مايكروسوفت
            var isValid = await userManager.VerifyTwoFactorTokenAsync(user, "Phone", request.OtpCode);

            if (!isValid) throw new Exception("الرمز خاطئ أو انتهت صلاحيته.");

            // جلب الدور الحقيقي (سيكون Admin في حالة المكتب)
            var userRoles = await userManager.GetRolesAsync(user);
            var role = userRoles.FirstOrDefault() ?? "Passenger";

            if (role == "Driver")
            {
                var approval = await _context.DriverApprovals
                    .FirstOrDefaultAsync(a => a.DriverId == user.Id);

                if (approval == null)
                    throw new Exception("طلب السائق غير موجود.");

                if (approval.Status == ApprovalStatus.pending)
                    throw new Exception("لم يتم قبولك بعد من قبل المكتب.");

                if (approval.Status == ApprovalStatus.rejected)
                    throw new Exception("تم رفض طلب تسجيلك.");
            }

            // إنشاء Access Token
            var accessToken = jwtService.GenerateToken(user, role);

            // إنشاء Refresh Token آمن
            var rawToken = GenerateRefreshToken();

            var refreshToken = new RefreshToken
            {
                TokenHash = ComputeHash(rawToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                UserId = user.Id
            };

            _context.RefreshTokens.Add(refreshToken);
            await _context.SaveChangesAsync();

            return new LoginResponse
            {
                Token = accessToken,
                RefreshToken = rawToken, // يرجع فقط هنا
                UserId = user.Id,
                Role = role
            };
        }

        private string GenerateRefreshToken()
        {
            var randomBytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(randomBytes);
        }

        public async Task<LoginResponse?> RefreshTokenAsync(string refreshToken)
        {
            var tokenHash = ComputeHash(refreshToken);

            var existingToken = await _context.RefreshTokens
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.TokenHash == tokenHash);

            if (existingToken == null)
                return null;

            if (existingToken.IsRevoked)
                return null;

            if (existingToken.ExpiresAt <= DateTime.UtcNow)
                return null;

            var user = existingToken.User;
            var roles = await userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "Passenger";

            // Rotate
            existingToken.IsRevoked = true;
            existingToken.RevokedAt = DateTime.UtcNow;

            var newRawToken = GenerateRefreshToken();

            var newRefreshToken = new RefreshToken
            {
                TokenHash = ComputeHash(newRawToken),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(14),
                UserId = user.Id
            };

            existingToken.ReplacedByTokenHash = newRefreshToken.TokenHash;

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();

            var newAccessToken = jwtService.GenerateToken(user, role);

            return new LoginResponse
            {
                Token = newAccessToken,
                RefreshToken = newRawToken,
                UserId = user.Id,
                Role = role
            };
        }


       



    

        // 1. طلب تغيير الرقم (إرسال رمز للرقم الجديد)
        // 1. طلب تغيير الرقم (إرسال رمز للرقم الجديد)
        public async Task<string> RequestChangePhoneNumberAsync( string userId,string newPhoneNumber)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return "المستخدم غير موجود";

            var existingUser = await userManager.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == newPhoneNumber);

            if (existingUser != null)
                return "الرقم مستخدم مسبقاً";

            var token = await userManager
                .GenerateChangePhoneNumberTokenAsync(user, newPhoneNumber);

            return token; // هنا المفروض تبعث SMS
        }

        // 2. تأكيد الرمز وتغيير الرقم فعلياً
        public async Task<bool> ConfirmChangePhoneNumberAsync( string userId, string newPhoneNumber, string token)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            var result = await userManager
                .ChangePhoneNumberAsync(user, newPhoneNumber, token);

            if (!result.Succeeded)
                return false;

            // مهم جداً لو كنت تستخدم PhoneNumber كـ Username
            user.UserName = newPhoneNumber;

            await userManager.UpdateSecurityStampAsync(user);
            await userManager.UpdateAsync(user);

            return true;
        }

        public async Task<bool> RevokeRefreshTokenAsync(string userId, string refreshToken)
        {
            var tokenHash = ComputeHash(refreshToken);

            var existingToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(r =>
                    r.TokenHash == tokenHash &&
                    r.UserId == userId);

            if (existingToken == null)
                return false;

            if (existingToken.IsRevoked)
                return false;

            if (existingToken.ExpiresAt <= DateTime.UtcNow)
                return false;

            existingToken.IsRevoked = true;
            existingToken.RevokedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return true;
        }

        private string ComputeHash(string token)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(token);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }


    }
}