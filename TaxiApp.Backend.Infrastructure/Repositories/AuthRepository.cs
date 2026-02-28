using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

            var token = jwtService.GenerateToken(user, role);

            return new LoginResponse
            {
                Token = token,
                UserId = user.Id,
                Role = role
            };
        }

        

        public async Task<bool> UpdatePassengerProfileAsync(
     string userId,
     UpdatePassengerRequest request)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            var passenger = await _context.Passengers
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (passenger == null)
                return false;

         
            //  تحديث بيانات المستخدم
         

            if (!string.IsNullOrWhiteSpace(request.FirstName))
                user.FirstName = request.FirstName;

            if (!string.IsNullOrWhiteSpace(request.LastName))
                user.LastName = request.LastName;

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                user.PhoneNumber = request.PhoneNumber;

            await userManager.UpdateAsync(user);

            
            //  تحديث العنوان
           

            // تحديث عنوان جديد
            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                passenger.Address = request.Address;
            }

            // حذف العنوان
            if (request.RemoveAddress)
            {
                passenger.Address = null;
            }

            
            //  إدارة الصور
           

            var folderPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Images");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            // رفع صورة جديدة
            if (request.ProfilePhotoImg != null &&
                request.ProfilePhotoImg.Length > 0)
            {
                var file = request.ProfilePhotoImg;

                var fileName = Guid.NewGuid().ToString() +
                               Path.GetExtension(file.FileName);

                var filePath = Path.Combine(folderPath, fileName);

                using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }

                // حذف الصورة القديمة
                if (!string.IsNullOrEmpty(passenger.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath,
                        passenger.ProfilePhotoUrl);

                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                passenger.ProfilePhotoUrl = fileName;
            }

            // حذف الصورة
            if (request.RemoveProfilePhoto)
            {
                if (!string.IsNullOrEmpty(passenger.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath,
                        passenger.ProfilePhotoUrl);

                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                passenger.ProfilePhotoUrl = null;
            }

            passenger.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return true;
        }




        // تعديل بيانات السائق
        public async Task<bool> UpdateDriverProfileAsync(
     string userId,
     UpdateDriverRequest request)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            var driver = await _context.Drivers
                .FirstOrDefaultAsync(d => d.UserId == userId);

            if (driver == null)
                return false;

            // =========================
            // 1️⃣ تحديث بيانات المستخدم
            // =========================

            if (!string.IsNullOrWhiteSpace(request.FirstName))
                user.FirstName = request.FirstName;

            if (!string.IsNullOrWhiteSpace(request.LastName))
                user.LastName = request.LastName;

            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
                user.PhoneNumber = request.PhoneNumber;

            await userManager.UpdateAsync(user);

            // =========================
            // 2️⃣ تحديث العنوان
            // =========================

            // تحديث عنوان جديد
            if (!string.IsNullOrWhiteSpace(request.Address))
            {
                driver.Address = request.Address;
            }

            // حذف العنوان
            if (request.RemoveAddress)
            {
                driver.Address = null;
            }

            // =========================
            // 3️⃣ إدارة الصور
            // =========================

            var folderPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Images");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            // رفع صورة جديدة
            if (request.ProfilePhotoImg != null &&
                request.ProfilePhotoImg.Length > 0)
            {
                var file = request.ProfilePhotoImg;

                var fileName = Guid.NewGuid().ToString() +
                               Path.GetExtension(file.FileName);

                var filePath = Path.Combine(folderPath, fileName);

                using (var stream = System.IO.File.Create(filePath))
                {
                    await file.CopyToAsync(stream);
                }

                // حذف القديمة
                if (!string.IsNullOrEmpty(driver.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath,
                        driver.ProfilePhotoUrl);

                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                driver.ProfilePhotoUrl = fileName;
            }

            // حذف الصورة
            if (request.RemoveProfilePhoto)
            {
                if (!string.IsNullOrEmpty(driver.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath,
                        driver.ProfilePhotoUrl);

                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                driver.ProfilePhotoUrl = null;
            }

            driver.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return true;
        }

        // 1. طلب تغيير الرقم (إرسال رمز للرقم الجديد)
        // 1. طلب تغيير الرقم (إرسال رمز للرقم الجديد)
        public async Task<string> RequestChangePhoneNumberAsync(string userId, string newPhoneNumber)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return "المستخدم غير موجود.";

            var existingUser = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == newPhoneNumber);
            if (existingUser != null) return "رقم الهاتف الجديد مسجل مسبقاً لمستخدم آخر.";

            // ✅ التصحيح: حذف كلمة "Phone"
            var token = await userManager.GenerateChangePhoneNumberTokenAsync(user, newPhoneNumber);

            return $"تم إرسال رمز التحقق للرقم الجديد: {token}";
        }

        // 2. تأكيد الرمز وتغيير الرقم فعلياً
        public async Task<bool> ConfirmChangePhoneNumberAsync(string userId, string newPhoneNumber, string token)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null) return false;

            var result = await userManager.ChangePhoneNumberAsync(user, newPhoneNumber, token);

            if (result.Succeeded)
            {
                user.UserName = newPhoneNumber;
                // تحديث الختم الأمني لإلغاء التوكنات القديمة
                await userManager.UpdateSecurityStampAsync(user);
                await userManager.UpdateAsync(user);
                return true;
            }

            return false;
        }
    }
}