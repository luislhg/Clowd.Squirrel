﻿// Parts of this file have been used from
// https://github.com/icsharpcode/ILSpy/blob/f7460a041ea8fb8b0abf8527b97a5b890eb94eea/ICSharpCode.Decompiler/SingleFileBundle.cs

using Microsoft.NET.HostModel.AppHost;
using Microsoft.NET.HostModel.Bundle;
using Squirrel;
using Squirrel.SimpleSplat;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SquirrelCli
{
    internal class SingleFileBundle
    {
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(SingleFileBundle));

        public static async Task UpdateSingleFileIcon(string sourceFile, string destinationFile, string iconPath)
        {
            using var d = Utility.WithTempDirectory(out var tmpdir);
            var hostPath = Path.Combine(tmpdir, "singlefilehost.exe");
            var sourceName = Path.GetFileNameWithoutExtension(sourceFile);

            // extract bundled host to file
            using (var hostStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SquirrelCli.singlefilehost.exe"))
            using (var file = new FileStream(hostPath, FileMode.Create, FileAccess.Write)) {
                hostStream.CopyTo(file);
            }

            // extract Update.exe to tmp dir
            Log.Info("Extracting Update.exe resources to temp directory");
            DumpPackageAssemblies(sourceFile, tmpdir);

            // create new app host
            var newAppHost = Path.Combine(tmpdir, sourceName + ".exe");
            HostWriter.CreateAppHost(
                hostPath, // input file
                newAppHost, // output file
                sourceName + ".dll", // entry point, relative to apphost
                true, // isGui?
                Path.Combine(tmpdir, sourceName + ".dll") // copy exe resources from?
            );
            File.Delete(hostPath);

            // set new icon
            Log.Info("Patching Update.exe icon");
            await HelperExe.SetExeIcon(newAppHost, iconPath);

            // create new bundle
            var bundlerOutput = Path.Combine(tmpdir, "output");
            Directory.CreateDirectory(bundlerOutput);
            var bundler = new Bundler(
                sourceName + ".exe",
                bundlerOutput,
                BundleOptions.EnableCompression,
                OSPlatform.Windows,
                Architecture.X86,
                new Version(6, 0),
                false,
                sourceName
            );

            Log.Info("Re-packing Update.exe bundle");
            var singleFile = SingleFileBundle.GenerateBundle(bundler, tmpdir, bundlerOutput);

            // copy to requested location
            File.Copy(singleFile, destinationFile);
        }

        private static void DumpPackageAssemblies(string packageFileName, string outputDirectory)
        {
            if (!HostWriter.IsBundle(packageFileName, out long bundleHeaderOffset)) {
                throw new InvalidOperationException($"Cannot dump assembiles for {packageFileName}, because it is not a single file bundle.");
            }

            using (var memoryMappedPackage = MemoryMappedFile.CreateFromFile(packageFileName, FileMode.Open, null, 0, MemoryMappedFileAccess.Read)) {
                using (var packageView = memoryMappedPackage.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read)) {
                    var manifest = SingleFileBundle.ReadManifest(packageView, bundleHeaderOffset);
                    foreach (var entry in manifest.Entries) {
                        Stream contents;

                        if (entry.CompressedSize == 0) {
                            contents = new UnmanagedMemoryStream(packageView.SafeMemoryMappedViewHandle, entry.Offset, entry.Size);
                        } else {
                            Stream compressedStream = new UnmanagedMemoryStream(packageView.SafeMemoryMappedViewHandle, entry.Offset, entry.CompressedSize);
                            Stream decompressedStream = new MemoryStream((int) entry.Size);
                            using (var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress)) {
                                deflateStream.CopyTo(decompressedStream);
                            }

                            if (decompressedStream.Length != entry.Size) {
                                throw new Exception($"Corrupted single-file entry '${entry.RelativePath}'. Declared decompressed size '${entry.Size}' is not the same as actual decompressed size '${decompressedStream.Length}'.");
                            }

                            decompressedStream.Seek(0, SeekOrigin.Begin);
                            contents = decompressedStream;
                        }

                        using (var fileStream = File.Create(Path.Combine(outputDirectory, entry.RelativePath))) {
                            contents.CopyTo(fileStream);
                        }
                    }
                }
            }
        }

        private static string GenerateBundle(Bundler bundler, string sourceDir, string outputDir)
        {
            // Convert sourceDir to absolute path
            sourceDir = Path.GetFullPath(sourceDir);

            // Get all files in the source directory and all sub-directories.
            string[] sources = Directory.GetFiles(sourceDir, searchPattern: "*", searchOption: SearchOption.AllDirectories);

            // Sort the file names to keep the bundle construction deterministic.
            Array.Sort(sources, StringComparer.Ordinal);

            List<FileSpec> fileSpecs = new List<FileSpec>(sources.Length);
            foreach (var file in sources) {
                fileSpecs.Add(new FileSpec(file, Path.GetRelativePath(sourceDir, file)));
            }

            return bundler.GenerateBundle(fileSpecs);
        }

        public struct Header
        {
            public uint MajorVersion;
            public uint MinorVersion;
            public int FileCount;
            public string BundleID;

            // Fields introduced with v2:
            public long DepsJsonOffset;
            public long DepsJsonSize;
            public long RuntimeConfigJsonOffset;
            public long RuntimeConfigJsonSize;
            public ulong Flags;

            public ImmutableArray<Entry> Entries;
        }

        /// <summary>
        /// FileType: Identifies the type of file embedded into the bundle.
        ///
        /// The bundler differentiates a few kinds of files via the manifest,
        /// with respect to the way in which they'll be used by the runtime.
        /// </summary>
        public enum FileType : byte
        {
            Unknown,           // Type not determined.
            Assembly,          // IL and R2R Assemblies
            NativeBinary,      // NativeBinaries
            DepsJson,          // .deps.json configuration file
            RuntimeConfigJson, // .runtimeconfig.json configuration file
            Symbols            // PDB Files
        };

        public struct Entry
        {
            public long Offset;
            public long Size;
            public long CompressedSize; // 0 if not compressed, otherwise the compressed size in the bundle
            public FileType Type;
            public string RelativePath; // Path of an embedded file, relative to the Bundle source-directory.
        }

        static UnmanagedMemoryStream AsStream(MemoryMappedViewAccessor view)
        {
            long size = checked((long) view.SafeMemoryMappedViewHandle.ByteLength);
            return new UnmanagedMemoryStream(view.SafeMemoryMappedViewHandle, 0, size);
        }

        public static Header ReadManifest(MemoryMappedViewAccessor view, long bundleHeaderOffset)
        {
            using var stream = AsStream(view);
            stream.Seek(bundleHeaderOffset, SeekOrigin.Begin);
            return ReadManifest(stream);
        }

        public static Header ReadManifest(Stream stream)
        {
            var header = new Header();
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            header.MajorVersion = reader.ReadUInt32();
            header.MinorVersion = reader.ReadUInt32();

            // Major versions 3, 4 and 5 were skipped to align bundle versioning with .NET versioning scheme
            if (header.MajorVersion < 1 || header.MajorVersion > 6) {
                throw new InvalidDataException($"Unsupported manifest version: {header.MajorVersion}.{header.MinorVersion}");
            }
            header.FileCount = reader.ReadInt32();
            header.BundleID = reader.ReadString();
            if (header.MajorVersion >= 2) {
                header.DepsJsonOffset = reader.ReadInt64();
                header.DepsJsonSize = reader.ReadInt64();
                header.RuntimeConfigJsonOffset = reader.ReadInt64();
                header.RuntimeConfigJsonSize = reader.ReadInt64();
                header.Flags = reader.ReadUInt64();
            }
            var entries = ImmutableArray.CreateBuilder<Entry>(header.FileCount);
            for (int i = 0; i < header.FileCount; i++) {
                entries.Add(ReadEntry(reader, header.MajorVersion));
            }
            header.Entries = entries.MoveToImmutable();
            return header;
        }

        private static Entry ReadEntry(BinaryReader reader, uint bundleMajorVersion)
        {
            Entry entry;
            entry.Offset = reader.ReadInt64();
            entry.Size = reader.ReadInt64();
            entry.CompressedSize = bundleMajorVersion >= 6 ? reader.ReadInt64() : 0;
            entry.Type = (FileType) reader.ReadByte();
            entry.RelativePath = reader.ReadString();
            return entry;
        }
    }
}