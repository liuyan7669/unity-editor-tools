using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

internal enum SourceCodeExtractMode
{
    MergeToText,
    CopyDirectory
}

internal sealed class SourceCodeExtractOptions
{
    public string SourceDirectory;
    public string OutputDirectory;
    public string Extensions;
    public string ExcludedDirectoryNames;
    public bool IncludeMetaFiles;
    public List<SourceCodeScanRoot> AdditionalSourceRoots;
    public SourceCodeExtractMode Mode;
}

internal sealed class SourceCodeScanRoot
{
    public string SourceDirectory;
    public string ExportPathPrefix;
}

internal sealed class SourceCodeFile
{
    public string FullPath;
    public string RelativePath;
}

internal sealed class SourceCodeExtractResult
{
    public string OutputPath;
    public int FileCount;
}

/// <summary>
/// 扫描并导出指定目录中的源码文件，不修改源文件。
/// </summary>
internal static class SourceCodeExtractor
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// 按当前规则扫描源码文件，预览与正式提取共用此结果。
    /// </summary>
    public static List<SourceCodeFile> Scan(SourceCodeExtractOptions options)
    {
        ValidateOptions(options);

        string sourceDirectory = NormalizeDirectoryPath(options.SourceDirectory);
        string outputDirectory = NormalizeDirectoryPath(options.OutputDirectory);
        HashSet<string> extensions = ParseExtensions(options.Extensions, options.IncludeMetaFiles);
        HashSet<string> excludedDirectoryNames = ParseItems(options.ExcludedDirectoryNames);
        var files = new List<SourceCodeFile>();
        var scannedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectSourceFiles(
            sourceDirectory,
            string.Empty,
            outputDirectory,
            extensions,
            excludedDirectoryNames,
            scannedFilePaths,
            files);

        if (options.AdditionalSourceRoots != null)
        {
            for (int i = 0; i < options.AdditionalSourceRoots.Count; i++)
            {
                SourceCodeScanRoot scanRoot = options.AdditionalSourceRoots[i];
                if (scanRoot == null
                    || string.IsNullOrWhiteSpace(scanRoot.SourceDirectory)
                    || !Directory.Exists(scanRoot.SourceDirectory))
                {
                    continue;
                }

                CollectSourceFiles(
                    NormalizeDirectoryPath(scanRoot.SourceDirectory),
                    NormalizeExportPathPrefix(scanRoot.ExportPathPrefix),
                    outputDirectory,
                    extensions,
                    excludedDirectoryNames,
                    scannedFilePaths,
                    files);
            }
        }

        files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        return files;
    }

    public static SourceCodeExtractResult Extract(SourceCodeExtractOptions options)
    {
        List<SourceCodeFile> sourceFiles = Scan(options);

        if (sourceFiles.Count == 0)
        {
            throw new InvalidOperationException("没有找到符合条件的源码文件。");
        }

        string sourceDirectory = NormalizeDirectoryPath(options.SourceDirectory);
        string outputDirectory = NormalizeDirectoryPath(options.OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        string outputPath = options.Mode == SourceCodeExtractMode.MergeToText
            ? MergeToText(sourceDirectory, outputDirectory, sourceFiles)
            : CopyDirectory(sourceDirectory, outputDirectory, sourceFiles);

        return new SourceCodeExtractResult
        {
            OutputPath = outputPath,
            FileCount = sourceFiles.Count
        };
    }

    private static void ValidateOptions(SourceCodeExtractOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.SourceDirectory))
        {
            throw new InvalidOperationException("请选择源码目录。");
        }

        if (!Directory.Exists(options.SourceDirectory))
        {
            throw new DirectoryNotFoundException($"源码目录不存在：{options.SourceDirectory}");
        }

        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            throw new InvalidOperationException("请选择输出目录。");
        }

        if (File.Exists(options.OutputDirectory))
        {
            throw new InvalidOperationException("输出路径指向了一个文件，请选择目录。");
        }

        string sourceDirectory = NormalizeDirectoryPath(options.SourceDirectory);
        string outputDirectory = NormalizeDirectoryPath(options.OutputDirectory);
        if (string.Equals(sourceDirectory, outputDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("输出目录不能与源码目录相同。");
        }

        if (ParseExtensions(options.Extensions, options.IncludeMetaFiles).Count == 0)
        {
            throw new InvalidOperationException("请至少填写一个文件扩展名。");
        }
    }

    private static void CollectSourceFiles(
        string sourceDirectory,
        string exportPathPrefix,
        string outputDirectory,
        HashSet<string> extensions,
        HashSet<string> excludedDirectoryNames,
        HashSet<string> scannedFilePaths,
        List<SourceCodeFile> files)
    {
        CollectSourceFilesRecursive(
            sourceDirectory,
            sourceDirectory,
            exportPathPrefix,
            outputDirectory,
            extensions,
            excludedDirectoryNames,
            scannedFilePaths,
            files);
    }

    private static void CollectSourceFilesRecursive(
        string rootDirectory,
        string currentDirectory,
        string exportPathPrefix,
        string outputDirectory,
        HashSet<string> extensions,
        HashSet<string> excludedDirectoryNames,
        HashSet<string> scannedFilePaths,
        List<SourceCodeFile> files)
    {
        string[] childDirectories = Directory.GetDirectories(currentDirectory);
        for (int i = 0; i < childDirectories.Length; i++)
        {
            string childDirectory = childDirectories[i];
            var directoryInfo = new DirectoryInfo(childDirectory);
            if (excludedDirectoryNames.Contains(directoryInfo.Name)
                || (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0
                || IsSameOrChildPath(outputDirectory, childDirectory))
            {
                continue;
            }

            CollectSourceFilesRecursive(
                rootDirectory,
                childDirectory,
                exportPathPrefix,
                outputDirectory,
                extensions,
                excludedDirectoryNames,
                scannedFilePaths,
                files);
        }

        string[] currentFiles = Directory.GetFiles(currentDirectory);
        for (int i = 0; i < currentFiles.Length; i++)
        {
            string filePath = currentFiles[i];
            if (extensions.Contains(Path.GetExtension(filePath)) && scannedFilePaths.Add(filePath))
            {
                string relativePath = GetRelativePath(rootDirectory, filePath);
                files.Add(new SourceCodeFile
                {
                    FullPath = filePath,
                    RelativePath = string.IsNullOrEmpty(exportPathPrefix)
                        ? relativePath
                        : Path.Combine(exportPathPrefix, relativePath)
                });
            }
        }
    }

    private static string MergeToText(
        string sourceDirectory,
        string outputDirectory,
        List<SourceCodeFile> sourceFiles)
    {
        string outputPath = GetUniquePath(
            outputDirectory,
            $"SourceCode_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
            false);

        using (var writer = new StreamWriter(outputPath, false, Utf8WithoutBom))
        {
            WriteManifestHeader(writer, sourceDirectory, sourceFiles.Count);

            for (int i = 0; i < sourceFiles.Count; i++)
            {
                SourceCodeFile sourceFile = sourceFiles[i];
                string relativePath = sourceFile.RelativePath;
                writer.WriteLine($"===== BEGIN FILE: {relativePath} =====");
                writer.Write(ReadSourceText(sourceFile.FullPath));
                writer.WriteLine();
                writer.WriteLine($"===== END FILE: {relativePath} =====");
                writer.WriteLine();
            }
        }

        return outputPath;
    }

    private static string CopyDirectory(
        string sourceDirectory,
        string outputDirectory,
        List<SourceCodeFile> sourceFiles)
    {
        string outputPath = GetUniquePath(
            outputDirectory,
            $"SourceCode_{DateTime.Now:yyyyMMdd_HHmmss}",
            true);
        Directory.CreateDirectory(outputPath);

        for (int i = 0; i < sourceFiles.Count; i++)
        {
            SourceCodeFile sourceFile = sourceFiles[i];
            string relativePath = sourceFile.RelativePath;
            string destinationPath = Path.Combine(outputPath, relativePath);
            string destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(sourceFile.FullPath, destinationPath, false);
        }

        string manifestPath = Path.Combine(outputPath, "_SourceCodeManifest.txt");
        using (var writer = new StreamWriter(manifestPath, false, Utf8WithoutBom))
        {
            WriteManifestHeader(writer, sourceDirectory, sourceFiles.Count);
            for (int i = 0; i < sourceFiles.Count; i++)
            {
                writer.WriteLine(sourceFiles[i].RelativePath);
            }
        }

        return outputPath;
    }

    private static void WriteManifestHeader(TextWriter writer, string sourceDirectory, int fileCount)
    {
        writer.WriteLine("Source Code Export");
        writer.WriteLine($"Source: {sourceDirectory}");
        writer.WriteLine($"Created: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        writer.WriteLine($"Files: {fileCount}");
        writer.WriteLine();
    }

    private static string ReadSourceText(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0x00 && bytes[3] == 0x00)
        {
            return Encoding.UTF32.GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return new UTF32Encoding(true, false).GetString(bytes, 4, bytes.Length - 4);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(bytes);
        }
    }

    private static HashSet<string> ParseExtensions(string value, bool includeMetaFiles)
    {
        HashSet<string> extensions = ParseItems(value);
        var normalizedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string extension in extensions)
        {
            string normalizedExtension = extension.TrimStart('*');
            if (!normalizedExtension.StartsWith(".", StringComparison.Ordinal))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            normalizedExtensions.Add(normalizedExtension);
        }

        if (includeMetaFiles)
        {
            normalizedExtensions.Add(".meta");
        }

        return normalizedExtensions;
    }

    private static HashSet<string> ParseItems(string value)
    {
        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return items;
        }

        string[] parts = value.Split(new[] { ';', ',', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            items.Add(parts[i].Trim());
        }

        return items;
    }

    private static string NormalizeExportPathPrefix(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalizedValue = value
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Trim(Path.DirectorySeparatorChar);
        bool containsParentSegment = false;
        string[] pathSegments = normalizedValue.Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < pathSegments.Length; i++)
        {
            if (pathSegments[i] == "..")
            {
                containsParentSegment = true;
                break;
            }
        }

        if (Path.IsPathRooted(normalizedValue) || containsParentSegment)
        {
            throw new InvalidOperationException($"附加源码目录的导出路径无效：{value}");
        }

        return normalizedValue;
    }

    internal static string GetRelativePath(string rootDirectory, string filePath)
    {
        string rootWithSeparator = EnsureTrailingDirectorySeparator(NormalizeDirectoryPath(rootDirectory));
        var rootUri = new Uri(rootWithSeparator);
        var fileUri = new Uri(Path.GetFullPath(filePath));
        return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsSameOrChildPath(string path, string parentPath)
    {
        string normalizedPath = NormalizeDirectoryPath(path);
        string normalizedParentPath = NormalizeDirectoryPath(parentPath);
        return string.Equals(normalizedPath, normalizedParentPath, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(
                EnsureTrailingDirectorySeparator(normalizedParentPath),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootPath = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(rootPath)
            && string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return rootPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private static string GetUniquePath(string parentDirectory, string name, bool isDirectory)
    {
        string path = Path.Combine(parentDirectory, name);
        string baseName = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        string extension = isDirectory ? string.Empty : Path.GetExtension(name);
        int index = 1;

        while (Directory.Exists(path) || File.Exists(path))
        {
            path = Path.Combine(parentDirectory, $"{baseName}_{index}{extension}");
            index++;
        }

        return path;
    }
}
