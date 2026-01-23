#!/bin/bash
# Fatal Attraction - Build and Run Script

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${YELLOW}Fatal Attraction - C# Game Engine${NC}"
echo "=================================="
echo ""

# Check for .NET
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}❌ .NET SDK not found. Please install .NET 6.0 or higher.${NC}"
    echo "Visit: https://dotnet.microsoft.com/download"
    exit 1
fi

echo -e "${GREEN}✓ .NET SDK found:${NC}"
dotnet --version
echo ""

# Restore dependencies
echo -e "${YELLOW}📦 Restoring dependencies...${NC}"
dotnet restore

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Restore failed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Dependencies restored${NC}"
echo ""

# Build
echo -e "${YELLOW}🔨 Building project...${NC}"
dotnet build

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Build failed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Build successful${NC}"
echo ""

# Run
echo -e "${YELLOW}🎮 Starting game...${NC}"
echo ""
dotnet run
