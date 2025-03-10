# Qatch - Quick Patch Tool

Qatch is a command-line utility for patching binary files by finding and replacing specific byte patterns. It supports wildcards in patterns and can create backups of files before modification.

## Overview

Qatch allows you to:
- Replace specific byte patterns in binary files
- Use wildcards in search patterns to match any byte
- Create backups before patching
- Apply multiple pattern replacements in a single operation

## Usage

```
qatch.exe --target <target> [--backup] --find-replace <find-pattern:replace-pattern> [--find-replace <find-pattern:replace-pattern> ...]
```

### Options

- `--target, -t <target>` - Target file to patch
- `--backup, -b` - Create a backup of the target file (with .BAK extension)
- `--find-replace, -fr <find-pattern:replace-pattern>` - Combined find and replace pattern in hex format
- `--find, -f <find-pattern>` - Used with a corresponding --replace option
- `--replace, -r <replace-pattern>` - Used with a preceding --find option
  - Note: When using --find and --replace, the --find must come first, followed by --replace
- `--help, -h` - Show help message

## Pattern Format

Patterns should be specified in hexadecimal format. You can use `??` as a wildcard to match any byte.

For example:
```
0228????????2D02162A000228????????2D23:172A????????2D02162A000228????????2D23
```

In this pattern:
- `0228` must match exactly
- `????????` must match any 8 bytes, with any value
- `2D02162A00` must match exactly
- And so on...

## Examples

### Create a backup and apply a single patch:

```
qatch.exe --target C:\path\to\file.exe --backup --find-replace 0228????2D02:172A????2D02
```

### Apply multiple patches:

```
qatch.exe --target C:\path\to\file.dll --find-replace 0228????:172A???? --find-replace 2D0216:2D0218
```

### Using separate find and replace options:

```
qatch.exe --target C:\path\to\file.exe --find 0228????2D02 --replace 172A????2D02
```

### Multiple patches with mixed syntax:

```
qatch.exe --target C:\path\to\file.exe --backup --find-replace 0228????:172A???? --find 2D0216 --replace 2D0218
```

## Notes

- The tool will only modify bytes that are not wildcards in the find pattern
- If no matches are found for a pattern, the file will not be modified
- Always create a backup before patching critical files
- When using `--find` and `--replace`, the `--find` option must come before its corresponding `--replace` option
- Wildcards in the find pattern (`??`) will not be modified in the target file