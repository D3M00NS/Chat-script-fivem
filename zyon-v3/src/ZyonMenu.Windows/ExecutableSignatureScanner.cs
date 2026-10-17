// C# / src/ZyonMenu.Windows/ExecutableSignatureScanner.cs / .NET 8 / bounded, read-only PE64 signature coverage.
using System.Buffers.Binary;
using System.Text;
using ZyonMenu.Core;

namespace ZyonMenu.Windows;

internal delegate bool ExecutableImageReader(ulong address, int length, out byte[] bytes);

internal sealed class ExecutableSignatureScanResult
{
    public SignatureTelemetry Telemetry { get; } = new();
    // A unique match in only the readable part cannot establish uniqueness across the image.
    public int? RecoilOffset => Telemetry.Complete && Telemetry.RecoilMatchCount == 1
        ? ExternalReadback.UniqueFieldOffset(Telemetry.RecoilCandidates) : null;
    public int? SpreadOffset => Telemetry.Complete && Telemetry.SpreadMatchCount == 1
        ? ExternalReadback.UniqueFieldOffset(Telemetry.SpreadCandidates) : null;
}

internal static class ExecutableSignatureScanner
{
    private const int ChunkSize = 64 * 1024;
    private const int PageSize = 4096;
    private const uint MaximumImageSize = 1024 * 1024 * 1024;
    private const int MaximumHeaderSize = 64 * 1024;
    private const int MaximumStoredCandidates = 16;
    private static readonly BytePattern RecoilPattern = BytePattern.Parse("48 8B 47 40 F3 0F 10 B0 ?? ?? ?? ??");
    private static readonly BytePattern SpreadPattern = BytePattern.Parse("F3 0F 10 43 ?? F3 0F 59 05 ?? ?? ?? ?? F3 0F 2C C0");

    public static ExecutableSignatureScanResult Scan(ulong moduleBase, uint imageSize, ExecutableImageReader read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var result = new ExecutableSignatureScanResult();
        var telemetry = result.Telemetry;
        telemetry.ImageSize = imageSize;
        if (imageSize < 64 || imageSize > MaximumImageSize || moduleBase < 0x10000 ||
            moduleBase > 0x00007FFFFFFFFFFF || imageSize - 1UL > 0x00007FFFFFFFFFFF - moduleBase)
            return Failure("O intervalo do módulo está fora dos limites de análise.");

        if (!ReadImage(0, 64, out var dos)) return Failure("O cabeçalho DOS não pôde ser lido.");
        if (dos[0] != 'M' || dos[1] != 'Z') return Failure("O cabeçalho MZ do executável não foi confirmado.");
        int pe = BinaryPrimitives.ReadInt32LittleEndian(dos.AsSpan(0x3C, 4));
        if (pe < 64 || pe > MaximumHeaderSize - 24 || !ReadImage((uint)pe, 24, out var coff))
            return Failure("O endereço ou a leitura do cabeçalho PE é inválido.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(coff) != 0x00004550 ||
            BinaryPrimitives.ReadUInt16LittleEndian(coff.AsSpan(4, 2)) != 0x8664)
            return Failure("O cabeçalho PE x64 do executável não foi confirmado.");
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(coff.AsSpan(6, 2));
        int optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(coff.AsSpan(20, 2));
        int tableSize = sectionCount * 40;
        int tableEnd = pe + 24 + optionalSize + tableSize;
        if (sectionCount is < 1 or > 96 || optionalSize < 112 || tableEnd > MaximumHeaderSize ||
            !ReadImage((uint)(pe + 24), optionalSize + tableSize, out var headers))
            return Failure("O tamanho ou a leitura da tabela de seções PE é inválido.");
        uint declaredImageSize = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(56, 4));
        uint headersSize = BinaryPrimitives.ReadUInt32LittleEndian(headers.AsSpan(60, 4));
        if (BinaryPrimitives.ReadUInt16LittleEndian(headers) != 0x020B || declaredImageSize != imageSize ||
            headersSize < tableEnd || headersSize > imageSize)
            return Failure("O cabeçalho PE32+ ou os limites da imagem não correspondem ao módulo carregado.");

        var ranges = new List<(uint Start, uint Size)>();
        for (int index = 0; index < sectionCount; index++)
        {
            var entry = headers.AsSpan(optionalSize + index * 40, 40);
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));
            uint rva = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(12, 4));
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(16, 4));
            uint size = Math.Max(virtualSize, rawSize);
            bool executable = (BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(36, 4)) & 0x20000000) != 0;
            if (size == 0 && !executable) continue;
            if (size == 0 || rva < headersSize || rva > imageSize || size > imageSize - rva ||
                ranges.Any(range => (ulong)rva < (ulong)range.Start + range.Size && (ulong)range.Start < (ulong)rva + size))
                return Failure("Uma seção PE tem limites inválidos ou sobrepostos; nenhum campo foi autorizado.");
            ranges.Add((rva, size));
            if (!executable) continue;
            int nameEnd = entry[..8].IndexOf((byte)0);
            telemetry.Sections.Add(new SignatureSectionTelemetry
            {
                Name = Encoding.ASCII.GetString(entry[..(nameEnd < 0 ? 8 : nameEnd)]),
                Rva = rva, VirtualSize = virtualSize, RawSize = rawSize, ScanSize = size
            });
        }
        if (telemetry.Sections.Count == 0) return Failure("A imagem PE não declara seções executáveis para análise.");

        foreach (var section in telemetry.Sections)
        {
            byte[] tail = [];
            ulong start = moduleBase + section.Rva;
            ulong end = start + section.ScanSize;
            for (ulong cursor = start; cursor < end;)
            {
                int length = (int)Math.Min((ulong)ChunkSize, end - cursor);
                if (TryRead(cursor, length, out var bytes)) Feed(bytes);
                else
                {
                    // Retry the same bounded interval page by page. A single inaccessible page must
                    // not discard the rest of a large executable section or bridge an unreadable gap.
                    ulong chunkEnd = cursor + (uint)length;
                    for (ulong page = cursor; page < chunkEnd;)
                    {
                        int pageLength = (int)Math.Min((ulong)PageSize - page % PageSize, chunkEnd - page);
                        if (TryRead(page, pageLength, out var pageBytes)) Feed(pageBytes);
                        else
                        {
                            section.UnreadableBytes += pageLength;
                            tail = [];
                        }
                        page += (uint)pageLength;
                    }
                }
                cursor += (uint)length;
            }
            section.Complete = section.UnreadableBytes == 0 && section.BytesRead == section.ScanSize;
            section.Reason = section.Complete
                ? section.ReadFailures == 0 ? "Seção lida por completo." : "Seção recuperada por completo com leituras por página."
                : $"Leitura incompleta: {section.UnreadableBytes} bytes inacessíveis; assinaturas não autorizadas.";
            if (section.Complete) telemetry.ExecutableSectionsRead++;
            else telemetry.ExecutableSectionsSkipped++;
            telemetry.BytesRead += section.BytesRead;
            telemetry.UnreadableBytes += section.UnreadableBytes;
            telemetry.ReadFailures += section.ReadFailures;

            bool TryRead(ulong address, int length, out byte[] bytes)
            {
                if (read(address, length, out bytes) && bytes.Length == length) return true;
                section.ReadFailures++;
                bytes = [];
                return false;
            }

            void Feed(byte[] bytes)
            {
                int priorLength = tail.Length;
                byte[] combined = new byte[priorLength + bytes.Length];
                tail.CopyTo(combined, 0);
                bytes.CopyTo(combined, priorLength);
                foreach (int match in RecoilPattern.FindAll(combined))
                {
                    if (match + RecoilPattern.Length <= priorLength) continue;
                    telemetry.RecoilMatchCount++;
                    if (telemetry.RecoilCandidates.Count < MaximumStoredCandidates)
                        telemetry.RecoilCandidates.Add(BinaryPrimitives.ReadInt32LittleEndian(combined.AsSpan(match + 8, 4)));
                }
                foreach (int match in SpreadPattern.FindAll(combined))
                {
                    if (match + SpreadPattern.Length <= priorLength) continue;
                    telemetry.SpreadMatchCount++;
                    if (telemetry.SpreadCandidates.Count < MaximumStoredCandidates)
                        telemetry.SpreadCandidates.Add(ExternalReadback.SignedDisplacement8(combined[match + 4]));
                }
                int retained = Math.Min(Math.Max(RecoilPattern.Length, SpreadPattern.Length) - 1, combined.Length);
                tail = combined.AsSpan(combined.Length - retained).ToArray();
                section.BytesRead += bytes.Length;
            }
        }

        telemetry.Complete = telemetry.ExecutableSectionsSkipped == 0;
        telemetry.CoverageReason = telemetry.Complete
            ? $"Cobertura completa: {telemetry.BytesRead} bytes em {telemetry.ExecutableSectionsRead} seções executáveis."
            : $"Cobertura incompleta: {telemetry.UnreadableBytes} bytes inacessíveis em {telemetry.ExecutableSectionsSkipped} seções; campos desativados.";
        telemetry.RecoilReason = Reason(telemetry.RecoilMatchCount, result.RecoilOffset);
        telemetry.SpreadReason = Reason(telemetry.SpreadMatchCount, result.SpreadOffset);
        return result;

        bool ReadImage(uint rva, int length, out byte[] bytes)
        {
            bytes = [];
            return length > 0 && rva <= imageSize && (uint)length <= imageSize - rva &&
                read(moduleBase + rva, length, out bytes) && bytes.Length == length;
        }

        ExecutableSignatureScanResult Failure(string reason)
        {
            telemetry.CoverageReason = reason;
            telemetry.RecoilReason = reason;
            telemetry.SpreadReason = reason;
            return result;
        }

        string Reason(int count, int? offset) => !telemetry.Complete
            ? telemetry.CoverageReason
            : offset is not null ? $"Assinatura única; deslocamento 0x{offset:X} resolvido no executável."
            : count > 1 ? $"Assinatura ambígua: {count} ocorrências; campo desativado."
            : count == 1 ? "O deslocamento extraído está fora do intervalo permitido; campo desativado."
            : $"Assinatura ausente após a leitura completa de {telemetry.ExecutableSectionsRead} seções executáveis ({telemetry.BytesRead} bytes).";
    }
}
