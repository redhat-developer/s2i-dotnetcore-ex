#!/bin/sh -l


dotnet restore
dotnet build
dotnet run --project MarketPriceGap