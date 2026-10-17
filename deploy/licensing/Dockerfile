FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Nexora.Domain/ src/Nexora.Domain/
COPY src/Nexora.Application/ src/Nexora.Application/
COPY src/Nexora.Licensing/ src/Nexora.Licensing/
RUN dotnet restore src/Nexora.Licensing --locked-mode
RUN dotnet publish src/Nexora.Licensing -c Release --no-restore -o /output

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /output ./
RUN mkdir /data && chown $APP_UID /data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 QIK_LICENSING_SETTINGS=/data/settings.json
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nexora.Licensing.dll"]
