# 1. مرحلة البناء (Build Stage)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# نسخ ملفات الـ csproj بناءً على أسماء المجلدات في مشروعك
COPY ["TaxiApp.Backend.Api/TaxiApp.Backend.Api.csproj", "TaxiApp.Backend.Api/"]
COPY ["TaxiApp.Backend.Core/TaxiApp.Backend.Core.csproj", "TaxiApp.Backend.Core/"]
COPY ["TaxiApp.Backend.Infrastructure/TaxiApp.Backend.Infrastructure.csproj", "TaxiApp.Backend.Infrastructure/"]

# تنفيذ الـ Restore للمكتبات
RUN dotnet restore "TaxiApp.Backend.Api/TaxiApp.Backend.Api.csproj"

# نسخ باقي الكود المصدري بالكامل
COPY . .
WORKDIR "/src/TaxiApp.Backend.Api"

# بناء المشروع بنسخة الـ Release
RUN dotnet build "TaxiApp.Backend.Api.csproj" -c Release -o /app/build

# 2. مرحلة النشر (Publish Stage)
FROM build AS publish
RUN dotnet publish "TaxiApp.Backend.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 3. المرحلة النهائية للتشغيل (Final Runtime Stage)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# إعدادات المنفذ لـ Render
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# تشغيل الـ DLL الخاص بمشروع الـ Api
ENTRYPOINT ["dotnet", "TaxiApp.Backend.Api.dll"]
