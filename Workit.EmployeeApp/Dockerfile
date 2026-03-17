FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Workit.Shared/Workit.Shared.csproj", "Workit.Shared/"]
COPY ["Workit.EmployeeApp/Workit.EmployeeApp.csproj", "Workit.EmployeeApp/"]
RUN dotnet restore "Workit.EmployeeApp/Workit.EmployeeApp.csproj"

COPY . .
WORKDIR "/src/Workit.EmployeeApp"
RUN dotnet publish "Workit.EmployeeApp.csproj" -c Release -o /app/publish --no-restore

# Replace appsettings.json with a runtime-injectable template
RUN echo '{"ApiBaseUrl":"__API_BASE_URL__"}' > /app/publish/wwwroot/appsettings.json

FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY Workit.EmployeeApp/nginx.conf /etc/nginx/nginx.conf
COPY Workit.EmployeeApp/docker-entrypoint.sh /docker-entrypoint.d/40-inject-config.sh
RUN chmod +x /docker-entrypoint.d/40-inject-config.sh
EXPOSE 80
