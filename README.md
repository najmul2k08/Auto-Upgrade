# Magnific Auto Upgrade Suite

High-performance C# (.NET 8 WPF) desktop application designed for automated batch upgrading of Magnific accounts using rotating HTTP proxies.

---

## 🚀 Key Features

- **Wise Design System**: Modern Scandinavian UI with pale sage canvas (`#E8EBE6`), rounded white cards, and signature Wise lime (`#9FE870`) accents.
- **Full Dark Mode**: Seamless toggle between light mode and a deep midnight charcoal dark theme (`#12130F`).
- **100% Vector Icons**: Custom SVG vector path geometries throughout the entire UI (zero emojis).
- **Multi-Tab Architecture**:
  - **Upgrade Runner**: Drag-and-drop `.txt` token files, configure concurrency (1–50 threads), paste/load proxies, and monitor a live activity terminal.
  - **Results History**: Persistent transaction archive (`results/history.json`), daily text logs (`approved_YYYY-MM-DD.txt`), status filters, and CSV/TXT exports.
  - **Settings**: Persistent configuration dashboard for thread defaults, timeouts, custom user-agents, auto-save options, and theme mode.
- **Mandatory Rotating Proxies**: Strict proxy enforcement with round-robin rotation across worker threads.
- **Token Extraction Engine**: Automatically extracts `GR_TOKEN` and `GR_REFRESH` from cookie strings, key-value headers, or JSON cookie arrays.

---

## 🛠️ Tech Stack & Requirements

- **Platform**: Windows 10/11 x64
- **Framework**: .NET 8.0 Windows Desktop (`net8.0-windows`)
- **Language**: C# 12 / WPF

---

## 📦 Building & Publishing

### Build
```bash
dotnet build -c Release AutoUpgrade.csproj
```

### Publish Single-File Executable
```bash
dotnet publish AutoUpgrade.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The compiled standalone executable will be located in the `publish/` directory.

---

## 🔒 Security
- All sensitive tokens and local logs are excluded from source control via `.gitignore`.
- Direct execution without proxies is blocked to prevent exposing local IP addresses.
