# Better Movement for KBM — native ASI runtime

This project builds the native x64 runtime used by Better Movement for KBM v2.0.

The release targets Ghost Recon Wildlands game version `133.1.0.9840374` / Steam build `24669148`. Before changing anything, the ASI verifies every required instruction against that exact executable layout. Unsupported builds are left untouched.

The runtime executes inside `GRW.exe`; it does not use external-process memory APIs, create configuration files, or write diagnostic logs.

Build the release configuration with Visual Studio's C++ toolchain:

```powershell
msbuild BetterMovementASI\BetterMovementASI.vcxproj /p:Configuration=Release /p:Platform=x64
```

The artifact is written to:

```text
BetterMovementASI\bin\Release\x64\BetterMovementForKBM.asi
```

Runtime installation consists of `BetterMovementForKBM.asi` and the x64 `winmm.dll` build of Ultimate ASI Loader placed beside `GRW.exe`.
