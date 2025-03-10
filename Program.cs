using System.Text.RegularExpressions;

namespace Qatch
{
    internal static class Program
    {
        internal static void Main(string?[] args)
        {
            if (args.Length < 3 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintHelp();
                return;
            }

            string? targetPath = null;
            var createBackup = false;
            var patterns = new List<KeyValuePair<string?, string?>>();

            ParseArguments(args, ref targetPath, ref createBackup, patterns);

            if (!ValidateArguments(targetPath, patterns)) return;

            try
            {
                if (createBackup) CreateBackup(targetPath);

                if (targetPath == null) return;
                var fileData = File.ReadAllBytes(targetPath);
                var fileModified = ProcessPatterns(fileData, patterns);

                FinalizeFileChanges(targetPath, fileData, fileModified);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Qatch - Quick Patch Tool");
            Console.WriteLine("Usage: qatch.exe --target <target> [--backup] --find-replace <find:replace> [...]");
            Console.WriteLine("Options:");
            Console.WriteLine("  --target, -t <target>       Target file to patch");
            Console.WriteLine("  --backup, -b                Create backup with .BAK extension");
            Console.WriteLine("  --find, -f <pattern>        Find pattern (requires --replace)");
            Console.WriteLine("  --replace, -r <pattern>     Replace pattern (requires --find)");
            Console.WriteLine("  --find-replace, -fr <f:r>  Combined find and replace pattern");
            Console.WriteLine("  --help, -h                  Show this help");
        }

        private static void ParseArguments(string?[] args, ref string? targetPath, 
            ref bool createBackup, List<KeyValuePair<string?, string?>> patterns)
        {
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i]?.ToLower())
                {
                    case "--target":
                    case "-t":
                        if (i + 1 < args.Length) targetPath = args[++i];
                        break;
                    case "--backup":
                    case "-b":
                        createBackup = true;
                        break;
                    case "--find":
                    case "-f":
                        HandleFindReplacePair(args, ref i, patterns);
                        break;
                    case "--find-replace":
                    case "-fr":
                        HandleCombinedPattern(args, ref i, patterns);
                        break;
                }
            }
        }

        static void HandleFindReplacePair(string?[] args, ref int index, 
            List<KeyValuePair<string?, string?>> patterns)
        {
            if (index + 1 >= args.Length) return;
            var findPattern = args[++index];
            
            var replaceIndex = Array.FindIndex(args, index + 1, 
                a => a == "--replace" || a == "-r");
            
            if (replaceIndex == -1 || replaceIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --find requires corresponding --replace");
                return;
            }

            patterns.Add(new KeyValuePair<string?, string?>(
                findPattern, 
                args[replaceIndex + 1]
            ));
            index = replaceIndex + 1;
        }

        static void HandleCombinedPattern(string?[] args, ref int index, 
            List<KeyValuePair<string?, string?>> patterns)
        {
            if (index + 1 >= args.Length) return;
            var parts = args[++index]?.Split(new[] { ':' }, 2);
            
            if (parts != null && parts.Length != 2)
            {
                Console.WriteLine("Error: Invalid find-replace format. Use <find>:<replace>");
                return;
            }

            patterns.Add(new KeyValuePair<string?, string?>(parts?[0], parts?[1]));
        }

        static bool ValidateArguments(string? targetPath, List<KeyValuePair<string?, string?>> patterns)
        {
            if (string.IsNullOrEmpty(targetPath))
            {
                Console.WriteLine("Error: Target path is required");
                return false;
            }

            if (patterns.Count == 0)
            {
                Console.WriteLine("Error: At least one find-replace pattern required");
                return false;
            }

            if (!File.Exists(targetPath))
            {
                Console.WriteLine($"Error: File not found - {targetPath}");
                return false;
            }

            return true;
        }

        private static void CreateBackup(string? targetPath)
        {
            var backupPath = targetPath + ".BAK";
            if (targetPath != null) File.Copy(targetPath, backupPath, true);
            Console.WriteLine($"Created backup: {backupPath}");
        }

        private static bool ProcessPatterns(byte[] fileData, List<KeyValuePair<string?, string?>> patterns)
        {
            var fileModified = false;

            foreach (var pattern in patterns)
            {
                var findBytes = ParseHexPattern(pattern.Key);
                var replaceBytes = ParseHexPattern(pattern.Value);

                if (!ValidatePatternPair(pattern.Key, pattern.Value, findBytes, replaceBytes))
                    continue;

                var matches = FindPatternMatches(fileData, findBytes);
                if (matches.Count == 0)
                {
                    Console.WriteLine($"Pattern not found: {pattern.Key}");
                    continue;
                }

                fileModified |= ApplyReplacements(fileData, findBytes, replaceBytes, matches);
                Console.WriteLine($"Replaced {matches.Count} instances of {pattern.Key}");
            }

            return fileModified;
        }

        private static List<Tuple<byte, bool>>? ParseHexPattern(string? pattern)
        {
            var cleanPattern = Regex.Replace(pattern, "[^0-9A-Fa-f?]", "");
            var result = new List<Tuple<byte, bool>>();

            if (cleanPattern.Length % 2 != 0) return null;

            for (int i = 0; i < cleanPattern.Length; i += 2)
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
                        result.Add(Tuple.Create(
                            Convert.ToByte(pair, 16),
                            false
                        ));
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return result;
        }

        static bool ValidatePatternPair(string? findStr, string? replaceStr,
            List<Tuple<byte, bool>>? find, List<Tuple<byte, bool>>? replace)
        {
            if (find == null || replace == null)
            {
                Console.WriteLine($"Invalid pattern: {findStr}:{replaceStr}");
                return false;
            }

            if (find.Count != replace.Count)
            {
                Console.WriteLine($"Pattern length mismatch: {findStr} ({find.Count}) vs {replaceStr} ({replace.Count})");
                return false;
            }

            return true;
        }

        static List<int> FindPatternMatches(byte[] data, List<Tuple<byte, bool>>? pattern)
        {
            var matches = new List<int>();
            
            for (int i = 0; i <= data.Length - pattern.Count; i++)
            {
                if (IsPatternMatch(data, i, pattern))
                    matches.Add(i);
            }
            
            return matches;
        }

        static bool IsPatternMatch(byte[] data, int start, List<Tuple<byte, bool>>? pattern)
        {
            for (int j = 0; j < pattern.Count; j++)
            {
                if (!pattern[j].Item2 && data[start + j] != pattern[j].Item1)
                    return false;
            }
            return true;
        }

        static bool ApplyReplacements(byte[] data, List<Tuple<byte, bool>>? find,
            List<Tuple<byte, bool>>? replace, List<int> matches)
        {
            bool modified = false;

            foreach (var pos in matches)
            {
                for (int i = 0; i < find.Count; i++)
                {
                    if (!find[i].Item2 && !replace[i].Item2)
                    {
                        data[pos + i] = replace[i].Item1;
                        modified = true;
                    }
                }
            }

            return modified;
        }

        static void FinalizeFileChanges(string? path, byte[] data, bool modified)
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