# YtProducer Console App - Quick Start (Windows)
# Run this from the project root to test the console app

Write-Host "╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  YtProducer Console App - Quick Start Guide       ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# Check if .env file exists
if (-not (Test-Path .env)) {
    Write-Host "❌ Error: .env file not found!" -ForegroundColor Red
    Write-Host "   Please create .env from .env.example and configure it."
    exit 1
}

Write-Host "✓ .env file found" -ForegroundColor Green
Write-Host ""

# Check if PostgreSQL is running (optional)
Write-Host "Ensuring database dependencies are installed..."
dotnet restore src/YtProducer.Console/YtProducer.Console.csproj

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to restore packages" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "🚀 Running console app..." -ForegroundColor Green
dotnet run --project src/YtProducer.Console/YtProducer.Console.csproj

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "✓ Console app completed successfully!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "❌ Console app encountered an error." -ForegroundColor Red
    Write-Host "   Check that PostgreSQL is running and the .env credentials are correct."
    exit 1
}
