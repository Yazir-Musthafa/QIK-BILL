namespace Nexora.Application.Inventory;

/// <summary>
/// Tells a barcode scanner from a person typing. USB scanners act as keyboards: they type the code very fast and finish
/// with Enter. A burst counts as a scan when it has at least <see cref="MinimumLength"/> characters, every gap between
/// keys is at most <see cref="MaximumGap"/>, and it ends with Enter. Anything slower is treated as typing and ignored.
/// The detector only watches keys; it never takes focus, so the screen decides what to do with a scan.
/// </summary>
public sealed class ScanDetector
{
    private readonly List<char> _buffer = [];
    private DateTime _lastKey = DateTime.MinValue;
    private bool _tooSlow;

    public int MinimumLength { get; init; } = 4;

    public TimeSpan MaximumGap { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Feeds a printable character typed at <paramref name="at"/>.</summary>
    public void Character(char character, DateTime at)
    {
        if (char.IsControl(character)) return;
        if (_buffer.Count > 0 && at - _lastKey > MaximumGap) _tooSlow = true;
        if (_tooSlow)
        {
            // Start over from this key: a scan may begin right after slow typing.
            _buffer.Clear();
            _tooSlow = false;
        }

        if (_buffer.Count < 200) _buffer.Add(character);
        _lastKey = at;
    }

    /// <summary>Feeds Enter. Returns the scanned code when the burst was a scan; otherwise null. Always resets.</summary>
    public string? Enter(DateTime at)
    {
        try
        {
            var fast = _buffer.Count > 0 && at - _lastKey <= MaximumGap;
            return fast && _buffer.Count >= MinimumLength ? new string([.. _buffer]).Trim() : null;
        }
        finally
        {
            Reset();
        }
    }

    public void Reset()
    {
        _buffer.Clear();
        _tooSlow = false;
        _lastKey = DateTime.MinValue;
    }
}
