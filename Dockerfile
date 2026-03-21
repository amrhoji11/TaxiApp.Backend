# 1. Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# لاحظ هنا استخدمنا TaxiApp.Backend لأن هذا هو اسم المجلد عندك في الصورة
COPY ["TaxiApp.Backend/TaxiApp.Backend.csproj", "TaxiApp.Backend/"]
COPY ["TaxiApp.Backend.Core/TaxiApp.Backend.Core.csproj", "TaxiApp.Backend.Core/"]
COPY ["TaxiApp.Backend.Infrastructure/TaxiApp.Backend.Infrastructure.csproj", "TaxiApp.Backend.Infrastructure/"]

RUN dotnet restore "TaxiApp.Backend/TaxiApp.Backend.csproj"

COPY . .
WORKDIR "/src/TaxiApp.Backend"
RUN dotnet build "TaxiApp.Backend.csproj" -c Release -o /app/build

# 2. Publish Stage
FROM build AS publish
RUN dotnet publish "TaxiApp.Backend.csproj" -c Release -o /app/publish /p:UseAppHost=false

# 3. Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# تأكد أن اسم الـ dll هو TaxiApp.Backend.dll
ENTRYPOINT ["dotnet", "TaxiApp.Backend.dll"]
