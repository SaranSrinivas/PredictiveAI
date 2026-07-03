# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the project file first
COPY PredictiveAnalysis.csproj ./

# Restore dependencies
RUN dotnet restore PredictiveAnalysis.csproj

# Copy the rest of the source
COPY . .

# Publish the app
RUN dotnet publish PredictiveAnalysis.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "PredictiveAnalysis.dll"]
