FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Workit.Shared/Workit.Shared.csproj", "Workit.Shared/"]
COPY ["Workit.Api/Workit.Api.csproj", "Workit.Api/"]
RUN dotnet restore "Workit.Api/Workit.Api.csproj"

COPY . .
WORKDIR "/src/Workit.Api"
RUN dotnet publish "Workit.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workit.Api.dll"]
