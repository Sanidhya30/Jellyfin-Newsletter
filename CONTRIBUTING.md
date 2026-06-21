# Contributing to Jellyfin Newsletter Plugin

Thank you for your interest in contributing to the Jellyfin Newsletter Plugin! We welcome all contributions, including bug reports, feature requests, and pull requests.

## How to Build Locally

To build the plugin on your local machine, please follow these steps:

### Prerequisites
1. **.NET SDK**: Ensure you have the .NET 9.0 SDK installed (or the version currently specified in the `.csproj`).
2. **SixLabors License**: This project uses `SixLabors.ImageSharp` version 4.0.0, which requires a valid license key file (`sixlabors.lic`) to compile.

### Setting up the License
1. Obtain a `sixlabors.lic` license file. If you are an open-source contributor or meet the criteria, you can request a **free Community License** at [https://sixlabors.com/pricing/](https://sixlabors.com/pricing/).
2. Place the `sixlabors.lic` file inside the `Jellyfin.Plugin.Newsletters/` folder.
3. The `.gitignore` file is already configured to ignore `.lic` files, so you won't accidentally commit your private license key.

> **Note:**
> If you do not have or do not wish to use a `sixlabors.lic` file, you can temporarily downgrade `SixLabors.ImageSharp` in `Jellyfin.Plugin.Newsletters.csproj` to version `3.1.12` to build the plugin locally without a license. Just make sure not to include this downgrade in your pull request.

### Building the Project
1. Clone the repository:
   ```bash
   git clone https://github.com/Sanidhya30/Jellyfin-Newsletter.git
   cd Jellyfin-Newsletter
   ```
2. Build the project using the .NET CLI:
   ```bash
   dotnet build
   ```
3. The compiled plugin `.dll` will be available in the `Jellyfin.Plugin.Newsletters/bin/Debug/net9.0/` directory.

## Submitting Pull Requests
- All Pull Requests should be made against the `development` branch.
- Please clearly note what was added, changed, or fixed in your PR description.
- Ensure your code compiles successfully and follows the project's formatting conventions.

Thank you for contributing!
