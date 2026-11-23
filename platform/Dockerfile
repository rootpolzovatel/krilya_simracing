FROM node:24-bookworm-slim AS frontend
WORKDIR /src
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src
COPY Krylya.Api/Krylya.Api.csproj Krylya.Api/
RUN dotnet restore Krylya.Api/Krylya.Api.csproj
COPY Krylya.Api/ Krylya.Api/
RUN dotnet publish Krylya.Api/Krylya.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend /out .
COPY --from=frontend /src/dist ./frontend
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown app:app /home/app/.aspnet/DataProtection-Keys
ENV Frontend__Directory=/app/frontend
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Krylya.Api.dll"]
