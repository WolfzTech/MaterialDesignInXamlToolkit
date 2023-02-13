﻿using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace mdresgen;

public static partial class Brushes
{
    [GeneratedRegex(@"^\s*<!-- INSERT HERE -->", RegexOptions.Multiline)]
    private static partial Regex TemplateReplaceRegex();

    private const string IgnoredBrushName = "MaterialDesign.Brush.Ignored";

    public static async Task GenerateBrushesAsync()
    {
        await using var inputFile = File.OpenRead("ThemeColors.json");
        Brush[] brushes = await JsonSerializer.DeserializeAsync<Brush[]>(inputFile)
            ?? throw new InvalidOperationException("Did not find brushes from source file");

        brushes = brushes.OrderBy(x => x.Name).ToArray();

        TreeItem<Brush> brushTree = BuildBrushTree(brushes.Where(x => x.Name != IgnoredBrushName).ToList());

        DirectoryInfo repoRoot = GetRepoRoot() ?? throw new InvalidOperationException("Failed to find the repo root");

        GenerateBuiltInThemingDictionaries(brushes, repoRoot);
        GenerateObsoleteBrushesDictionary(brushes.Where(x => x.Name != IgnoredBrushName), repoRoot);
        GenerateThemeClass(brushTree, repoRoot);
    }

    private static void GenerateBuiltInThemingDictionaries(IEnumerable<Brush> brushes, DirectoryInfo repoRoot)
    {
        WriteFile("Light");
        WriteFile("Dark");

        void WriteFile(string theme)
        {
            using var writer = new StreamWriter(Path.Combine(repoRoot.FullName, "MaterialDesignThemes.Wpf", "Themes", $"MaterialDesignTheme.{theme}.xaml"));
            writer.WriteLine($"""
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"
                                    xmlns:colors="clr-namespace:MaterialDesignColors;assembly=MaterialDesignColors">
                  <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="./Internal/MaterialDesignTheme.BaseThemeColors.xaml" />
                  </ResourceDictionary.MergedDictionaries>
                """);
            foreach (Brush brush in brushes)
            {
                string value = brush.ThemeValues[theme];
                WriteBrush(brush.Name!, value, writer);

                foreach (string alternate in brush.AlternateKeys ?? Enumerable.Empty<string>())
                {
                    WriteBrush(alternate, value, writer);
                }

                static void WriteBrush(string name, string value, StreamWriter writer)
                {
                    if (value.StartsWith('#'))
                    {
                        writer.WriteLine($$"""
                      <SolidColorBrush x:Key="{{name}}" Color="{{value}}" po:Freeze="True" />
                    """);
                    }
                    else
                    {
                        writer.WriteLine($$"""
                      <colors:StaticResource x:Key="{{name}}" ResourceKey="{{value}}" />
                    """);
                    }
                }
            }

            writer.WriteLine();

            writer.WriteLine("</ResourceDictionary>");
        }
    }

    private static void GenerateObsoleteBrushesDictionary(IEnumerable<Brush> brushes, DirectoryInfo repoRoot)
    {
        StringBuilder output = new();
        foreach (Brush brush in brushes)
        {
            foreach (string obsoleteKey in brush.ObsoleteKeys ?? Enumerable.Empty<string>())
            {
                output.AppendLine($$"""
                      <colors:StaticResource x:Key="{{obsoleteKey}}" ResourceKey="{{brush.Name}}" />
                    """);
            }
        }

        using var reader = new StreamReader("MaterialDesignTheme.ObsoleteBrushes.xaml");
        string existingDictionary = reader.ReadToEnd();
        string dictionaryContents = TemplateReplaceRegex().Replace(existingDictionary, output.ToString());

        using var writer = new StreamWriter(Path.Combine(repoRoot.FullName, "MaterialDesignThemes.Wpf", "Themes", "MaterialDesignTheme.ObsoleteBrushes.xaml"));
        writer.Write(dictionaryContents);
    }

    private static void GenerateThemeClass(TreeItem<Brush> brushes, DirectoryInfo repoRoot)
    {
        using var writer = new StreamWriter(Path.Combine(repoRoot.FullName, "MaterialDesignThemes.Wpf", "Theme.g.cs"));

        writer.WriteLine("""
            /// <summary>
            /// This file is auto-generated by mdresgen.
            /// </summary>
            using System.Windows.Media;

            namespace MaterialDesignThemes.Wpf;

            partial class Theme
            {
            """);

        WriteTreeItem(brushes, writer, 0);

        writer.WriteLine("}");

        static void WriteTreeItem(TreeItem<Brush> treeItem, StreamWriter writer, int indentLevel)
        {
            string indent = new(' ', indentLevel * 4);
            if (!string.IsNullOrWhiteSpace(treeItem.Name))
            {
                writer.WriteLine($"{indent}public class {treeItem.Name}");
                writer.WriteLine($"{indent}{{");
            }

            foreach (Brush brush in treeItem.Values)
            {
                writer.WriteLine($"{indent}    public Color {brush.PropertyName} {{ get; set; }}");
                writer.WriteLine();
            }

            foreach (TreeItem<Brush> child in treeItem.Children)
            {
                writer.WriteLine($"{indent}    public {child.Name} {child.Name}s {{ get; set; }} = new();");
                writer.WriteLine();
            }

            foreach (TreeItem<Brush> child in treeItem.Children)
            {
                WriteTreeItem(child, writer, indentLevel + 1);
            }

            if (!string.IsNullOrWhiteSpace(treeItem.Name))
            {
                writer.WriteLine($"{indent}}}");
                writer.WriteLine();
            }
        }
    }

    private static DirectoryInfo? GetRepoRoot()
    {
        DirectoryInfo? currentDirectory = new(Environment.CurrentDirectory);
        while (currentDirectory is not null && !currentDirectory.EnumerateDirectories(".git").Any())
        {
            currentDirectory = currentDirectory.Parent;
        }
        return currentDirectory;
    }

    private static TreeItem<Brush> BuildBrushTree(IReadOnlyList<Brush> brushes)
    {
        TreeItem<Brush> root = new("");

        foreach (Brush brush in brushes)
        {
            TreeItem<Brush> current = root;
            foreach (string part in brush.ContainerParts)
            {
                TreeItem<Brush>? child = current.Children.FirstOrDefault(x => x.Name == part);
                if (child is null)
                {
                    child = new(part);
                    current.Children.Add(child);
                }
                current = child;
            }
            current.Values.Add(brush);
        }

        return root;
    }
}

public record class Brush(
    [property:JsonPropertyName("name")]
    string? Name,
    [property:JsonPropertyName("themeValues")]
    ThemeValues ThemeValues,
    [property:JsonPropertyName("alternateKeys")]
    string[]? AlternateKeys,
    [property:JsonPropertyName("obsoleteKeys")]
    string[]? ObsoleteKeys)
{
    public const string BrushPrefix = "MaterialDesign.Brush.";
    public string PropertyName => Name!.Split(".")[^1];
    public string NameWithoutPrefix => Name![BrushPrefix.Length..];
    public string[] ContainerParts => Name!.Split('.')[2..^1];
    public string ContainerTypeName => string.Join('.', ContainerParts);
}

public record class ThemeValues(
    [property:JsonPropertyName("light")]
    string Light,
    [property:JsonPropertyName("dark")]
    string Dark)
{
    public string this[string theme]
    {
        get
        {
            return theme.ToLowerInvariant() switch
            {
                "light" => Light,
                "dark" => Dark,
                _ => throw new InvalidOperationException($"Unknown theme: {theme}")
            };
        }
    }
}

