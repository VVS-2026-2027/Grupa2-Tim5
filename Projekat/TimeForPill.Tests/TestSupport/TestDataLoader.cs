using System.Globalization;
using System.Xml.Linq;

namespace TimeForPill.Tests;

public static class TestDataLoader
{
    public static IEnumerable<object[]> CsvTherapyCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "podaci.csv");
        var rows = File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
            .Skip(1);

        foreach (var row in rows)
        {
            var values = row.Split(';');
            if (values.Length != 5)
            {
                throw new InvalidDataException($"Neispravan CSV red: {row}");
            }

            yield return
            [
                values[0],
                int.Parse(values[1], CultureInfo.InvariantCulture),
                int.Parse(values[2], CultureInfo.InvariantCulture),
                int.Parse(values[3], CultureInfo.InvariantCulture),
                bool.Parse(values[4])
            ];
        }
    }

    public static IEnumerable<object[]> XmlContactCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "podaci.xml");
        var document = XDocument.Load(path);

        foreach (var contact in document.Root!.Elements("Kontakt"))
        {
            yield return
            [
                (string?)contact.Attribute("Ime") ?? string.Empty,
                (string?)contact.Attribute("Prezime") ?? string.Empty,
                (string?)contact.Attribute("Email") ?? string.Empty,
                (string?)contact.Attribute("Telefon") ?? string.Empty,
                bool.Parse((string?)contact.Attribute("OcekivanoValidno") ?? "false")
            ];
        }
    }
}
