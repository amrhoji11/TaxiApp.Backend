using Microsoft.AspNetCore.Authentication;
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
        private readonly RoleManager<IdentityRole> roleManager;

        public AuthRepository(UserManager<ApplicationUser> userManager,
            ApplicationDbContext context,
            JwtService jwtService,
            RoleManager<IdentityRole> roleManager)
        {
            this.userManager = userManager;
            this._context = context;
            this.jwtService = jwtService;
            this.roleManager = roleManager;
        }

        // 1. تسجيل الراكب (Passenger)
        public async Task<RegisterPassengerResponse> RegisterPassengerAsync(RegisterPassengerRequest request)
        {
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
                    PhoneNumber = user.PhoneNumber
                };
            }

            throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // 2. تسجيل السائق (Driver) - تم تحديثه ✅
        public async Task<RegisterDriverResponse> RegisterDriverAsync(RegisterDriverRequest request)
        {
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
                    Status = DriverStatus.Pending // السائق يبدأ بحالة معلق بانتظار الموافقة
                };

                _context.Drivers.Add(driver);
                await _context.SaveChangesAsync();

                return new RegisterDriverResponse
                {
                    UserId = user.Id.ToString(),
                    FullName = $"{user.FirstName} {user.LastName}"
                };
            }

            throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        // 3. تسجيل الدخول (Login)
        public async Task<LoginResponse> LoginAsync(LoginRequest request)
        {
            var user = await userManager.Users.FirstOrDefaultAsync(u => u.PhoneNumber == request.PhoneNumber);

            if (user == null)
            {
                throw new Exception("رقم الهاتف الذي أدخلته غير مسجل لدينا.");
            }

            //  جلب الأدوار الحقيقية بدلاً من القيمة الثابتة
            var userRoles = await userManager.GetRolesAsync(user);
            var role = userRoles.FirstOrDefault() ?? "Passenger";

            // مرر الدور الحقيقي للتوكن
            var token = jwtService.GenerateToken(user, role);

            return new LoginResponse
            {
                Token = token,
                UserId = user.Id.ToString(),
                Role = role // الآن يرجع الدور الحقيقي (SuperAdmin, Driver, إلخ)
            };
        }
    }
}