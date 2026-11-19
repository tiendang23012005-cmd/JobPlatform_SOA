FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG PROJECT
WORKDIR /src
COPY . .
RUN dotnet publish ${PROJECT} -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG DLL
ENV DLL=${DLL} ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["sh","-c","dotnet $DLL"]
