ARG DOTNET_VERSION=8.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-alpine AS build
WORKDIR /src

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN apk add --no-cache icu-libs

COPY *.sln .
COPY ["Presentation/Presentation.csproj", "Presentation/"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Domain/Domain.csproj", "Domain/"]
COPY ["Infrastructure/Infrastructure.csproj", "Infrastructure/"]
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["Domain.Tests/Domain.Tests.csproj", "Domain.Tests/"]
COPY ["PaymentService.Processor/PaymentService.Processor.csproj", "PaymentService.Processor/"]

RUN dotnet restore "TechChallengeFIAP.Payment.sln"

COPY . .

RUN dotnet publish Presentation/Presentation.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-alpine AS final
WORKDIR /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_HTTP_PORTS=8080

RUN apk add --no-cache icu-libs tzdata

USER app

COPY --from=build --chown=app:app /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Presentation.dll"]