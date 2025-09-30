# Giai đoạn 1: Build ứng dụng
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Sao chép solution và chỉ lấy project AI_API
COPY *.sln .
COPY AI_API/*.csproj ./AI_API/
RUN dotnet restore ./AI_API/AI_API.csproj  # Chỉ restore project AI_API

# Sao chép toàn bộ mã nguồn và build
COPY . ./
RUN dotnet publish -c Release -o out/AI_API ./AI_API/AI_API.csproj

# Giai đoạn 2: Tạo image runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Sao chép output từ giai đoạn build
COPY --from=build /app/out/AI_API .

# Thiết lập biến môi trường
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:7000
ENV DOTNET_RUNNING_IN_CONTAINER=true

# Mở cổng cho API
EXPOSE 7000

# Lệnh chạy ứng dụng
ENTRYPOINT ["dotnet", "AI_API.dll"]