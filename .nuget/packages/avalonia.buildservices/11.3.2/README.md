# Avalonia Telemetry

## Overview
Avalonia collects anonymous build-time usage data to understand how the framework is used and to focus development where it delivers the most value. This information guides our priorities, ensures we invest in the right features, and helps maintain a stable, performant ecosystem.

## Data Collection
When is data collected?
* Build time only – telemetry runs exclusively during compilation
* No runtime impact – deployed applications are unaffected

### What is collected?
Minimal, anonymised information (hashed with SHA256 where applicable):
* Build timestamp
* Project name (hashed)
* Output type, target framework, runtime identifier
* Avalonia version in use
* Accelerate license tier (indie, business, enterprise...)
* Anonymous machine identifier (GUID), and machine name (hashed)
* Development environment (Visual Studio, Rider, VS Code, CLI)
* Operating system version and architecture
* CI environment detection (Azure DevOps, GitHub Actions, etc.)

### What is not collected?
* Source code or project contents
* Personal information
* File paths or directory structures
* Runtime application data or network requests
* Email addresses, names, or account information

## Privacy & Security
* **Anonymous**: No personally identifiable information is collected
* **Hashed**: Sensitive values are SHA256 hashed
* **EU Storage**: Data is stored in Western Europe
* **Build-time only**: No collection in deployed apps
* **GDPR compliant**: Processed under legitimate interest with full compliance measures

### Opting Out
Set the environment variable:
`AVALONIA_TELEMETRY_OPTOUT=1`

#### Before opting out, please consider:
This data is our only reliable way to track active versions, platforms, and development environments. It lets us anticipate ecosystem changes and prioritise fixes and features that benefit everyone.

If you choose to disable telemetry, we welcome support through other means, for example, an [Avalonia Accelerate](https://avaloniaui.net/accelerate#pricing) licence, [donations](https://github.com/sponsors/AvaloniaUI), or contributions to code, documentation, or testing. Open source thrives on fairness, and we see anonymous telemetry as a reasonable exchange for a free, high-quality framework.

For full details, see our [Privacy Policy](https://avaloniaui.net/legal-center/privacy-policy).

## Why Open Source?
Transparency matters. By publishing the telemetry code, you can:
* Audit exactly what we collect
* Verify our privacy statements
* Understand the system’s implementation

## Industry Context
Anonymous build-time telemetry is standard across major .NET frameworks and tools, including the .NET SDK itself, Uno Platform, and WinUI. It is a proven, low-impact way to guide development effectively.

## Contributing
You are welcome to raise issues, suggestions, or security concerns. Pull requests will not be accepted. 

Licence
MIT Licence. 
