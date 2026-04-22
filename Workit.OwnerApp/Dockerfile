FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY Workit.OwnerApp/publish/ .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Workit.OwnerApp.dll"]
