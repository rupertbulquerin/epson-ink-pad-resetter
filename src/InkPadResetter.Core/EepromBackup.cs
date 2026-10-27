using System.Globalization;
using System.Text;

namespace InkPadResetter.Core;

/// <summary>A full EEPROM dump, saved as a readable hex text file.</summary>
public sealed record EepromBackup(string Model, string Serial, DateTime TakenAt, byte[] Data)
{
    public string FileName => $"eeprom-{Serial}-{TakenAt:yyyyMMdd-HHmmss}.txt";

    public string Save(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        var text = new StringBuilder()
            .AppendLine($"# Epson {Model} EEPROM backup, {TakenAt:yyyy-MM-dd HH:mm:ss}")
            .AppendLine($"# model {Model}")
            .AppendLine($"# serial {Serial}");
        for (int row = 0; row < Data.Length; row += 16)
        {
            text.Append($"{row:X3}:");
            for (int i = row; i < Math.Min(row + 16, Data.Length); i++) text.Append($" {Data[i]:X2}");
            text.AppendLine();
        }
        File.WriteAllText(path, text.ToString());
        return path;
    }

    public static EepromBackup Load(string path)
    {
        string model = "", serial = "";
        var data = new List<byte>();
        foreach (var raw in File.ReadLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("# model ", StringComparison.Ordinal)) model = line[8..].Trim();
            else if (line.StartsWith("# serial ", StringComparison.Ordinal)) serial = line[9..].Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            int colon = line.IndexOf(':');
            int offset = int.Parse(line[..colon], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (offset != data.Count) throw new InvalidDataException($"Backup row {offset:X3} is out of order.");
            foreach (var cell in line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                data.Add(byte.Parse(cell, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        return new(model, serial, File.GetLastWriteTime(path), [.. data]);
    }
}
