﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Mono.Options;
using Squirrel;
using Squirrel.Json;
using Squirrel.Lib;
using Squirrel.NuGet;
using Squirrel.Shared;
using Squirrel.SimpleSplat;
using SquirrelCli.Sources;

namespace SquirrelCli
{
    class Program : IEnableLogger
    {
#pragma warning disable CS0436 // Type conflicts with imported type
        public static string DisplayVersion => ThisAssembly.AssemblyInformationalVersion;
        public static string FileVersion => ThisAssembly.AssemblyFileVersion;
#pragma warning restore CS0436 // Type conflicts with imported type

        public static int Main(string[] args)
        {
            var logger = new ConsoleLogger();
            SquirrelLocator.CurrentMutable.Register(() => logger, typeof(ILogger));

            bool help = false;
            bool verbose = false;
            var globalOptions = new OptionSet() {
                { "h|?|help", "Ignores all other arguments and shows help text", _ => help = true },
                { "verbose", "Print extra diagnostic logging", _ => verbose = true },
            };

            var exeName = Path.GetFileName(AssemblyRuntimeInfo.EntryExePath);
            string sqUsage =
                $"Squirrel {DisplayVersion}, tool for creating and deploying Squirrel releases" + Environment.NewLine +
                $"Usage: {exeName} [verb] [--option:value]";

            var commands = new CommandSet {
                "",
                sqUsage,
                "",
                "[ Global Options ]",
                globalOptions.GetHelpText().TrimEnd(),
                "",
                "[ Package Authoring ]",
                { "pack", "Creates a Squirrel release from a folder containing application files", new PackOptions(), Pack },
                { "releasify", "Take an existing nuget package and convert it into a Squirrel release", new ReleasifyOptions(), Releasify },
                "",
                "[ Package Deployment / Syncing ]",
                { "b2-down", "Download recent releases from BackBlaze B2", new SyncBackblazeOptions(), o => Download(new BackblazeRepository(o)) },
                { "b2-up", "Upload releases to BackBlaze B2", new SyncBackblazeOptions(), o => Upload(new BackblazeRepository(o)) },
                { "http-down", "Download recent releases from an HTTP source", new SyncHttpOptions(), o => Download(new SimpleWebRepository(o)) },
                { "github-down", "Download recent releases from GitHub", new SyncGithubOptions(), o => Download(new GitHubRepository(o)) },
                { "s3-down", "Download recent releases from a S3 bucket", new SyncS3Options(), o => Download(new S3Repository(o)) },
                { "s3-up", "Upload recent releases to a S3 bucket", new SyncS3Options(), o => Upload(new S3Repository(o)) },
                //"",
                //"[ Examples ]",
                //$"    {exeName} pack ",
                //$"        ",
            };

            try {
                globalOptions.Parse(args);

                if (verbose) {
                    logger.Level = LogLevel.Debug;
                }

                if (help) {
                    commands.WriteHelp();
                    return 0;
                } else {
                    // parse cli and run command
                    commands.Execute(args);
                }

                return 0;
            } catch (Exception ex) when (ex is OptionValidationException || ex is OptionException) {
                // if the arguments fail to validate, print argument help
                Console.WriteLine();
                logger.Write(ex.Message, LogLevel.Error);
                commands.WriteHelp();
                Console.WriteLine();
                logger.Write(ex.Message, LogLevel.Error);
                return -1;
            } catch (Exception ex) {
                // for other errors, just print the error and short usage instructions
                Console.WriteLine();
                logger.Write(ex.ToString(), LogLevel.Error);
                Console.WriteLine();
                Console.WriteLine(sqUsage);
                Console.WriteLine($" > '{exeName} -h' to see program help.");
                return -1;
            }
        }

        static IFullLogger Log => SquirrelLocator.Current.GetService<ILogManager>().GetLogger(typeof(Program));

        static void Upload<T>(T repo) where T : IPackageRepository => repo.UploadMissingPackages().Wait();

        static void Download<T>(T repo) where T : IPackageRepository => repo.DownloadRecentPackages().Wait();

        static void Pack(PackOptions options)
        {
            var releaseNotesText = String.IsNullOrEmpty(options.releaseNotes)
                ? "" // no releaseNotes
                : $"<releaseNotes>{SecurityElement.Escape(File.ReadAllText(options.releaseNotes))}</releaseNotes>";

            using (Utility.WithTempDirectory(out var tmpDir)) {
                string nuspec = $@"
<?xml version=""1.0"" encoding=""utf-8""?>
<package>
  <metadata>
    <id>{options.packId}</id>
    <title>{options.packTitle ?? options.packId}</title>
    <description>{options.packTitle ?? options.packId}</description>
    <authors>{options.packAuthors ?? options.packId}</authors>
    <version>{options.packVersion}</version>
    {releaseNotesText}
  </metadata>
  <files>
    <file src=""**"" target=""lib\native\"" exclude=""{(options.includePdb ? "" : "*.pdb;")}*.nupkg;*.vshost.*""/>
  </files>
</package>
".Trim();
                var nuspecPath = Path.Combine(tmpDir, options.packId + ".nuspec");
                File.WriteAllText(nuspecPath, nuspec);

                new NugetConsole().Pack(nuspecPath, options.packDirectory, tmpDir);

                var nupkgPath = Directory.EnumerateFiles(tmpDir).Where(f => f.EndsWith(".nupkg")).FirstOrDefault();
                if (nupkgPath == null)
                    throw new Exception($"Failed to generate nupkg, unspecified error");

                options.package = nupkgPath;
                Releasify(options);
            }
        }

        static void Releasify(ReleasifyOptions options)
        {
            var targetDir = options.releaseDir ?? Path.Combine(".", "Releases");
            if (!Directory.Exists(targetDir)) {
                Directory.CreateDirectory(targetDir);
            }

            var package = options.package;
            var baseUrl = options.baseUrl;
            var generateDeltas = !options.noDelta;
            var backgroundGif = options.splashImage;
            var setupIcon = options.icon ?? options.appIcon;

            if (!package.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("package must be packed with nuget and end in '.nupkg'");

            // normalize and validate that the provided frameworks are supported 
            var requiredFrameworks = Runtimes.ParseDependencyString(options.framework);
            if (requiredFrameworks.Any())
                Log.Info("Package dependencies (from '--framework' argument) resolved as: " + String.Join(", ", requiredFrameworks.Select(r => r.Id)));

            using var ud = Utility.WithTempDirectory(out var tempDir);

            // update icon for Update.exe if requested
            var bundledUpdatePath = HelperExe.UpdatePath(p => Microsoft.NET.HostModel.AppHost.HostWriter.IsBundle(p, out var _hz));
            var updatePath = Path.Combine(tempDir, "Update.exe");
            if (setupIcon != null) {
                DotnetUtil.UpdateSingleFileBundleIcon(bundledUpdatePath, updatePath, setupIcon).Wait();
            } else {
                File.Copy(bundledUpdatePath, updatePath, true);
            }

            if (!DotnetUtil.IsSingleFileBundle(updatePath))
                throw new InvalidOperationException("Update.exe is corrupt. Broken Squirrel install?");

            // Sign Update.exe so that virus scanners don't think we're pulling one over on them
            options.SignPEFile(updatePath);

            // copy input package to target output directory
            var di = new DirectoryInfo(targetDir);
            File.Copy(package, Path.Combine(di.FullName, Path.GetFileName(package)), true);

            var allNuGetFiles = di.EnumerateFiles()
                .Where(x => x.Name.EndsWith(".nupkg", StringComparison.InvariantCultureIgnoreCase));

            var toProcess = allNuGetFiles.Where(x => !x.Name.Contains("-delta") && !x.Name.Contains("-full"));
            var processed = new List<string>();

            var releaseFilePath = Path.Combine(di.FullName, "RELEASES");
            var previousReleases = new List<ReleaseEntry>();
            if (File.Exists(releaseFilePath)) {
                previousReleases.AddRange(ReleaseEntry.ParseReleaseFile(File.ReadAllText(releaseFilePath, Encoding.UTF8)));
            }

            foreach (var file in toProcess) {
                Log.Info("Creating release for package: " + file.FullName);

                var rp = new ReleasePackage(file.FullName);
                rp.CreateReleasePackage(Path.Combine(di.FullName, rp.SuggestedReleaseFileName), contentsPostProcessHook: (pkgPath, zpkg) => {
                    var nuspecPath = Directory.GetFiles(pkgPath, "*.nuspec", SearchOption.TopDirectoryOnly)
                        .ContextualSingle("package", "*.nuspec", "top level directory");
                    var libDir = Directory.GetDirectories(Path.Combine(pkgPath, "lib"))
                        .ContextualSingle("package", "'lib' folder");

                    var awareExes = SquirrelAwareExecutableDetector.GetAllSquirrelAwareApps(libDir);

                    // unless the validation has been disabled, do not allow the creation of packages without a SquirrelAwareApp inside
                    if (!options.allowUnaware && !awareExes.Any()) {
                        throw new ArgumentException(
                            "There are no SquirreAwareApp's in the provided package. Please mark an exe " +
                            "as aware using the assembly manifest, or use the '--allowUnaware' argument " +
                            "to skip this validation and create a package anyway (not recommended).");
                    }

                    // fail the release if this is a clickonce application
                    if (Directory.EnumerateFiles(libDir, "*.application").Any(f => File.ReadAllText(f).Contains("clickonce"))) {
                        throw new ArgumentException(
                            "Squirrel does not support building releases for ClickOnce applications. " +
                            "Please publish your application to a folder without ClickOnce.");
                    }

                    // record architecture of squirrel aware binaries so setup can fast fail if unsupported
                    var peparsed = awareExes.ToDictionary(path => path, path => new PeNet.PeFile(path));
                    var pearchs = peparsed
                        .Select(pe => pe.Value?.ImageNtHeaders?.FileHeader?.Machine ?? 0)
                        .Select(machine => { Utility.TryParseEnumU16<RuntimeCpu>((ushort) machine, out var cpu); return cpu; })
                        .Where(m => m != RuntimeCpu.Unknown)
                        .Distinct()
                        .ToArray();

                    if (pearchs.Length > 1) {
                        Log.Warn("Multiple squirrel aware binaries were detected with different machine architectures: " + String.Join(", ", pearchs));
                    }

                    // check dependencies for squirrel aware binaries 
                    peparsed.ForEach(kvp => DotnetUtil.CheckDotnetReferences(kvp.Key, kvp.Value, requiredFrameworks));

                    // CS: the first will be selected for the "package" architecture. this order is important,
                    // because of emulation support. Arm64 generally supports x86/x64 emulation, and x64
                    // often supports x86 emulation, so we want to pick the least compatible architecture
                    // for the package.
                    var archOrder = new[] { /*RuntimeCpu.Arm64,*/ RuntimeCpu.X64, RuntimeCpu.X86 };
                    var pkgarch = archOrder.FirstOrDefault(o => pearchs.Contains(o));

                    if (pkgarch == RuntimeCpu.Unknown) {
                        Log.Warn("Unable to detect package machine architecture from SquirrelAware binaries.");
                    } else {
                        Log.Info($"Package architecture: {pkgarch} (implicit, from a SquirrelAware binary)");
                    }

                    ZipPackage.SetSquirrelMetadata(nuspecPath, pkgarch, requiredFrameworks.Select(r => r.Id));

                    // create stub executable for all exe's in this package (except Squirrel!)
                    Log.Info("Creating stub executables");
                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => x.Name.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => !x.Name.Contains("squirrel.exe", StringComparison.InvariantCultureIgnoreCase))
                        .Where(x => Utility.IsFileTopLevelInPackage(x.FullName, pkgPath))
                        .Where(x => Utility.ExecutableUsesWin32Subsystem(x.FullName))
                        .ToArray() // materialize the IEnumerable so we never end up creating stubs for stubs
                        .ForEach(x => createExecutableStubForExe(x.FullName));

                    // sign all exe's in this package
                    new DirectoryInfo(pkgPath).GetAllFilesRecursively()
                        .Where(x => Utility.FileIsLikelyPEImage(x.Name))
                        .ForEachAsync(x => options.SignPEFile(x.FullName))
                        .Wait();

                    // copy Update.exe into package, so it can also be updated in both full/delta packages
                    File.Copy(updatePath, Path.Combine(libDir, "Squirrel.exe"), true);

                    // copy app icon to 'lib/fx/app.ico'
                    var iconTarget = Path.Combine(libDir, "app.ico");
                    if (options.appIcon != null) {

                        // icon was specified on the command line
                        Log.Info("Using app icon from command line arguments");
                        File.Copy(options.appIcon, iconTarget, true);

                    } else if (!File.Exists(iconTarget) && zpkg.IconUrl != null) {

                        // icon was provided in the nuspec. download it and possibly convert it from a different image format
                        Log.Info($"Downloading app icon from '{zpkg.IconUrl}'.");
                        var fd = Utility.CreateDefaultDownloader();
                        var imgBytes = fd.DownloadBytes(zpkg.IconUrl.ToString()).Result;
                        if (zpkg.IconUrl.AbsolutePath.EndsWith(".ico")) {
                            File.WriteAllBytes(iconTarget, imgBytes);
                        } else {
                            using var imgStream = new MemoryStream(imgBytes);
                            using var bmp = (Bitmap) Image.FromStream(imgStream);
                            using var ico = Icon.FromHandle(bmp.GetHicon());
                            using var fs = File.Open(iconTarget, FileMode.Create, FileAccess.Write);
                            ico.Save(fs);
                        }
                    }

                    // copy other images to root (used by setup)
                    if (setupIcon != null) File.Copy(setupIcon, Path.Combine(pkgPath, "setup.ico"), true);
                    if (backgroundGif != null) File.Copy(backgroundGif, Path.Combine(pkgPath, "splashimage" + Path.GetExtension(backgroundGif)));
                });

                processed.Add(rp.ReleasePackageFile);

                var prev = ReleaseEntry.GetPreviousRelease(previousReleases, rp, targetDir);
                if (prev != null && generateDeltas) {
                    var deltaBuilder = new DeltaPackageBuilder();
                    var dp = deltaBuilder.CreateDeltaPackage(prev, rp,
                        Path.Combine(di.FullName, rp.SuggestedReleaseFileName.Replace("full", "delta")));
                    processed.Insert(0, dp.InputPackageFile);
                }
            }

            foreach (var file in toProcess) {
                File.Delete(file.FullName);
            }

            var newReleaseEntries = processed
                .Select(packageFilename => ReleaseEntry.GenerateFromFile(packageFilename, baseUrl))
                .ToList();
            var distinctPreviousReleases = previousReleases
                .Where(x => !newReleaseEntries.Select(e => e.Version).Contains(x.Version));
            var releaseEntries = distinctPreviousReleases.Concat(newReleaseEntries).ToList();

            ReleaseEntry.WriteReleaseFile(releaseEntries, releaseFilePath);

            var bundledzp = new ZipPackage(package);
            var targetSetupExe = Path.Combine(di.FullName, $"{bundledzp.Id}Setup.exe");
            File.Copy(HelperExe.SetupPath, targetSetupExe, true);
            Utility.Retry(() => HelperExe.SetPEVersionBlockFromPackageInfo(targetSetupExe, bundledzp, setupIcon).Wait());

            var newestFullRelease = Squirrel.EnumerableExtensions.MaxBy(releaseEntries, x => x.Version).Where(x => !x.IsDelta).First();
            var newestReleasePath = Path.Combine(di.FullName, newestFullRelease.Filename);

            Log.Info($"Creating Setup bundle");
            SetupBundle.CreatePackageBundle(targetSetupExe, newestReleasePath);
            options.SignPEFile(targetSetupExe);

            if (!String.IsNullOrEmpty(options.msi)) {
                bool x64 = options.msi.Equals("x64");
                var msiPath = createMsiPackage(targetSetupExe, new ZipPackage(package), x64).Result;
                options.SignPEFile(msiPath);
            }

            Log.Info("Done");
        }

        static async Task<string> createMsiPackage(string setupExe, IPackage package, bool packageAs64Bit)
        {
            Log.Info($"Compiling machine-wide msi deployment tool in {(packageAs64Bit ? "64-bit" : "32-bit")} mode");

            var setupExeDir = Path.GetDirectoryName(setupExe);
            var setupName = Path.GetFileNameWithoutExtension(setupExe);
            var culture = CultureInfo.GetCultureInfo(package.Language ?? "").TextInfo.ANSICodePage;

            var templateText = File.ReadAllText(HelperExe.WixTemplatePath);
            var templateData = new Dictionary<string, string> {
                { "Id", package.Id },
                { "Title", package.ProductName },
                { "Author", package.ProductCompany },
                { "Version", Regex.Replace(package.Version.ToString(), @"-.*$", "") },
                { "Summary", package.ProductDescription },
                { "Codepage", $"{culture}" },
                { "Platform", packageAs64Bit ? "x64" : "x86" },
                { "ProgramFilesFolder", packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFilesFolder" },
                { "Win64YesNo", packageAs64Bit ? "yes" : "no" },
                { "SetupName", setupName }
            };

            // NB: We need some GUIDs that are based on the package ID, but unique (i.e.
            // "Unique but consistent").
            for (int i = 1; i <= 10; i++) {
                templateData[String.Format("IdAsGuid{0}", i)] = Utility.CreateGuidFromHash(String.Format("{0}:{1}", package.Id, i)).ToString();
            }

            var templateResult = CopStache.Render(templateText, templateData);

            var wxsTarget = Path.Combine(setupExeDir, setupName + ".wxs");
            File.WriteAllText(wxsTarget, templateResult, Encoding.UTF8);

            try {
                var msiTarget = Path.Combine(setupExeDir, setupName + "_DeploymentTool.msi");
                await HelperExe.CompileWixTemplateToMsi(wxsTarget, msiTarget);
                return msiTarget;
            } finally {
                File.Delete(wxsTarget);
            }
        }

        static void createExecutableStubForExe(string exeToCopy)
        {
            try {
                var target = Path.Combine(
                    Path.GetDirectoryName(exeToCopy),
                    Path.GetFileNameWithoutExtension(exeToCopy) + "_ExecutionStub.exe");

                Utility.Retry(() => File.Copy(HelperExe.StubExecutablePath, target, true));

                Utility.Retry(() => {
                    using var writer = new Microsoft.NET.HostModel.ResourceUpdater(target, true);
                    writer.AddResourcesFromPEImage(exeToCopy);
                    writer.Update();
                });
            } catch (Exception ex) {
                Log.ErrorException($"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.", ex);
            }
        }
    }

    class ConsoleLogger : ILogger
    {
        public LogLevel Level { get; set; } = LogLevel.Info;

        private readonly object gate = new object();

        public void Write(string message, LogLevel logLevel)
        {
            if (logLevel < Level) {
                return;
            }

            lock (gate) {
                string lvl = logLevel.ToString().Substring(0, 4).ToUpper();
                if (logLevel == LogLevel.Error || logLevel == LogLevel.Fatal) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}{Environment.NewLine}", ConsoleColor.Red);
                } else if (logLevel == LogLevel.Warn) {
                    Utility.ConsoleWriteWithColor($"[{lvl}] {message}{Environment.NewLine}", ConsoleColor.Yellow);
                } else {
                    Console.WriteLine($"[{lvl}] {message}");
                }
            }
        }
    }
}
