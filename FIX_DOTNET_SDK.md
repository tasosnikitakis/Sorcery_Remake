# Fix: Install .NET SDK
**Issue: .NET Runtime installed but SDK missing**

---

## Problem

You currently have:
- ✅ .NET Runtime 8.0.15 (can run .NET apps)
- ❌ .NET SDK (needed to BUILD .NET apps)

**Error:** "No .NET SDKs were found"

---

## Solution: Install .NET SDK

### Option 1: Install .NET 8 SDK (Recommended)

Since our project targets .NET 8, install the matching SDK:

**Download:**
https://dotnet.microsoft.com/download/dotnet/8.0

**Steps:**
1. Click the link above
2. Under ".NET 8.0 SDK", download:
   - **Windows x64** → `dotnet-sdk-8.0.xxx-win-x64.exe`
3. Run the installer
4. **Restart your computer** (important!)
5. Verify installation (see below)

### Option 2: Install .NET 10 SDK (Latest)

If you prefer the latest version:

**Download:**
https://dotnet.microsoft.com/download/dotnet/10.0

**Note:** .NET 10 is backward compatible with .NET 8 projects.

---

## Verify Installation

After installing and restarting:

```bash
# Check SDK is installed
dotnet --list-sdks
```

**Expected output:**
```
8.0.xxx [C:\Program Files\dotnet\sdk]
```

**Or for .NET 10:**
```
10.0.xxx [C:\Program Files\dotnet\sdk]
```

---

## After SDK Installation

### 1. Restart Everything
```bash
# 1. Close VS Code
# 2. Close any terminals
# 3. Restart your computer (ensures PATH is updated)
```

### 2. Verify in New Terminal
```bash
# Open new PowerShell or CMD
dotnet --version
# Should show: 8.0.xxx or 10.0.xxx
```

### 3. Test the Project
```bash
cd d:\sorcery+_remake
dotnet restore
dotnet build
```

**Expected:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### 4. Run the Game
```bash
dotnet run
```

**Or in VS Code:** Press **F5**

---

## Why This Happened

**SDK vs Runtime:**
- **Runtime** = Can RUN pre-built .NET applications
- **SDK** = Can BUILD .NET applications (includes runtime + compiler + tools)

When you installed ".NET 10", you might have downloaded the **Runtime** installer instead of the **SDK** installer.

**SDK installer filename should contain "sdk":**
- ✅ `dotnet-sdk-8.0.404-win-x64.exe` (SDK - correct)
- ❌ `dotnet-runtime-8.0.15-win-x64.exe` (Runtime only - won't work)

---

## Troubleshooting

### After installing, still shows "No SDKs found"

**Fix:**
1. Restart computer (not just VS Code)
2. Open **new** terminal
3. Run `dotnet --list-sdks`

### Can't download SDK

**Offline installer:**
1. Go to https://dotnet.microsoft.com/download/dotnet/8.0
2. Find "Installers" section
3. Download offline installer for your OS

### Multiple .NET versions

It's OK to have multiple versions! They coexist peacefully:
```
dotnet-sdk-6.0.xxx
dotnet-sdk-8.0.xxx
dotnet-sdk-10.0.xxx
```

Our project will use .NET 8 (specified in SorceryRemake.csproj).

---

## Quick Links

| Version | Download Link |
|---------|---------------|
| **.NET 8 SDK (Recommended)** | https://dotnet.microsoft.com/download/dotnet/8.0 |
| .NET 10 SDK (Latest) | https://dotnet.microsoft.com/download/dotnet/10.0 |
| All versions | https://dotnet.microsoft.com/download/dotnet |

---

## Next Steps After Installation

1. **Restart computer**
2. **Verify:** `dotnet --list-sdks` shows installed SDK
3. **Build:** `cd d:\sorcery+_remake && dotnet build`
4. **Run:** Press F5 in VS Code or `dotnet run`

---

**Current Status:**
- ❌ SDK Not Installed
- ✅ Runtime Installed (8.0.15)
- ✅ Project Ready (waiting for SDK)

**Action Required:** Install .NET 8 SDK → Restart → Build → Play! 🎮
