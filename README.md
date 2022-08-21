[![Nuget](https://img.shields.io/nuget/v/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)
[![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/Clowd.Squirrel?style=flat-square)](https://www.nuget.org/packages/Clowd.Squirrel/)
[![Discord](https://img.shields.io/discord/767856501477343282?style=flat-square&color=purple)](https://discord.gg/CjrCrNzd3F)
[![Build](https://img.shields.io/github/workflow/status/clowd/Clowd.Squirrel/Build%20Squirrel/develop?style=flat-square)](https://github.com/clowd/Clowd.Squirrel/actions)

# Clowd.Squirrel

Squirrel is both a set of tools and a dotnet library to completely manage installation and updating any desktop application. Now with cross-platform support!

###### Migrating from Squirrel.Windows

This project is originally a fork of the windows-only library [Squirrel.Windows](https://github.com/Squirrel/Squirrel.Windows). This project is now cross-platform and things here have now diverged. It is possible to easily migrate applications, but there are some important differences. See the [Squirrel.Windows to Clowd.Squirrel migration guide](docs/migrate-from-squirrel-windows.md) for more info.

###### Join our Community

If you are interested in staying up to date, looking to contribute, or need help/support, you can [join us on Discord](https://discord.gg/CjrCrNzd3F). If this project was useful to you, [donations are always appreciated](https://github.com/sponsors/caesay) as they allow me to spend more time improving Squirrel.



## What Do We Want?

Apps should be as fast easy to install. Update should be seamless like Google Chrome. From a developer's side, it should be really straightforward to create an installer for my app, and publish updates to it, without having to jump through insane hoops. 

* **Integrating** an app to use Squirrel should be extremely easy, provide a client API, and be developer friendly.
* **Packaging** is really easy, can be automated, and supports **delta updates**.
* **Distributing** should be straightforward, use simple HTTP updates, and provide multiple "channels" (a-la Chrome Dev/Beta/Release).
* **Installing** is Wizard-Free™, with no UAC dialogs, does not require reboot, and is .NET Framework friendly.
* **Updating** is in the background, doesn't interrupt the user, and does not require a reboot.



## Getting Started

There are two supported versions of Squirrel. You will need to choose which version is right for your project.

### v3.0 (`develop` branch)

This is the version being actively developed. New features are currently being developed here, and API's are subject to change between minor versions. It is cross-platform on Windows and MacOS. It is possible to upgrade from v2.9 to v3.0, but it is not possible to roll back to an earlier version of Squirrel without re-installing your application.

- Get started with v3.0 for Windows
- Get started with v3.0 for MacOS
- Get started with v3.0 for cross-platform AvaloniaUI applications

### v2.9 (`master` branch)

This is Windows-only and it's structure closely resembles Squirrel.Windows. This version has a stable API, it will not receive any significant new features, but will receive important / critical fixes.

- [Get started with v2.9 for Windows](docs/quickstart-v2.md)



## Building Squirrel
For the impatient:

```cmd
git clone https://github.com/clowd/Clowd.Squirrel
cd Clowd.Squirrel
build.cmd
```

Two `.nupkg` files will be produced in the `./build/Release` directory.



See [Contributing](docs/contributing/contributing.md) for additional information on building and contributing to Squirrel.



## License and Usage

See [LICENSE](LICENSE) for details on copyright and usage.
