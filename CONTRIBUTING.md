# Contributing

PRs are welcome.

## Dev setup

- .NET 8 SDK
- `dotnet restore`
- `dotnet run`

## World objects

World objects live under `World/` and are compiled at runtime.
They should implement `IMudObject` (or `IRoom` for rooms).
