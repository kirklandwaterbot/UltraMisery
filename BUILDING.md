# Building UltraMisery

## Requirements

- A .NET SDK capable of targeting .NET Standard 2.1.
- Your own ULTRAKILL installation.
- A BepInEx 5 installation, such as an r2modman ULTRAKILL profile.

Game assemblies and third-party dependencies are not redistributed in this repository. The project references them from your local installations.

## Build

From this repository, run the following PowerShell command with your actual paths:

```powershell
dotnet build UltraMisery.csproj -c Release "-p:GameDir=C:\Program Files (x86)\Steam\steamapps\common\ULTRAKILL" "-p:ProfileDir=$env:APPDATA\r2modmanPlus-local\ULTRAKILL\profiles\Default"
```

The resulting DLL is written to `bin/Release/netstandard2.1/UltraMisery.dll` and copied to `package/BepInEx/plugins/UltraMisery/UltraMisery.dll`.

Copy that DLL into a test profile's `BepInEx/plugins/UltraMisery` directory and fully restart the game. Test in a separate profile before publishing.

## Thunderstore packaging

The `package` folder contains the public README, changelog, manifest and 256x256 icon. After a successful build, ZIP the folder's contents, not the folder itself:

```powershell
Compress-Archive -Path ./package/* -DestinationPath ./UltraMisery-0.1.0-Thunderstore.zip
```

For subsequent releases, update the manifest version and `PluginVersion` in `Plugin.cs`, then rebuild. Do not include game DLLs, decompiled code, local logs, saves or private configuration.

## Validation scope

Compilation is not a gameplay test. The first release is a public beta; full campaign, custom-level and enemy-matchup coverage remains unverified.
