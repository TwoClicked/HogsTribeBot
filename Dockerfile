FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY HogsTribeBot.sln .
COPY TribeBot.Bot/TribeBot.Bot.csproj TribeBot.Bot/
COPY TribeBot.Core/TribeBot.Core.csproj TribeBot.Core/
COPY TribeBot.Data/TribeBot.Data.csproj TribeBot.Data/
COPY TribeBot.Services/TribeBot.Services.csproj TribeBot.Services/
COPY TribeBot.Common/TribeBot.Common.csproj TribeBot.Common/

RUN dotnet restore

COPY . .

RUN dotnet publish TribeBot.Bot/TribeBot.Bot.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TribeBot.Bot.dll"]