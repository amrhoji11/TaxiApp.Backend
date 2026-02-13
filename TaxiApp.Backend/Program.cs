using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaxiApp.Backend.Core;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;
using TaxiApp.Backend.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace TaxiApp.Backend
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1. إضافة الـ Controllers والـ Swagger
            builder.Services.AddControllers()
     .AddJsonOptions(options =>
     {
         // هذا السطر يمنع الدوران اللانهائي في البيانات
         options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
     });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 2. إعداد قاعدة البيانات
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

            // 3. إعداد الـ Identity (الهوية)
            builder.Services.AddIdentityCore<ApplicationUser>(options => {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 1;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager<SignInManager<ApplicationUser>>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

            // 4. 🔥 إعدادات الـ Authentication لقراءة الـ JWT
            builder.Services.AddAuthentication(options => {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options => {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidAudience = builder.Configuration["JWT:ValidAudience"],
                    ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JWT:Secret"]))
                };
            });

            builder.Services.AddAuthorization();

            // 5. تسجيل الخدمات (Dependency Injection)
            builder.Services.AddScoped<IAuthRepository, AuthRepository>();
            builder.Services.AddScoped<JwtService>();
            builder.Services.AddScoped<IDriverRepository, DriverRepository>();

            var app = builder.Build();

            // 6. 🔥 تعريف الأدوار تلقائياً عند تشغيل السيرفر (Seeding)
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

                string[] roleNames = { "SuperAdmin", "Admin", "Driver", "Passenger" };

                foreach (var roleName in roleNames)
                {
                    // فحص وجود الدور بدون استخدام await مباشرة
                    var roleExist = roleManager.RoleExistsAsync(roleName).GetAwaiter().GetResult();

                    if (!roleExist)
                    {
                        // إنشاء الدور بدون استخدام await مباشرة
                        roleManager.CreateAsync(new IdentityRole(roleName)).GetAwaiter().GetResult();
                    }
                }
            }

            // 7. إعدادات الـ Middleware
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            // الترتيب هنا "مقدس": المصادقة أولاً ثم التصريح
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}