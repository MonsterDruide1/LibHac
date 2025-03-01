﻿using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace hactoolnet;

internal static class CliParser
{
    private static CliOption[] GetCliOptions() => new[]
    {
        new CliOption("help", 0, (o, _) => o.PrintHelp = true),
        new CliOption("version", 0, (o, _) => o.PrintVersion = true),
        new CliOption("custom", 0, (o, _) => o.RunCustom = true),
        new CliOption("intype", 't', 1, (o, a) => o.InFileType = ParseFileType(o, a[0])),
        new CliOption("raw", 'r', 0, (o, _) => o.Raw = true),
        new CliOption("verify", 'y', 0, (o, _) => o.Validate = true),
        new CliOption("dev", 'd', 0, (o, _) => o.UseDevKeys = true),
        new CliOption("enablehash", 'h', 0, (o, _) => o.EnableHash = true),
        new CliOption("disablekeywarns", 0, (o, _) => o.DisableKeyWarns = true),
        new CliOption("keyset", 'k', 1, (o, a) => o.Keyfile = a[0]),
        new CliOption("titlekeys", 1, (o, a) => o.TitleKeyFile = a[0]),
        new CliOption("consolekeys", 1, (o, a) => o.ConsoleKeyFile = a[0]),
        new CliOption("accesslog", 1, (o, a) => o.AccessLog = a[0]),
        new CliOption("resultlog", 1, (o, a) => o.ResultLog = a[0]),
        new CliOption("section0", 1, (o, a) => o.SectionOut[0] = a[0]),
        new CliOption("section1", 1, (o, a) => o.SectionOut[1] = a[0]),
        new CliOption("section2", 1, (o, a) => o.SectionOut[2] = a[0]),
        new CliOption("section3", 1, (o, a) => o.SectionOut[3] = a[0]),
        new CliOption("section0dir", 1, (o, a) => o.SectionOutDir[0] = a[0]),
        new CliOption("section1dir", 1, (o, a) => o.SectionOutDir[1] = a[0]),
        new CliOption("section2dir", 1, (o, a) => o.SectionOutDir[2] = a[0]),
        new CliOption("section3dir", 1, (o, a) => o.SectionOutDir[3] = a[0]),
        new CliOption("header", 1, (o, a) => o.HeaderOut = a[0]),
        new CliOption("exefs", 1, (o, a) => o.ExefsOut = a[0]),
        new CliOption("exefsdir", 1, (o, a) => o.ExefsOutDir = a[0]),
        new CliOption("romfs", 1, (o, a) => o.RomfsOut = a[0]),
        new CliOption("romfsdir", 1, (o, a) => o.RomfsOutDir = a[0]),
        new CliOption("debugoutdir", 1, (o, a) => o.DebugOutDir = a[0]),
        new CliOption("savedir", 1, (o, a) => o.SaveOutDir = a[0]),
        new CliOption("outdir", 1, (o, a) => o.OutDir = a[0]),
        new CliOption("ini1dir", 1, (o, a) => o.Ini1OutDir = a[0]),
        new CliOption("outfile", 1, (o, a) => o.OutFile = a[0]),
        new CliOption("plaintext", 1, (o, a) => o.PlaintextOut = a[0]),
        new CliOption("ciphertext", 1, (o, a) => o.CiphertextOut = a[0]),
        new CliOption("uncompressed", 1, (o, a) => o.UncompressedOut = a[0]),
        new CliOption("nspout", 1, (o, a) => o.NspOut = a[0]),
        new CliOption("sdseed", 1, (o, a) => o.SdSeed = a[0]),
        new CliOption("sdpath", 1, (o, a) => o.SdPath = a[0]),
        new CliOption("basenca", 1, (o, a) => o.BaseNca = a[0]),
        new CliOption("basefile", 1, (o, a) => o.BaseFile = a[0]),
        new CliOption("rootdir", 1, (o, a) => o.RootDir = a[0]),
        new CliOption("updatedir", 1, (o, a) => o.UpdateDir = a[0]),
        new CliOption("normaldir", 1, (o, a) => o.NormalDir = a[0]),
        new CliOption("securedir", 1, (o, a) => o.SecureDir = a[0]),
        new CliOption("logodir", 1, (o, a) => o.LogoDir = a[0]),
        new CliOption("repack", 1, (o, a) => o.RepackSource = a[0]),
        new CliOption("listapps", 0, (o, _) => o.ListApps = true),
        new CliOption("listtitles", 0, (o, _) => o.ListTitles = true),
        new CliOption("listncas", 0, (o, _) => o.ListNcas = true),
        new CliOption("listromfs", 0, (o, _) => o.ListRomFs = true),
        new CliOption("listfiles", 0, (o, _) => o.ListFiles = true),
        new CliOption("sign", 0, (o, _) => o.SignSave = true),
        new CliOption("trim", 0, (o, _) => o.TrimSave = true),
        new CliOption("readbench", 0, (o, _) => o.ReadBench = true),
        new CliOption("hashedfs", 0, (o, _) => o.BuildHfs = true),
        new CliOption("extractini1", 0, (o, _) => o.ExtractIni1 = true),
        new CliOption("title", 1, (o, a) => o.TitleId = ParseTitleId(o, a[0])),
        new CliOption("bench", 1, (o, a) => o.BenchType = a[0]),
        new CliOption("cpufreq", 1, (o, a) => o.CpuFrequencyGhz = ParseDouble(o, a[0])),
        new CliOption("json", 1, (o, a) => o.JsonFile = a[0]),

        new CliOption("replacefile", 2, (o, a) =>
        {
            o.ReplaceFileDest = a[0];
            o.ReplaceFileSource = a[1];
        })
    };

    public static Options Parse(string[] args)
    {
        Options options = ParseAllOptions(args);

        if (options.PrintVersion)
        {
            Console.WriteLine(GetLongVersion());

            return options;
        }

        if (options.PrintHelp)
        {
            Console.WriteLine(GetShortVersion());
            Console.WriteLine(GetUsage());

            return options;
        }

        if (!options.IsParseSuccessful)
        {
            Console.WriteLine($"hactoolnet: {options.ParseErrorMessage}");
            Console.WriteLine("Usage: hactoolnet [options...] <path>");
            Console.WriteLine("Use 'hactoolnet --help' for full usage information.");

            return options;
        }

        options.ContinueRunning = true;
        return options;
    }

    public static Options ParseAllOptions(string[] args)
    {
        var options = new Options();
        bool inputSpecified = false;

        CliOption[] cliOptions = GetCliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string arg;

            if (args[i].Length == 2 && (args[i][0] == '-' || args[i][0] == '/'))
            {
                arg = args[i][1].ToString().ToLower();
            }
            else if (args[i].Length > 2 && (args[i][0] == '-' && args[i][1] == '-'))
            {
                arg = args[i].Substring(2).ToLower();
            }
            else
            {
                if (inputSpecified)
                {
                    options.ParseErrorMessage ??= $"Unable to parse option {args[i]}";
                    continue;
                }

                options.InFile = args[i];
                inputSpecified = true;
                continue;
            }

            CliOption option = cliOptions.FirstOrDefault(x => x.Long == arg || x.Short == arg);
            if (option == null)
            {
                options.ParseErrorMessage ??= $"Unknown option {args[i]}";
                continue;
            }

            if (i + option.ArgsNeeded >= args.Length)
            {
                options.ParseErrorMessage ??= $"Need {option.ArgsNeeded} parameter{(option.ArgsNeeded == 1 ? "" : "s")} after {args[i]}";
                continue;
            }

            string[] optionArgs = new string[option.ArgsNeeded];
            Array.Copy(args, i + 1, optionArgs, 0, option.ArgsNeeded);

            option.Assigner(options, optionArgs);
            i += option.ArgsNeeded;
        }

        if (!inputSpecified && options.InFileType != FileType.Keygen && options.InFileType != FileType.Bench && !options.RunCustom)
        {
            options.ParseErrorMessage ??= "Input file must be specified";
        }

        options.IsParseSuccessful = options.ParseErrorMessage is null;
        return options;
    }

    private static FileType ParseFileType(Options options, string input)
    {
        switch (input.ToLower())
        {
            case "nca": return FileType.Nca;
            case "pfs0": return FileType.Pfs0;
            case "pfsbuild": return FileType.PfsBuild;
            case "nsp": return FileType.Nsp;
            case "romfs": return FileType.Romfs;
            case "romfsbuild": return FileType.RomfsBuild;
            case "nax0": return FileType.Nax0;
            case "xci": return FileType.Xci;
            case "switchfs": return FileType.SwitchFs;
            case "save": return FileType.Save;
            case "keygen": return FileType.Keygen;
            case "pk11": return FileType.Pk11;
            case "pk21": return FileType.Pk21;
            case "kip1": return FileType.Kip1;
            case "ini1": return FileType.Ini1;
            case "ndv0": return FileType.Ndv0;
            case "bench": return FileType.Bench;
            case "npdm": return FileType.Npdm;
        }

        options.ParseErrorMessage ??= "Specified type is invalid.";

        return default;
    }

    private static ulong ParseTitleId(Options options, string input)
    {
        if (input.Length != 16)
        {
            options.ParseErrorMessage ??= "Title ID must be 16 hex characters long";
            return default;
        }

        if (!ulong.TryParse(input, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id))
        {
            options.ParseErrorMessage ??= "Could not parse title ID";
        }

        return id;
    }

    private static double ParseDouble(Options options, string input)
    {
        if (!double.TryParse(input, out double value))
        {
            options.ParseErrorMessage ??= $"Could not parse value \"{input}\"";
        }

        return value;
    }

    private static string GetShortVersion()
    {
        return $"hactoolnet {VersionInfo.Version}";
    }

    private static string GetLongVersion()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"hactoolnet {VersionInfo.Version}");
        //sb.AppendLine($"Commit time: {VersionInfo.CommitTime}");
        //sb.Append($"Commit hash: {VersionInfo.CommitHash}");

        return sb.ToString();
    }

    private static string GetUsage()
    {
        var sb = new StringBuilder();

        sb.AppendLine("Usage: hactoolnet.exe [options...] <path>");
        sb.AppendLine("Options:");
        sb.AppendLine("  -r, --raw            Keep raw data, don\'t unpack.");
        sb.AppendLine("  -y, --verify         Verify all hashes in the input file.");
        sb.AppendLine("  -h, --enablehash     Enable hash checks when reading the input file.");
        sb.AppendLine("  -d, --dev            Decrypt with development keys instead of retail.");
        sb.AppendLine("  -k, --keyset         Load keys from an external file.");
        sb.AppendLine("  -t, --intype=type    Specify input file type [nca, xci, romfs, pfs0, pk11, pk21, ini1, kip1, switchfs, save, ndv0, keygen, romfsbuild, pfsbuild]");
        sb.AppendLine("  --titlekeys <file>   Load title keys from an external file.");
        sb.AppendLine("  --accesslog <file>   Specify the access log file path.");
        sb.AppendLine("  --disablekeywarns    Disables warning output when loading external keys.");
        sb.AppendLine("  --version            Display version information and exit.");
        sb.AppendLine("  --help               Display this help and exit.");
        sb.AppendLine("NCA options:");
        sb.AppendLine("  --plaintext <file>   Specify file path for saving a decrypted copy of the NCA.");
        sb.AppendLine("  --ciphertext <file>  Specify file path for saving an encrypted copy of the NCA.");
        sb.AppendLine("  --header <file>      Specify Header file path.");
        sb.AppendLine("  --section0 <file>    Specify Section 0 file path.");
        sb.AppendLine("  --section1 <file>    Specify Section 1 file path.");
        sb.AppendLine("  --section2 <file>    Specify Section 2 file path.");
        sb.AppendLine("  --section3 <file>    Specify Section 3 file path.");
        sb.AppendLine("  --section0dir <dir>  Specify Section 0 directory path.");
        sb.AppendLine("  --section1dir <dir>  Specify Section 1 directory path.");
        sb.AppendLine("  --section2dir <dir>  Specify Section 2 directory path.");
        sb.AppendLine("  --section3dir <dir>  Specify Section 3 directory path.");
        sb.AppendLine("  --exefs <file>       Specify ExeFS file path.");
        sb.AppendLine("  --exefsdir <dir>     Specify ExeFS directory path.");
        sb.AppendLine("  --romfs <file>       Specify RomFS file path.");
        sb.AppendLine("  --romfsdir <dir>     Specify RomFS directory path.");
        sb.AppendLine("  --listromfs          List files in RomFS.");
        sb.AppendLine("  --basenca            Set Base NCA to use with update partitions.");
        sb.AppendLine("NPDM options:");
        sb.AppendLine("  --json <file>        Specify file path for saving JSON representation of program permissions to.");
        sb.AppendLine("KIP1 options:");
        sb.AppendLine("  --uncompressed <f>   Specify file path for saving uncompressed KIP1.");
        sb.AppendLine("RomFS options:");
        sb.AppendLine("  --romfsdir <dir>     Specify RomFS directory path.");
        sb.AppendLine("  --listromfs          List files in RomFS.");
        sb.AppendLine("RomFS creation options:");
        sb.AppendLine("                       Input path must be a directory");
        sb.AppendLine("  --outfile <file>     Specify created RomFS file path.");
        sb.AppendLine("Partition FS options:");
        sb.AppendLine("  --outdir <dir>       Specify extracted FS directory path.");
        sb.AppendLine("Partition FS creation options:");
        sb.AppendLine("                       Input path must be a directory");
        sb.AppendLine("  --outfile <file>     Specify created Partition FS file path.");
        sb.AppendLine("  --hashedfs           Create a hashed Partition FS (HFS0).");
        sb.AppendLine("XCI options:");
        sb.AppendLine("  --rootdir <dir>      Specify root XCI directory path.");
        sb.AppendLine("  --updatedir <dir>    Specify update XCI directory path.");
        sb.AppendLine("  --normaldir <dir>    Specify normal XCI directory path.");
        sb.AppendLine("  --securedir <dir>    Specify secure XCI directory path.");
        sb.AppendLine("  --logodir <dir>      Specify logo XCI directory path.");
        sb.AppendLine("  --outdir <dir>       Specify XCI directory path.");
        sb.AppendLine("  --nspout <file>      Specify file for the created NSP.");
        sb.AppendLine("Partition FS and XCI options:");
        sb.AppendLine("  --exefs <file>       Specify main ExeFS file path.");
        sb.AppendLine("  --exefsdir <dir>     Specify main ExeFS directory path.");
        sb.AppendLine("  --romfs <file>       Specify main RomFS file path.");
        sb.AppendLine("  --romfsdir <dir>     Specify main RomFS directory path.");
        sb.AppendLine("  --listapps           List application info.");
        sb.AppendLine("  --listtitles         List title info for all titles.");
        sb.AppendLine("  --listncas           List info for all NCAs.");
        sb.AppendLine("  --title <title id>   Specify title ID to use.");
        sb.AppendLine("Package1 options:");
        sb.AppendLine("  --outdir <dir>       Specify Package1 directory path.");
        sb.AppendLine("Package2 options:");
        sb.AppendLine("  --outdir <dir>       Specify Package2 directory path.");
        sb.AppendLine("  --extractini1        Enable INI1 extraction to default directory (redundant with --ini1dir set).");
        sb.AppendLine("  --ini1dir <dir>      Specify INI1 directory path. Overrides default path, if present.");
        sb.AppendLine("INI1 options:");
        sb.AppendLine("  --outdir <dir>       Specify INI1 directory path.");
        sb.AppendLine("Switch FS options:");
        sb.AppendLine("  --sdseed <seed>      Set console unique seed for SD card NAX0 encryption.");
        sb.AppendLine("  --listapps           List application info.");
        sb.AppendLine("  --listtitles         List title info for all titles.");
        sb.AppendLine("  --listncas           List info for all NCAs.");
        sb.AppendLine("  --title <title id>   Specify title ID to use.");
        sb.AppendLine("  --outdir <dir>       Specify directory path to save title NCAs to. (--title must be specified)");
        sb.AppendLine("  --exefs <file>       Specify ExeFS directory path. (--title must be specified)");
        sb.AppendLine("  --exefsdir <dir>     Specify ExeFS directory path. (--title must be specified)");
        sb.AppendLine("  --romfs <file>       Specify RomFS directory path. (--title must be specified)");
        sb.AppendLine("  --romfsdir <dir>     Specify RomFS directory path. (--title must be specified)");
        sb.AppendLine("  --savedir <dir>      Specify save file directory path.");
        sb.AppendLine("  -y, --verify         Verify all titles, or verify a single title if --title is set.");
        sb.AppendLine("Save data options:");
        sb.AppendLine("  --outdir <dir>       Specify directory path to save contents to.");
        sb.AppendLine("  --debugoutdir <dir>  Specify directory path to save intermediate data to for debugging.");
        sb.AppendLine("  --sign               Sign the save file. (Requires device_key in key file)");
        sb.AppendLine("  --trim               Trim garbage data in the save file. (Requires device_key in key file)");
        sb.AppendLine("  --listfiles          List files in save file.");
        sb.AppendLine("  --repack <dir>       Replaces the contents of the save data with the specified directory.");
        sb.AppendLine("  --replacefile <filename in save> <file> Replaces a file in the save data");
        sb.AppendLine("NAX0 options:");
        sb.AppendLine("  --sdseed <seed>      Set console unique seed for SD card NAX0 encryption.");
        sb.AppendLine("  --sdpath <path>      Set relative path for NAX0 key derivation (ex: /registered/000000FF/cafebabecafebabecafebabecafebabe.nca).");
        sb.AppendLine("  --plaintext          Specify file path to save decrypted contents.");
        sb.AppendLine("NDV0 (Delta) options:");
        sb.AppendLine("                       Input delta patch can be a delta NCA file or a delta fragment file.");
        sb.AppendLine("  --basefile <file>    Specify base file path.");
        sb.AppendLine("  --outfile            Specify patched file path.");
        sb.AppendLine("Keygen options:");
        sb.AppendLine("  --outdir <dir>       Specify directory path to save key files to.");

        return sb.ToString();
    }

    private class CliOption
    {
        public CliOption(string longName, char shortName, int argsNeeded, Action<Options, string[]> assigner)
        {
            Long = longName;
            Short = shortName.ToString();
            ArgsNeeded = argsNeeded;
            Assigner = assigner;
        }

        public CliOption(string longName, int argsNeeded, Action<Options, string[]> assigner)
        {
            Long = longName;
            ArgsNeeded = argsNeeded;
            Assigner = assigner;
        }

        public string Long { get; }
        public string Short { get; }
        public int ArgsNeeded { get; }
        public Action<Options, string[]> Assigner { get; }
    }
}
