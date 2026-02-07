# .NET SDK Download Guide (Step-by-Step)

**Problem:** You keep downloading the Runtime instead of the SDK.

---

## Visual Guide - Which Button to Click

When you go to the .NET download page, there are **multiple download buttons**. You need the **SDK**, not the Runtime.

### Go to this URL:
**https://dotnet.microsoft.com/en-us/download/dotnet/8.0**

### You'll see a page like this:

```
┌─────────────────────────────────────────────────┐
│ .NET 8.0 (recommended)                          │
├─────────────────────────────────────────────────┤
│                                                 │
│ SDK 8.0.404                                    │
│ Build apps                                      │
│                                                 │
│ ┌──────────────────┐                           │
│ │  Download x64    │ ← CLICK THIS ONE!         │
│ └──────────────────┘                           │
│                                                 │
├─────────────────────────────────────────────────┤
│                                                 │
│ Runtime 8.0.15                                  │
│ Run apps                                        │
│                                                 │
│ ┌──────────────────┐                           │
│ │  Download x64    │ ← NOT THIS ONE!           │
│ └──────────────────┘                           │
│                                                 │
└─────────────────────────────────────────────────┘
```

### Key Differences:

| ❌ WRONG (Runtime) | ✅ CORRECT (SDK) |
|-------------------|------------------|
| Says "Runtime" | Says "SDK" |
| Says "Run apps" | Says "Build apps" |
| File: `dotnet-runtime-8.0.15-win-x64.exe` | File: `dotnet-sdk-8.0.404-win-x64.exe` |
| ~50 MB download | ~200 MB download (larger!) |

---

## Alternative: Direct Download Link

**Copy and paste this link into your browser:**

### .NET 8 SDK Direct Link:
```
https://download.visualstudio.microsoft.com/download/pr/b5c66c7d-62e6-4b0c-b4b7-c8b93c558b8a/e4c42ac257423f32dfd2e3d8c5d5b1ed/dotnet-sdk-8.0.404-win-x64.exe
```

**File details:**
- Name: `dotnet-sdk-8.0.404-win-x64.exe`
- Size: ~200 MB
- Type: SDK Installer

---

## After Downloading

### 1. Verify the filename
**Before installing, check the filename:**
```
✅ CORRECT: dotnet-sdk-8.0.404-win-x64.exe
           (notice "sdk" in the name)

❌ WRONG:   dotnet-runtime-8.0.15-win-x64.exe
           (this is just the runtime)
```

### 2. Uninstall Current .NET (Optional but Recommended)

To avoid confusion, uninstall what you have:
```
1. Windows Settings
2. Apps → Installed apps
3. Search "dotnet" or ".NET"
4. Uninstall all .NET entries you see
5. Restart computer
```

### 3. Install the SDK
```
1. Run dotnet-sdk-8.0.404-win-x64.exe
2. Follow installer (click Next → Next → Install)
3. Wait for completion
4. Restart computer
```

### 4. Verify Installation

**Open PowerShell and run:**
```powershell
dotnet --list-sdks
```

**You MUST see output like this:**
```
8.0.404 [C:\Program Files\dotnet\sdk]
```

**If you see nothing (blank), the SDK didn't install!**

---

## Troubleshooting

### Still shows no SDKs after installation?

**Check SDK folder exists:**
```powershell
dir "C:\Program Files\dotnet\sdk"
```

**Expected:**
```
Directory of C:\Program Files\dotnet\sdk

8.0.404
```

**If folder doesn't exist:**
- You downloaded the Runtime again (not the SDK)
- Download the file from the direct link above
- Make sure filename contains "sdk"

### Can I use .NET 10 instead of .NET 8?

Yes! .NET 10 SDK works fine (it's backward compatible).

**Direct link for .NET 10 SDK:**
```
https://dotnet.microsoft.com/en-us/download/dotnet/10.0
```

Click the **SDK** download button (not Runtime).

---

## Size Check

**Before installing, verify file size:**

| File Type | Size |
|-----------|------|
| SDK | ~200 MB |
| Runtime | ~50 MB |

**If your download is only 50 MB, you got the Runtime!**

---

## Visual Confirmation

After installation, this folder structure should exist:

```
C:\Program Files\dotnet\
├── dotnet.exe
├── sdk\                    ← This folder MUST exist!
│   └── 8.0.404\           ← SDK version folder
│       ├── dotnet.dll
│       ├── MSBuild.dll
│       └── ... (many files)
├── shared\
│   └── Microsoft.NETCore.App\
└── host\
```

**If `sdk\` folder is missing → SDK not installed!**

---

## Alternative: Use Winget (Package Manager)

If you have Windows 11 or Windows Package Manager:

```powershell
# Install .NET 8 SDK via winget
winget install Microsoft.DotNet.SDK.8

# Verify
dotnet --list-sdks
```

---

## Next Steps After Successful Installation

```powershell
# 1. Verify SDK is there
dotnet --list-sdks

# 2. Navigate to project
cd d:\sorcery+_remake

# 3. Restore packages
dotnet restore

# 4. Build
dotnet build

# 5. Run
dotnet run
```

---

**Current Issue:** SDK folder doesn't exist at `C:\Program Files\dotnet\sdk`

**Solution:** Download the **SDK** installer (200 MB file with "sdk" in filename)

**Direct Link:** https://download.visualstudio.microsoft.com/download/pr/b5c66c7d-62e6-4b0c-b4b7-c8b93c558b8a/e4c42ac257423f32dfd2e3d8c5d5b1ed/dotnet-sdk-8.0.404-win-x64.exe
