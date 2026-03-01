# Development Environment Notes

## Valheim Assembly References

## Modded Client Workflow

- Keep Steam install clean at `VALHEIM_INSTALL_PATH`.
- Use a dedicated modded copy at `VALHEIM_MODDED_PATH`.
- Initialize/update modded copy with `scripts/Initialize-ModdedValheimClient.ps1`.
- Install BepInEx into `VALHEIM_MODDED_PATH`.
- Use deployment and launch scripts against `VALHEIM_MODDED_PATH` only.

**Experiment Result (2026-02-14):** Some Valheim assemblies CAN be referenced without Unity runtime!

### What Works ✅
- **assembly_utils.dll** - Utility extensions (GetStableHashCode, string helpers)
  - Loads in `dotnet test` without Unity
  - Zero UnityEngine dependencies
  - Tests run fast (<10s)
  
### What Doesn't Work ❌
- **WorldGenerator** and core game classes - Require UnityEngine runtime
  - Uses `UnityEngine.Random`, `Vector2/3`, `Color`
  - Static singletons with Unity lifecycle
  - Takes Unity-coupled objects (World, GameObject)

### Hybrid Approach
We reference `assembly_utils.dll` for utilities but port game algorithms ourselves.

**See:** `tests/WorldZones.WorldGen.Tests/AssemblyUtilsCompatibilityTest.cs` for validation tests.

---

## Windows-Specific Issues

### Test Process Locking (xUnit on Windows)

**Problem:** When running `dotnet test`, the test host process (`testhost.exe`) can hold locks on test DLLs even after tests complete, preventing subsequent builds.

**Symptoms:**
```
error MSB3027: Could not copy "xunit.abstractions.dll" to "bin\Debug\net472\xunit.abstractions.dll". 
Exceeded retry count of 10. Failed. The file is locked by: "testhost (PID)"
```

**Workarounds:**
1. **Kill hung process:** `Stop-Process -Id <PID> -Force` before rebuilding
2. **Clean build directory:** `dotnet clean` then `dotnet build`
3. **Use separate terminal:** Run tests in one terminal, builds in another
4. **Wait for process exit:** Sometimes test host takes 5-10s to fully release locks

**Root Cause:** Windows file locking + xUnit VSTest adapter behavior. Not reproducible on Linux/Mac.

**Prevention:** 
- Avoid rapid build-test-build cycles
- Let test processes fully exit before rebuilding
- Consider using `dotnet watch test` for continuous testing (manages process lifecycle)

---

## Visual Studio 2026

**Version:** Using preview/beta VS 2026 (not VS 2022)
- Solution format: `.slnx` (modern XML-based solution files)
- Generally stable, but being aware of potential preview issues

---

## .NET SDK Version

**Current:** .NET 10.0 Preview (10.0.200-preview.0.26103.119)
- Targeting .NET Framework 4.7.2 for runtime compatibility
- Using modern SDK tooling for better build performance
- SDK warnings about preview version are expected and safe to ignore

---

## Git Line Ending Warnings

Occasional warnings about LF→CRLF conversion when committing:
```
warning: in the working copy of 'file.cs', LF will be replaced by CRLF the next time Git touches it
```

**Cause:** Mixed line endings from external sources (FastNoiseLite.cs downloaded with LF)
**Impact:** None - Git normalizes on commit
**Fix (if desired):** Add `.gitattributes` with `* text=auto`
