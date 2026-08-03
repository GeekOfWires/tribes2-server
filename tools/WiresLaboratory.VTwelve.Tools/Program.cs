using WiresLaboratory.VTwelve.Resources;
using WiresLaboratory.VTwelve.Script.Dso;

namespace WiresLaboratory.VTwelve.Tools;

/// <summary>
/// Corpus harness: runs every shipped VL2 volume and DSO code block through the readers.
/// The DSO parser is strict about landing exactly on end-of-file, so a clean sweep over the
/// whole corpus is the evidence that the version-174 layout is correct.
/// </summary>
public class Program
{
    public static int Main(string[] args)
    {
        // --pcap <file>: replay a captured client session through the wire-format types.
        var pcapIndex = Array.IndexOf(args, "--pcap");
        if (pcapIndex >= 0 && pcapIndex + 1 < args.Length)
            return PcapProtocolCheck.Run(args[pcapIndex + 1], gamePort: 28000);

        var root = args.Length > 0 ? args[0] : ".";
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"not a directory: {root}");
            return 2;
        }

        // --symbols: report the engine surface the shipped scripts actually depend on.
        if (args.Contains("--symbols"))
        {
            var dso = Directory.GetFiles(root, "*.dso", SearchOption.AllDirectories);
            IdentifierSurvey.Report(IdentifierSurvey.Run(dso, engineOnly: true), top: 55);
            return 0;
        }

        var failures = 0;
        failures += SweepVolumes(root);
        failures += SweepDso(root);

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "RESULT: clean sweep — VL2 and DSO v174 layouts verified against the shipped corpus."
            : $"RESULT: {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    private static int SweepVolumes(string root)
    {
        var files = Directory.GetFiles(root, "*.vl2", SearchOption.AllDirectories).Order().ToArray();
        Console.WriteLine($"=== VL2 volumes ({files.Length}) ===");
        var failed = 0;
        long entries = 0;
        foreach (var f in files)
        {
            try
            {
                using var vol = Vl2Archive.Open(f);
                entries += vol.Entries.Count;
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"  FAIL {Path.GetFileName(f)}: {ex.Message}");
            }
        }
        Console.WriteLine($"  opened {files.Length - failed}/{files.Length}, {entries:N0} entries total");
        return failed;
    }

    private static int SweepDso(string root)
    {
        var files = Directory.GetFiles(root, "*.dso", SearchOption.AllDirectories).Order().ToArray();
        Console.WriteLine($"=== DSO code blocks ({files.Length}) ===");

        var failed = 0;
        var versions = new SortedDictionary<uint, int>();
        long slots = 0, ident = 0;
        var widest = (Name: "", Code: 0u);

        foreach (var f in files)
        {
            try
            {
                var dso = DsoFile.Load(f);
                versions[dso.Version] = versions.GetValueOrDefault(dso.Version) + 1;
                slots += dso.CodeSize;
                ident += dso.Identifiers.Length;
                if (dso.CodeSize > widest.Code) widest = (Path.GetFileName(f), dso.CodeSize);
            }
            catch (Exception ex)
            {
                failed++;
                if (failed <= 10) Console.WriteLine($"  FAIL {Path.GetFileName(f)}: {ex.Message}");
            }
        }

        Console.WriteLine($"  parsed {files.Length - failed}/{files.Length}");
        foreach (var (v, n) in versions) Console.WriteLine($"    version {v}: {n} file(s)");
        Console.WriteLine($"  {slots:N0} instruction slots, {ident:N0} identifier patches");
        if (widest.Code > 0) Console.WriteLine($"  largest: {widest.Name} ({widest.Code:N0} slots)");

        // A sample of resolved strings proves the tables are addressed correctly, not just sized.
        var sample = files.FirstOrDefault(f => f.Contains("cloaking", StringComparison.OrdinalIgnoreCase))
                     ?? files.FirstOrDefault();
        if (sample is not null && failed < files.Length)
        {
            var dso = DsoFile.Load(sample);
            var names = dso.GlobalStrings.Enumerate().Take(8).Select(s => s.Value);
            Console.WriteLine($"  sample {Path.GetFileName(sample)} globals: {string.Join(", ", names)}");
        }
        return failed;
    }
}
