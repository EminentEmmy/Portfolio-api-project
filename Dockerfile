# Stage 1: Build and restore the application inside a compilation image
FROM ://microsoft.com AS build
WORKDIR /src

# Copy project definition models and restore package manifests
COPY ["PortfolioApi/PortfolioApi.csproj", "PortfolioApi/"]
RUN dotnet restore "PortfolioApi/PortfolioApi.csproj"

# Copy remaining code files and compile production configurations
COPY . .
WORKDIR "/src/PortfolioApi"
RUN dotnet build "PortfolioApi.csproj" -c Release -o /app/build

# Stage 2: Publish optimized binary artifacts
FROM build AS publish
RUN dotnet publish "PortfolioApi.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Run compiled application layers inside a lean runtime engine
FROM ://microsoft.com AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 5220
ENV ASPNETCORE_URLS=http://+:5220
ENTRYPOINT ["dotnet", "PortfolioApi.dll"]
