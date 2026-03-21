using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;
using TaxiApp.Backend.Infrastructure.Repositories;
using TaxiApp.Backend.Infrastructure;
using TaxiApp.Backend.Core;
using TaxiApp.Backend.Infrastructure.Helper; // لضمان عمل DbSeeder

namespace TaxiApp.Backend
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();



            builder.Services.AddHttpContextAccessor();

            // 1. إضافة الـ Controllers مع منع الدوران اللانهائي (من كودك)
            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
                });

            // دعم Swagger و OpenAPI
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddOpenApi();

            // 2. إعداد قاعدة البيانات (تبديل تلقائي بين SQL Server و PostgreSQL)
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");

                if (!string.IsNullOrEmpty(databaseUrl))
                {
                    // إذا وجد رابط DATABASE_URL (هذا يعني أننا على Render)
                    options.UseNpgsql(databaseUrl);
                }
                else
                {
                    // إذا لم يجده (هذا يعني أننا نبرمج محلياً)
                    options.UseSqlServer(connectionString);
                }
            });

            // 3. إعداد الـ Identity مع خيارات كلمة المرور (من كودك)
            builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 1;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                // --- إضافة هذا الجزء لتغيير مدة الـ OTP ---
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultPhoneProvider;
                options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultEmailProvider;
                options.Tokens.ChangePhoneNumberTokenProvider = TokenOptions.DefaultPhoneProvider;
            })
            .AddRoles<IdentityRole>()
            .AddSignInManager<SignInManager<ApplicationUser>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

            // تعيين مدة الصلاحية لكل الرموز (OTP) إلى 5 دقائق
            builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
            {
                options.TokenLifespan = TimeSpan.FromMinutes(2);
            });

            // 4. إعدادات الـ Authentication والـ JWT (من كودك)
            builder.Services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true, // 🔥 هذا أهم سطر
                    ClockSkew = TimeSpan.Zero, // يمنع مهلة الـ 5 دقائق الافتراضية

                    ValidAudience = builder.Configuration["JWT:ValidAudience"],
                    ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
                    IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["JWT:Secret"]))
                };
            });

            builder.Services.AddAuthorization();

            // 5. تسجيل كافة الخدمات (من الطرفين)
            builder.Services.AddScoped<IOrderRepository, OrderRepository>();
            builder.Services.AddScoped<IAuthRepository, AuthRepository>();
            builder.Services.AddScoped<IVehicleRepository, VehicleRepository>();
            builder.Services.AddScoped<IUserBlockRepository, UserBlockRepository>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();
            builder.Services.AddScoped<IDriverRepository, DriverRepository>();

            builder.Services.AddScoped<IPassengerRepository, PassengerRepository>();
            builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
            builder.Services.AddSingleton<ActiveTripStore>();

            builder.Services.AddHostedService<RefreshTokenCleanupService>();
            builder.Services.AddHostedService<DatabaseCleanupService>();
            builder.Services.AddSignalR();





            builder.Services.AddScoped<JwtService>();
            builder.Services.AddScoped<IDriverApprovalRepository, DriverApprovalRepository>();

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll",
                    policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader();
                    });
            });

            var app = builder.Build();

            // 6. تشغيل الـ Seeding (الأدوار والبيانات الأولية)
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;

                // أولاً: دمج منطق الـ Roles من كودك
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
                string[] roleNames = { "SuperAdmin", "Admin", "Driver", "Passenger" };
                foreach (var roleName in roleNames)
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                    {
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                    }
                }

                // ثانياً: دمج الـ DbSeeder من كود زميلك
                await DbSeeder.SeedAdminAsync(services); // إضافة هذا السطر            }

                // 7. إعدادات الـ Middleware
                if (app.Environment.IsDevelopment())
                {

                    app.MapOpenApi();
                }

               
                    app.UseSwagger();
                app.UseSwaggerUI(c => {
                    c.RoutePrefix = string.Empty; // لفتح Swagger مباشرة عند فتح الرابط
                });

                app.MapHub<NotificationHub>("/notificationHub");

                app.UseHttpsRedirection();
                app.UseCors("AllowAll");

                // الترتيب "المقدس"
                app.UseAuthentication();
                app.UseAuthorization();

                app.MapControllers();

                app.Run();
            }
        }
    }
}