namespace RPA.Infrastructure.Workflow;

/// <summary>
/// Basit SemVer (major.minor.patch) ayrıştırma ve karşılaştırma. Pre-release/build
/// meta verileri göz ardı edilir (Spec Bölüm 5.4 versiyon pinleme için yeterli).
/// </summary>
public readonly struct SemanticVersion : IComparable<SemanticVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    public SemanticVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>"1.2.3" biçimini ayrıştırır. Eksik parçalar 0 kabul edilir; hatalıysa false.</summary>
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Pre-release/build kısmını at: "1.2.3-rc1+meta" → "1.2.3"
        var core = value.Split('-', '+')[0];
        var parts = core.Split('.');
        int major = 0, minor = 0, patch = 0;

        if (parts.Length > 0 && !TryPart(parts[0], out major)) return false;
        if (parts.Length > 1 && !TryPart(parts[1], out minor)) return false;
        if (parts.Length > 2 && !TryPart(parts[2], out patch)) return false;

        version = new SemanticVersion(major, minor, patch);
        return true;

        static bool TryPart(string s, out int n) => int.TryParse(s, out n) && n >= 0;
    }

    public int CompareTo(SemanticVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
