using System.Text.RegularExpressions;

namespace Nexora.Domain.Companies;

/// <summary>Format and checksum validation for Indian PAN and GSTIN. No online verification is performed.</summary>
public static partial class TaxIdentifiers
{
    private const string GstinAlphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// PAN: five letters, four digits, one letter. The fourth letter is the holder type
    /// (P person, C company, H HUF, F firm, A AOP, T trust, B BOI, L local authority, J juridical person, G government).
    /// </summary>
    public static bool IsValidPan(string? pan) => pan is not null && Pan().IsMatch(pan);

    /// <summary>
    /// GSTIN: two-digit state code, the holder's PAN, entity number, the letter Z by default,
    /// and a mod-36 check character.
    /// </summary>
    public static bool IsValidGstin(string? gstin)
    {
        if (gstin is null || !Gstin().IsMatch(gstin)) return false;
        if (!IndianState.IsValidCode(gstin[..2])) return false;
        if (!IsValidPan(gstin.Substring(2, 10))) return false;
        return GstinCheckCharacter(gstin) == gstin[14];
    }

    public static string StateCodeOfGstin(string gstin) => gstin[..2];

    public static string PanOfGstin(string gstin) => gstin.Substring(2, 10);

    /// <summary>Computes the check character from the first 14 characters.</summary>
    public static char GstinCheckCharacter(string gstin)
    {
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var product = GstinAlphabet.IndexOf(gstin[i]) * (i % 2 == 0 ? 1 : 2);
            sum += product / 36 + product % 36;
        }

        return GstinAlphabet[(36 - sum % 36) % 36];
    }

    [GeneratedRegex("^[A-Z]{3}[ABCFGHLJPT][A-Z][0-9]{4}[A-Z]$")]
    private static partial Regex Pan();

    [GeneratedRegex("^[0-9]{2}[A-Z0-9]{10}[1-9A-Z]Z[0-9A-Z]$")]
    private static partial Regex Gstin();
}
