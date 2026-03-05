#!/bin/bash
# Console App Quick Start
# Run this from the project root to test the console app

echo "╔════════════════════════════════════════════════════╗"
echo "║  YtProducer Console App - Quick Start Guide       ║"
echo "╚════════════════════════════════════════════════════╝"
echo ""

# Check if .env file exists
if [ ! -f .env ]; then
    echo "❌ Error: .env file not found!"
    echo "   Please create .env from .env.example and configure it."
    exit 1
fi

echo "✓ .env file found"
echo ""

# Check if PostgreSQL is running (optional)
echo "Ensuring database dependencies are installed..."
dotnet restore src/YtProducer.Console/YtProducer.Console.csproj

echo ""
echo "🚀 Running console app..."
dotnet run --project src/YtProducer.Console/YtProducer.Console.csproj

if [ $? -eq 0 ]; then
    echo ""
    echo "✓ Console app completed successfully!"
else
    echo ""
    echo "❌ Console app encountered an error."
    echo "   Check that PostgreSQL is running and the .env credentials are correct."
    exit 1
fi
