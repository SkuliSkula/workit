FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Workit.Shared/Workit.Shared.csproj", "Workit.Shared/"]
COPY ["Workit.OwnerApp/Workit.OwnerApp.csproj", "Workit.OwnerApp/"]
RUN dotnet restore "Workit.OwnerApp/Workit.OwnerApp.csproj"

COPY . .
WORKDIR "/src/Workit.OwnerApp"
RUN dotnet publish "Workit.OwnerApp.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workit.OwnerApp.dll"]
