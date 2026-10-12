using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexora.Application;

namespace Nexora.Infrastructure.Backup;

/// <summary>Facts stored in a backup's header, readable before decryption.</summary>
public sealed record BackupHeader(int Format, string AppVersion, DateTime CreatedUtc, string LatestMigration, IReadOnlyList<string> Migrations,
    IReadOnlyList<string> Companies, bool Encrypted, int KdfIterations, string SaltBase64, string NoncePrefixBase64, int ChunkSize, string PayloadSha256);

/// <summary>
/// The QIK Suite backup file, version 1. Layout:
/// <code>
/// "QIKBAK1\n"                      8 bytes, magic
/// header length                    4 bytes, little-endian
/// header                           UTF-8 JSON (<see cref="BackupHeader"/>)
/// payload                          the SQLite database, gzip-compressed;
///                                  when encrypted, a series of chunks [4-byte length][ciphertext][16-byte tag], AES-256-GCM
/// file hash                        32 bytes, SHA-256 of everything above
/// </code>
/// Encryption: the key is PBKDF2-HMAC-SHA256 of the password with a random 16-byte salt and <see cref="Iterations"/>
/// iterations. Chunk i (1 MB of compressed data) uses nonce = 4-byte random prefix + 8-byte big-endian i, and
/// authenticated data = header bytes + i + a final-chunk flag, so chunks cannot be reordered, dropped or moved
/// between files, and the header cannot change. <see cref="BackupHeader.PayloadSha256"/> is the SHA-256 of the
/// compressed database, checked after decryption. Without a password the file hash detects corruption but not a
/// deliberate edit by someone who recomputes it.
/// </summary>
public static class BackupFormat
{
    public const int Version = 1;
    public const int Iterations = 600_000;
    public const int ChunkSize = 1024 * 1024;
    private static readonly byte[] Magic = "QIKBAK1\n"u8.ToArray();
    private const int TagSize = 16;

    public static void Write(Stream output, BackupHeader header, string compressedPath, string? password, int iterations = Iterations)
    {
        string payloadHash;
        using (var compressed = File.OpenRead(compressedPath)) payloadHash = Convert.ToHexString(SHA256.HashData(compressed));
        byte[] salt = [], prefix = [];
        if (password is not null)
        {
            salt = RandomNumberGenerator.GetBytes(16);
            prefix = RandomNumberGenerator.GetBytes(4);
        }

        header = header with
        {
            Format = Version, Encrypted = password is not null, KdfIterations = password is null ? 0 : iterations, SaltBase64 = Convert.ToBase64String(salt),
            NoncePrefixBase64 = Convert.ToBase64String(prefix), ChunkSize = ChunkSize, PayloadSha256 = payloadHash,
        };
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Put(ReadOnlySpan<byte> bytes)
        {
            output.Write(bytes);
            hasher.AppendData(bytes);
        }

        Put(Magic);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, headerBytes.Length);
        Put(length);
        Put(headerBytes);

        using (var source = File.OpenRead(compressedPath))
        {
            if (password is null)
            {
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer)) > 0) Put(buffer.AsSpan(0, read));
            }
            else
            {
                var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
                using var aes = new AesGcm(key, TagSize);
                var plain = new byte[ChunkSize];
                long index = 0;
                var total = source.Length;
                long done = 0;
                do
                {
                    var read = source.ReadAtLeast(plain, (int)Math.Min(ChunkSize, total - done), throwOnEndOfStream: false);
                    done += read;
                    var final = done >= total;
                    var cipher = new byte[read];
                    var tag = new byte[TagSize];
                    aes.Encrypt(Nonce(prefix, index), plain.AsSpan(0, read), cipher, tag, Aad(headerBytes, index, final));
                    BinaryPrimitives.WriteInt32LittleEndian(length, read);
                    Put(length);
                    Put(cipher);
                    Put(tag);
                    index++;
                }
                while (done < total);
            }
        }

        output.Write(hasher.GetHashAndReset());
    }

    /// <summary>Reads only the header (no password needed), after checking the magic and the file hash.</summary>
    public static BackupHeader ReadHeader(string path)
    {
        using var input = File.OpenRead(path);
        var (header, _, _) = Open(input);
        return header;
    }

    /// <summary>Checks the file hash, decrypts and checks the payload hash, and writes the decompressed database to <paramref name="databasePath"/>.</summary>
    public static BackupHeader Extract(string path, string? password, string databasePath)
    {
        using var input = File.OpenRead(path);
        var (header, headerBytes, payloadStart) = Open(input);
        if (header.Encrypted && string.IsNullOrEmpty(password)) throw new WorkspaceValidationException("This backup is encrypted. Enter its password.");
        var compressedPath = databasePath + ".gz";
        try
        {
            input.Position = payloadStart;
            var payloadEnd = input.Length - 32;
            using (var compressed = File.Create(compressedPath))
            {
                if (!header.Encrypted)
                {
                    CopyRange(input, compressed, payloadEnd - payloadStart);
                }
                else
                {
                    var key = Rfc2898DeriveBytes.Pbkdf2(password!, Convert.FromBase64String(header.SaltBase64), header.KdfIterations, HashAlgorithmName.SHA256, 32);
                    using var aes = new AesGcm(key, TagSize);
                    var prefix = Convert.FromBase64String(header.NoncePrefixBase64);
                    long index = 0;
                    var lengthBytes = new byte[4];
                    while (input.Position < payloadEnd)
                    {
                        input.ReadExactly(lengthBytes);
                        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                        if (length < 0 || length > header.ChunkSize || input.Position + length + TagSize > payloadEnd) throw Damaged();
                        var cipher = new byte[length];
                        var tag = new byte[TagSize];
                        input.ReadExactly(cipher);
                        input.ReadExactly(tag);
                        var final = input.Position == payloadEnd;
                        var plain = new byte[length];
                        try
                        {
                            aes.Decrypt(Nonce(prefix, index), cipher, tag, plain, Aad(headerBytes, index, final));
                        }
                        catch (AuthenticationTagMismatchException)
                        {
                            throw new WorkspaceValidationException(index == 0
                                ? "The password is wrong, or the backup has been changed."
                                : "The backup has been changed or damaged after it was made.");
                        }

                        compressed.Write(plain);
                        index++;
                    }
                }
            }

            using (var check = File.OpenRead(compressedPath))
                if (Convert.ToHexString(SHA256.HashData(check)) != header.PayloadSha256) throw Damaged();
            using (var source = new GZipStream(File.OpenRead(compressedPath), CompressionMode.Decompress))
            using (var target = File.Create(databasePath))
            {
                try
                {
                    source.CopyTo(target);
                }
                catch (InvalidDataException)
                {
                    throw Damaged();
                }
            }

            return header;
        }
        finally
        {
            File.Delete(compressedPath);
        }
    }

    public static void Compress(string databasePath, string compressedPath)
    {
        using var source = File.OpenRead(databasePath);
        using var target = new GZipStream(File.Create(compressedPath), CompressionLevel.Optimal);
        source.CopyTo(target);
    }

    private static (BackupHeader Header, byte[] HeaderBytes, long PayloadStart) Open(FileStream input)
    {
        if (input.Length < Magic.Length + 4 + 32) throw NotABackup();
        var magic = new byte[Magic.Length];
        input.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw NotABackup();

        // The file hash covers every byte before it.
        input.Position = 0;
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            var remaining = input.Length - 32;
            while (remaining > 0)
            {
                var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw Damaged();
                hasher.AppendData(buffer, 0, read);
                remaining -= read;
            }

            var stored = new byte[32];
            input.ReadExactly(stored);
            if (!CryptographicOperations.FixedTimeEquals(hasher.GetHashAndReset(), stored)) throw Damaged();
        }

        input.Position = Magic.Length;
        var lengthBytes = new byte[4];
        input.ReadExactly(lengthBytes);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > 1_000_000 || Magic.Length + 4 + length > input.Length - 32) throw Damaged();
        var headerBytes = new byte[length];
        input.ReadExactly(headerBytes);
        BackupHeader header;
        try
        {
            header = JsonSerializer.Deserialize<BackupHeader>(headerBytes) ?? throw Damaged();
        }
        catch (JsonException)
        {
            throw Damaged();
        }

        if (header.Format > Version) throw new WorkspaceValidationException("This backup was made by a newer version of QIK Suite. Update QIK Suite to use it.");
        return (header, headerBytes, input.Position);
    }

    private static byte[] Nonce(byte[] prefix, long index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), index);
        return nonce;
    }

    private static byte[] Aad(byte[] header, long index, bool final)
    {
        var aad = new byte[header.Length + 9];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteInt64BigEndian(aad.AsSpan(header.Length), index);
        aad[^1] = final ? (byte)1 : (byte)0;
        return aad;
    }

    private static void CopyRange(Stream input, Stream output, long count)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0) throw Damaged();
            output.Write(buffer, 0, read);
            count -= read;
        }
    }

    private static WorkspaceValidationException NotABackup() => new("This is not a QIK Suite backup file.");

    private static WorkspaceValidationException Damaged() => new("The backup file is damaged or has been changed. Use another backup.");
}
