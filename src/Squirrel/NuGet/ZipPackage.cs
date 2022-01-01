﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using SharpCompress.Archives.Zip;

namespace Squirrel.NuGet
{
    internal interface IPackage
    {
        string Id { get; }
        string Description { get; }
        IEnumerable<string> Authors { get; }
        string Title { get; }
        string Summary { get; }
        string Language { get; }
        string Copyright { get; }
        Uri ProjectUrl { get; }
        string ReleaseNotes { get; }
        Uri IconUrl { get; }
        IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; }
        IEnumerable<PackageDependencySet> DependencySets { get; }
        SemanticVersion Version { get; }
        IEnumerable<string> GetSupportedFrameworks();
        IEnumerable<IPackageFile> GetLibFiles();
        string GetFullName();
    }

    internal class ZipPackage : IPackage
    {
        public string Id { get; private set; }
        public string Description { get; private set; }
        public IEnumerable<string> Authors { get; private set; } = Enumerable.Empty<string>();
        public string Title { get; private set; }
        public string Summary { get; private set; }
        public string Language { get; private set; }
        public string Copyright { get; private set; }
        public SemanticVersion Version { get; private set; }
        public IEnumerable<FrameworkAssemblyReference> FrameworkAssemblies { get; private set; } = Enumerable.Empty<FrameworkAssemblyReference>();
        public IEnumerable<PackageDependencySet> DependencySets { get; private set; } = Enumerable.Empty<PackageDependencySet>();
        public Uri ProjectUrl { get; private set; }
        public string ReleaseNotes { get; private set; }
        public Uri IconUrl { get; private set; }

        private readonly Func<Stream> _streamFactory;
        private static readonly string[] ExcludePaths = new[] { "_rels", "package" };
        private const string ManifestRelationType = "manifest";

        public ZipPackage(string filePath)
        {
            if (String.IsNullOrEmpty(filePath)) {
                throw new ArgumentException("Argument_Cannot_Be_Null_Or_Empty", "filePath");
            }

            _streamFactory = () => File.OpenRead(filePath);
            EnsureManifest();
        }

        public IEnumerable<string> GetSupportedFrameworks()
        {
            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);

            var fileFrameworks = from entries in zip.Entries
                                 let uri = new Uri(entries.Key, UriKind.Relative)
                                 let path = UriUtility.GetPath(uri)
                                 where IsPackageFile(path)
                                 select VersionUtility.ParseFrameworkNameFromFilePath(path, out var effectivePath);

            return FrameworkAssemblies.SelectMany(f => f.SupportedFrameworks)
                       .Concat(fileFrameworks)
                       .Where(f => f != null)
                       .Distinct()
                       .ToArray();
        }

        public IEnumerable<IPackageFile> GetLibFiles()
        {
            return GetFiles(Constants.LibDirectory);
        }

        public IEnumerable<IPackageFile> GetContentFiles()
        {
            return GetFiles(Constants.ContentDirectory);
        }

        public IEnumerable<IPackageFile> GetFiles(string directory)
        {
            string folderPrefix = directory + Path.DirectorySeparatorChar;
            return GetFiles().Where(file => file.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase));
        }

        public List<IPackageFile> GetFiles()
        {
            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);

            var files = from entries in zip.Entries
                        let uri = new Uri(entries.Key, UriKind.Relative)
                        let path = UriUtility.GetPath(uri)
                        where IsPackageFile(path)
                        select (IPackageFile) new ZipPackageFile(path, entries);

            return files.ToList();
        }

        public string GetFullName()
        {
            return Id + " " + Version;
        }

        private void EnsureManifest()
        {
            using var stream = _streamFactory();
            using var zip = ZipArchive.Open(stream);

            var manifest = zip.Entries
                .FirstOrDefault(f => f.Key.EndsWith(Constants.ManifestExtension, StringComparison.OrdinalIgnoreCase));

            if (manifest == null)
                throw new InvalidOperationException("PackageDoesNotContainManifest");

            using var manifestStream = manifest.OpenEntryStream();
            ReadManifest(manifestStream);
        }

        void ReadManifest(Stream manifestStream)
        {
            var document = XmlUtility.LoadSafe(manifestStream, ignoreWhiteSpace: true);

            var metadataElement = document.Root.ElementsNoNamespace("metadata").FirstOrDefault();
            if (metadataElement == null) {
                throw new InvalidDataException(
                    String.Format(CultureInfo.CurrentCulture, "Manifest_RequiredElementMissing", "metadata"));
            }

            var allElements = new HashSet<string>();

            XNode node = metadataElement.FirstNode;
            while (node != null) {
                var element = node as XElement;
                if (element != null) {
                    ReadMetadataValue(element, allElements);
                }
                node = node.NextNode;
            }
        }

        private void ReadMetadataValue(XElement element, HashSet<string> allElements)
        {
            if (element.Value == null) {
                return;
            }

            allElements.Add(element.Name.LocalName);

            string value = element.Value.SafeTrim();
            switch (element.Name.LocalName) {
            case "id":
                Id = value;
                break;
            case "version":
                Version = new SemanticVersion(value);
                break;
            case "authors":
                Authors = value?.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()) ?? Enumerable.Empty<string>();
                break;
            //case "owners":
            //    Owners = value;
            //    break;
            //case "licenseUrl":
            //    LicenseUrl = value;
            //    break;
            case "projectUrl":
                ProjectUrl = new Uri(value);
                break;
            case "iconUrl":
                IconUrl = new Uri(value);
                break;
            //case "requireLicenseAcceptance":
            //    RequireLicenseAcceptance = XmlConvert.ToBoolean(value);
            //    break;
            //case "developmentDependency":
            //    DevelopmentDependency = XmlConvert.ToBoolean(value);
            //    break;
            case "description":
                Description = value;
                break;
            case "summary":
                Summary = value;
                break;
            case "releaseNotes":
                ReleaseNotes = value;
                break;
            case "copyright":
                Copyright = value;
                break;
            case "language":
                Language = value;
                break;
            case "title":
                Title = value;
                break;
            //case "tags":
            //    Tags = value;
            //    break;
            case "dependencies":
                DependencySets = ReadDependencySets(element);
                break;
            case "frameworkAssemblies":
                FrameworkAssemblies = ReadFrameworkAssemblies(element);
                break;
                //case "references":
                //    ReferenceSets = ReadReferenceSets(element);
                //    break;
            }
        }

        private List<FrameworkAssemblyReference> ReadFrameworkAssemblies(XElement frameworkElement)
        {
            if (!frameworkElement.HasElements) {
                return new List<FrameworkAssemblyReference>(0);
            }

            return (from element in frameworkElement.ElementsNoNamespace("frameworkAssembly")
                    let assemblyNameAttribute = element.Attribute("assemblyName")
                    where assemblyNameAttribute != null && !String.IsNullOrEmpty(assemblyNameAttribute.Value)
                    select new FrameworkAssemblyReference(
                        assemblyNameAttribute.Value.SafeTrim(),
                        ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim()))
                    ).ToList();
        }

        private List<PackageDependencySet> ReadDependencySets(XElement dependenciesElement)
        {
            if (!dependenciesElement.HasElements) {
                return new List<PackageDependencySet>();
            }

            // Disallow the <dependencies> element to contain both <dependency> and 
            // <group> child elements. Unfortunately, this cannot be enforced by XSD.
            if (dependenciesElement.ElementsNoNamespace("dependency").Any() &&
                dependenciesElement.ElementsNoNamespace("group").Any()) {
                throw new InvalidDataException("Manifest_DependenciesHasMixedElements");
            }

            var dependencies = ReadDependencies(dependenciesElement);
            if (dependencies.Count > 0) {
                // old format, <dependency> is direct child of <dependencies>
                var dependencySet = new PackageDependencySet(null, dependencies);
                return new List<PackageDependencySet> { dependencySet };
            } else {
                var groups = dependenciesElement.ElementsNoNamespace("group");
                return (from element in groups
                        let fx = ParseFrameworkNames(element.GetOptionalAttributeValue("targetFramework").SafeTrim())
                        select new PackageDependencySet(
                            VersionUtility.ParseFrameworkName(element.GetOptionalAttributeValue("targetFramework").SafeTrim()),
                            ReadDependencies(element))).ToList();
            }
        }

        private List<PackageDependency> ReadDependencies(XElement containerElement)
        {
            // element is <dependency>
            return (from element in containerElement.ElementsNoNamespace("dependency")
                    let idElement = element.Attribute("id")
                    where idElement != null && !String.IsNullOrEmpty(idElement.Value)
                    select new PackageDependency(
                        idElement.Value.SafeTrim(),
                        VersionUtility.ParseVersionSpec(element.GetOptionalAttributeValue("version").SafeTrim())
                    )).ToList();
        }

        private IEnumerable<string> ParseFrameworkNames(string frameworkNames)
        {
            if (String.IsNullOrEmpty(frameworkNames)) {
                return Enumerable.Empty<string>();
            }

            return frameworkNames.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(VersionUtility.ParseFrameworkName);
        }

        bool IsPackageFile(string partPath)
        {
            if (Path.GetFileName(partPath).Equals(ContentType.ContentTypeFileName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (Path.GetExtension(partPath).Equals(Constants.ManifestExtension, StringComparison.OrdinalIgnoreCase))
                return false;

            string directory = Path.GetDirectoryName(partPath);
            return !ExcludePaths.Any(p => directory.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
