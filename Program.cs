using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Qatch
{
    public static class Program
    {
        private const int MinimumPatternLength = 2;

        internal static void Main(string[] args)
        {
            if (args.Length < 3 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintHelp();
                return;
            }

            string targetPath = null;
            var createBackup = false;
            var patterns = new List<KeyValuePair<string, string>>();

            ParseArguments(args, ref targetPath, ref createBackup, patterns);

            if (!ValidateArguments(targetPath, patterns)) return;

            try
            {
                if (createBackup) CreateBackup(targetPath);

                var fileData = File.ReadAllBytes(targetPath);
                var fileModified = ProcessPatterns(fileData, patterns);

                FinalizeFileChanges(targetPath, fileData, fileModified);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Qatch - Quick Patch Tool");
            Console.WriteLine("Usage: qatch.exe --target <file> [--backup] [--find-replace <HEX:HEX>...]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --target, -t <PATH>      Target file to modify");
            Console.WriteLine("  --backup, -b             Create backup (.BAK)");
            Console.WriteLine("  --find-replace, -fr <PAT> Colon-separated hex patterns");
            Console.WriteLine("  --help, -h               Show this help");
            Console.WriteLine("\nPattern format:");
            Console.WriteLine("  Use ?? for wildcard bytes (e.g., 01??A3:02??B4)");
            Console.WriteLine("  Enclose patterns in quotes to prevent shell expansion");
        }

        private static void ParseArguments(IReadOnlyList<string> args, ref string targetPath, 
            ref bool createBackup, List<KeyValuePair<string, string>> patterns)
        {
            for (var i = 0; i < args.Count; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--target":
                    case "-t":
                        if (i + 1 < args.Count) targetPath = args[++i];
                        break;
                    
                    case "--backup":
                    case "-b":
                        createBackup = true;
                        break;
                    
                    case "--find-replace":
                    case "-fr":
                        HandleCombinedPattern(args, ref i, patterns);
                        break;
                }
            }
        }

        private static void HandleCombinedPattern(IReadOnlyList<string> args, ref int index,
            List<KeyValuePair<string, string>> patterns)
        {
            if (index + 1 >= args.Count)
            {
                Console.WriteLine("Error: Missing value for --find-replace");
                return;
            }

            var parts = args[++index].Split(new[] { ':' }, 2);
            if (parts.Length != 2)
            {
                Console.WriteLine("Error: Invalid find-replace format. Use <find>:<replace>");
                return;
            }

            var find = parts[0].Trim();
            var replace = parts[1].Trim();

            if (string.IsNullOrEmpty(find)) Console.WriteLine("Error: Empty find pattern");
            if (string.IsNullOrEmpty(replace)) Console.WriteLine("Error: Empty replace pattern");
            
            if (!string.IsNullOrEmpty(find) && !string.IsNullOrEmpty(replace))
                patterns.Add(new KeyValuePair<string, string>(find, replace));
        }

        private static bool ValidateArguments(string targetPath, List<KeyValuePair<string, string>> patterns)
        {
            var valid = true;
            
            if (string.IsNullOrEmpty(targetPath))
            {
                Console.WriteLine("Error: Target path is required");
                valid = false;
            }
            else if (!File.Exists(targetPath))
            {
                Console.WriteLine($"Error: File not found - {targetPath}");
                valid = false;
            }

            if (patterns.Count == 0)
            {
                Console.WriteLine("Error: At least one find-replace pattern required");
                valid = false;
            }

            return valid;
        }

        private static void CreateBackup(string targetPath)
        {
            var backupPath = targetPath + ".BAK";
            File.Copy(targetPath, backupPath, true);
            Console.WriteLine($"Created backup: {backupPath}");
        }

        private static bool ProcessPatterns(byte[] fileData, List<KeyValuePair<string, string>> patterns)
        {
            var modified = false;
            
            foreach (var (findPattern, replacePattern) in patterns)
            {
                var findBytes = ParseHexPattern(findPattern);
                var replaceBytes = ParseHexPattern(replacePattern);

                if (!ValidatePatternPair(findPattern, replacePattern, findBytes, replaceBytes))
                    continue;

                var matches = FindPatternMatches(fileData, findBytes);
                if (matches.Count == 0)
                {
                    Console.WriteLine($"Pattern not found: {findPattern}");
                    continue;
                }

                modified |= ApplyReplacements(fileData, findBytes, replaceBytes, matches);
                Console.WriteLine($"Replaced {matches.Count} instances of: {findPattern}");
            }

            return modified;
        }

        private static List<Tuple<byte, bool>> ParseHexPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return null;

            var cleanPattern = Regex.Replace(pattern.ToUpper(), "[^0-9A-F?]", "");
            var result = new List<Tuple<byte, bool>>();

            if (cleanPattern.Length % 2 != 0)
            {
                Console.WriteLine($"Error: Odd number of hex characters in pattern: {pattern}");
                return null;
            }

            for (var i = 0; i < cleanPattern.Length; i += 2)
            {
                var pair = cleanPattern.Substring(i, 2);
                if (pair == "??")
                {
                    result.Add(Tuple.Create((byte)0, true));
                }
                else
                {
                    try
                    {
                        var value = Convert.ToByte(pair, 16);
                        result.Add(Tuple.Create(value, false));
                    }
                    catch
                    {
                        Console.WriteLine($"Error: Invalid hex pair '{pair}' in pattern: {pattern}");
                        return null;
                    }
                }
            }

            if (result.Count < MinimumPatternLength)
            {
                Console.WriteLine($"Error: Pattern too short (min {MinimumPatternLength} bytes): {pattern}");
                return null;
            }

            return result;
        }

        private static bool ValidatePatternPair(string findPattern, string replacePattern,
            List<Tuple<byte, bool>> findBytes, List<Tuple<byte, bool>> replaceBytes)
        {
            var valid = true;

            if (findBytes == null)
            {
                Console.WriteLine($"Error: Invalid find pattern: {findPattern}");
                valid = false;
            }

            if (replaceBytes == null)
            {
                Console.WriteLine($"Error: Invalid replace pattern: {replacePattern}");
                valid = false;
            }

            if (findBytes?.Count != replaceBytes?.Count)
            {
                Console.WriteLine("Error: Pattern length mismatch - " +
                                  $"Find: {findBytes?.Count ?? 0} bytes, " +
                                  $"Replace: {replaceBytes?.Count ?? 0} bytes");
                valid = false;
            }

            return valid;
        }

        private static List<int> FindPatternMatches(byte[] data, List<Tuple<byte, bool>> pattern)
        {
            var matches = new List<int>();
            if (pattern == null || pattern.Count == 0) return matches;

            for (var i = 0; i <= data.Length - pattern.Count; i++)
            {
                var match = true;
                for (var j = 0; j < pattern.Count; j++)
                {
                    if (pattern[j].Item2) continue; // Skip wildcards
                    if (data[i + j] != pattern[j].Item1)
                    {
                        match = false;
                        break;
                    }
                }
                if (match) matches.Add(i);
            }
            return matches;
        }

        private static bool ApplyReplacements(byte[] data, List<Tuple<byte, bool>> find,
            List<Tuple<byte, bool>> replace, List<int> matches)
        {
            var modified = false;

            foreach (var pos in matches)
            {
                for (var i = 0; i < find.Count; i++)
                {
                    // Only replace non-wildcard positions in find pattern
                    if (!find[i].Item2 && !replace[i].Item2)
                    {
                        data[pos + i] = replace[i].Item1;
                        modified = true;
                    }
                }
            }

            return modified;
        }

        private static void FinalizeFileChanges(string path, byte[] data, bool modified)
        {
            if (modified)
            {
                File.WriteAllBytes(path, data);
                Console.WriteLine("File successfully patched");
            }
            else
            {
                Console.WriteLine("No changes made");
            }
        }
    }
}