## Migrating from Squirrel.Windows?

 - The command line interface for Squirrel.exe is different. Check 'Squirrel.exe -h' for more info.
 - The command line for Update.exe here is compatible with the old Squirrel.
 - Update.exe here is bigger and is included in your packages. This means Update.exe will be updated each time you update your app. As long as you build delta packages, this will not impact the size of your updates.
 - Migrating to this library is fully compatible, except for the way we detect SquirrelAware binaries. Follow the Quick Start guide.
 - There have been a great many other improvements here. To see some of them [have a look at the feature matrix](#feature-matrix).
 - Something detected as a virus? This was an issue at the old Squirrel, and also see [issue #28](https://github.com/clowd/Clowd.Squirrel/issues/28)





## Feature Matrix

| Feature                                                      | Clowd.Squirrel                               | Squirrel.Windows                                             |
| ------------------------------------------------------------ | -------------------------------------------- | ------------------------------------------------------------ |
| Continuous updates, bug fixes, and other improvements        | ✅                                            | ❌                                                            |
| Provides a command line update interface (Update.exe) with your app | ✅                                            | ✅                                                            |
| Update.exe Size                                              | ❌ 12.5mb                                     | ✅ 2mb                                                        |
| Provides a C# SDK                                            | netstandard2.0<br>net461<br>net5.0<br>net6.0 | netstandard2.0                                               |
| SDK has 100% XML comment coverage in Nuget Pacakge           | ✅                                            | None, does not ship comments in NuGet                        |
| SDK Dependencies                                             | SharpCompress                                | SharpCompress (outdated & security vulnerability)<br>NuGet (outdated and bugs)<br>Mono.Cecil (outdated and bugs)<br>Microsoft.Web.Xdt<br>Microsoft.CSharp<br>Microsoft.Win32.Registry<br>System.Drawing.Common<br>System.Net.Http<br>System.Web |
| SDK is strong-name signed                                    | ✅                                            | ❌                                                            |
| Provides an update package builder (Squirrel.exe)            | ✅                                            | ✅                                                            |
| Supports building tiny delta updates                         | ✅                                            | ✅                                                            |
| Can compile a release/setup in a single easy command         | ✅                                            | ❌                                                            |
| Command line tool for package building that actually prints helpful messages to the console | ✅                                            | ❌                                                            |
| CLI help text that is command-based and easily understandable | ✅                                            | ❌                                                            |
| Supports building packages for native apps                   | ✅                                            | ✅                                                            |
| Supports building packages for .Net/Core                     | ✅                                            | Limited/Buggy                                                |
| Supports building packages for PublishSingleFile apps        | ✅                                            | ❌                                                            |
| Supports fully automated CI package deployments easily       | ✅                                            | ❌                                                            |
| Compiles an installer (Setup.exe)                            | ✅                                            | ✅                                                            |
| Setup Splash Gif                                             | ✅                                            | ✅                                                            |
| Setup Splash Png,Jpeg,Tiff,Etc                               | ✅                                            | ❌                                                            |
| Setup Splash Progress Bar                                    | ✅                                            | ❌                                                            |
| Setup Splash has Multi-Monitor DPI support                   | ✅                                            | ❌                                                            |
| No internal dependencies on external frameworks/runtimes     | ✅                                            | ❌                                                            |
| Can deploy an application that has no dependencies           | ✅                                            | ❌ (always installs .Net Framework with your app)             |
| Can install .Net Full Framework during setup                 | ✅                                            | ✅                                                            |
| Can install .Net/Core during setup                           | ✅                                            | ❌                                                            |
| Can install vcredist during setup                            | ✅                                            | ❌                                                            |
| Can install new runtimes (see above) during updates          | ✅                                            | ❌                                                            |
| Cleans up after itself                                       | ✅                                            | Leaves huge log files everywhere<br>Does not delete itself during uninstall |
| Can build an MSI enterprise machine-wide deployment tool     | ✅                                            | ✅                                                            |

