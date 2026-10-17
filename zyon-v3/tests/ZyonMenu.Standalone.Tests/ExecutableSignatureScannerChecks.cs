// C# / tests/ZyonMenu.Standalone.Tests/ExecutableSignatureScannerChecks.cs / .NET 8 / PE and page-boundary fixtures.
using System.Buffers.Binary;
using ZyonMenu.Windows;

namespace ZyonMenu.Standalone.Tests;

internal static class ExecutableSignatureScannerChecks
{
    public static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        var large = new ImageFixture(33 * 1024 * 1024 + 4096);
        large.Recoil(large.FirstSection + 32 * 1024 * 1024 + 64, 0x80);
        large.Spread(large.FirstSection + 32 * 1024 * 1024 + 128, 0x60);
        var scan = large.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset == 0x80 && scan.SpreadOffset == 0x60,
            "A section larger than 32 MiB must be scanned through its end instead of being skipped.");
        Check(scan.Telemetry.ExecutableSectionsRead == 1 && scan.Telemetry.ExecutableSectionsSkipped == 0,
            "A completely readable large section must be reported as complete.");
        Check(scan.Telemetry.BytesRead == large.SectionSize && large.LargestRead <= 65536,
            "Large sections must use bounded requests and count every executable byte once.");
        Check(scan.Telemetry.Sections[0].Name == ".text" && scan.Telemetry.Sections[0].VirtualSize == large.SectionSize,
            "The report must retain section layout for diagnosing the actual loaded PE image.");
        Check(scan.Telemetry.ModuleBase == ImageFixture.Base && scan.Telemetry.ImageSize == large.Bytes.Length &&
            scan.Telemetry.DeclaredImageSize == large.Bytes.Length && scan.Telemetry.OptionalHeaderMagic == 0x20B &&
            scan.Telemetry.DosPeOffset == ImageFixture.Pe && scan.Telemetry.Machine == 0x8664 &&
            scan.Telemetry.PeSignature == 0x00004550,
            "Raw header facts must identify both image extents, DOS PE offset, PE signature, machine and optional magic.");
        Check(scan.Telemetry.DosHeaderRead && scan.Telemetry.CoffHeaderRead && scan.Telemetry.OptionalHeaderAndSectionTableRead &&
            scan.Telemetry.DeclaredSectionCount == 1 && scan.Telemetry.DeclaredOptionalHeaderSize == 240 &&
            scan.Telemetry.SectionTableOffset == ImageFixture.Optional + 240 &&
            scan.Telemetry.SectionTableEnd == ImageFixture.Optional + 280 &&
            scan.Telemetry.HeaderBytesRead == 64 + 24 + 280,
            "The report must retain successful header reads and the actual section-table extent.");

        var smallerPe = new ImageFixture(8192);
        smallerPe.SetSectionUInt32(0, 8, 4096);
        smallerPe.SetSectionUInt32(0, 16, 4096);
        smallerPe.Write32(ImageFixture.Optional + 56, (uint)smallerPe.FirstSection + 4096);
        smallerPe.Recoil(smallerPe.FirstSection + 128, 0x80);
        scan = smallerPe.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset == 0x80 && scan.Telemetry.BytesRead == 4096,
            "A copied PE image smaller than the Windows host extent must be accepted when its whole section fits both bounds.");
        Check(scan.Telemetry.ImageSize == smallerPe.Bytes.Length && scan.Telemetry.DeclaredImageSize == 8192 &&
            scan.Telemetry.EffectiveImageSize == 8192 && smallerPe.LastReadEnd <= ImageFixture.Base + 8192 &&
            scan.Telemetry.ImageBoundsReason.Contains("distintos"),
            "Different host/PE extents must be retained; reads must remain inside their intersection.");

        var largerPe = new ImageFixture(4096);
        largerPe.Write32(ImageFixture.Optional + 56, (uint)largerPe.Bytes.Length + 4096);
        largerPe.Recoil(largerPe.FirstSection + 128, 0x90);
        scan = largerPe.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset == 0x90 && scan.Telemetry.BytesRead == 4096,
            "A larger copied PE SizeOfImage must not invalidate a complete executable section within the host extent.");
        Check(scan.Telemetry.DeclaredImageSize == largerPe.Bytes.Length + 4096 &&
            scan.Telemetry.EffectiveImageSize == largerPe.Bytes.Length && largerPe.OutOfFixtureReadAttempts == 0 &&
            largerPe.LastReadEnd <= ImageFixture.Base + (uint)largerPe.Bytes.Length,
            "A larger PE declaration must never extend the reader past the observed Windows host bounds.");

        var truncatedHost = new ImageFixture(8192);
        truncatedHost.Recoil(truncatedHost.FirstSection + 128, 0x80);
        uint observedSize = (uint)truncatedHost.Bytes.Length - 4096;
        scan = truncatedHost.Scan(observedSize);
        Check(!scan.Telemetry.Complete && scan.RecoilOffset is null && scan.Telemetry.BytesRead == 0,
            "An executable section crossing the host bound must fail before scanning, even when its initial candidate is readable.");
        Check(truncatedHost.LastReadEnd <= ImageFixture.Base + observedSize &&
            scan.Telemetry.CoverageReason.Contains("Windows") && scan.Telemetry.CoverageReason.Contains("PE"),
            "A truncated section must not cause out-of-host reads or a misleading generic size mismatch.");

        var transferredHeader = new ImageFixture(4096, 1, 0xF00);
        transferredHeader.Write32(transferredHeader.OptionalOffset + 60, 4096);
        transferredHeader.Recoil(transferredHeader.FirstSection + 128, 0xA0);
        scan = transferredHeader.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset == 0xA0 &&
            scan.Telemetry.DeclaredHeadersSize < scan.Telemetry.SectionTableEnd,
            "A transplanted NT header at the host DOS offset can place the actual section table beyond copied SizeOfHeaders.");
        Check(scan.Telemetry.ProtectedHeaderSize == scan.Telemetry.SectionTableEnd &&
            scan.Telemetry.DosPeOffset == 0xF00 && scan.Telemetry.HeaderBytesRead == 368,
            "Transferred headers must protect their actual table and retain the distinct declared header size.");
        var tableOverlap = new ImageFixture(4096, 1, 0xF00);
        tableOverlap.Write32(tableOverlap.OptionalOffset + 60, 4096);
        tableOverlap.SetSectionUInt32(0, 12, 4096);
        scan = tableOverlap.Scan();
        Check(!scan.Telemetry.Complete && scan.RecoilOffset is null && scan.Telemetry.BytesRead == 0,
            "A section overlapping the actual transferred table must fail even when it begins after copied SizeOfHeaders.");

        var boundary = new ImageFixture(3 * 65536);
        // This shorter pattern remains entirely inside the carry from the first block.
        boundary.Recoil(boundary.FirstSection + 65536 - 14, 0x90);
        boundary.Spread(boundary.FirstSection + 2 * 65536 - 8, 0x70);
        scan = boundary.Scan();
        Check(scan.Telemetry.RecoilMatchCount == 1 && scan.Telemetry.SpreadMatchCount == 1,
            "Overlap must neither duplicate an already complete short match nor lose a boundary-crossing match.");
        Check(scan.RecoilOffset == 0x90 && scan.SpreadOffset == 0x70,
            "Operands in contiguous block-boundary matches must remain intact.");

        var recovered = new ImageFixture(2 * 65536);
        recovered.Recoil(recovered.FirstSection + 4096 - 5, 0xA0);
        recovered.Spread(recovered.FirstSection + 65536 - 7, 0x68);
        recovered.Refuse = (_, length) => length > 4096;
        scan = recovered.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset == 0xA0 && scan.SpreadOffset == 0x68,
            "Readable fallback pages must preserve contiguous matches across page and chunk boundaries.");
        Check(scan.Telemetry.ReadFailures == 2 && scan.Telemetry.UnreadableBytes == 0,
            "Failed bulk requests recovered by pages must be recorded without claiming missing coverage.");
        Check(scan.Telemetry.BytesRead == recovered.SectionSize && scan.Telemetry.Sections[0].Reason.Contains("recuperada"),
            "Fallback bytes must be counted once and explicitly identified as recovered.");

        var hole = new ImageFixture(65536);
        int inaccessible = hole.FirstSection + 3 * 4096;
        hole.Recoil(hole.FirstSection + 256, 0x80);
        hole.Spread(hole.FirstSection + 5 * 4096, 0x50);
        // These fragments would become a false second match if bytes around the hole were concatenated.
        byte[] split = RecoilBytes(0xA0);
        split.AsSpan(0, 6).CopyTo(hole.Bytes.AsSpan(inaccessible - 6));
        split.AsSpan(6).CopyTo(hole.Bytes.AsSpan(inaccessible + 4096));
        hole.Refuse = (address, length) => address < ImageFixture.Base + (uint)inaccessible + 4096 &&
            ImageFixture.Base + (uint)inaccessible < address + (uint)length;
        scan = hole.Scan();
        Check(!scan.Telemetry.Complete && scan.RecoilOffset is null && scan.SpreadOffset is null,
            "A unique candidate from incomplete executable coverage must never authorize a field.");
        Check(scan.Telemetry.RecoilMatchCount == 1 && scan.Telemetry.SpreadMatchCount == 1,
            "Readable regions on both sides remain diagnostic without matching across an inaccessible page.");
        Check(scan.Telemetry.UnreadableBytes == 4096 && scan.Telemetry.BytesRead == hole.SectionSize - 4096,
            "The exact unreadable page coverage must be reported.");
        Check(scan.Telemetry.ExecutableSectionsSkipped == 1 && scan.Telemetry.Sections[0].ReadFailures == 2,
            "The report must distinguish the failed chunk and failed page from readable fallback pages.");
        Check(scan.Telemetry.RecoilReason.Contains("incompleta") && scan.Telemetry.SpreadReason.Contains("desativados"),
            "Incomplete coverage must not be described as an absent or unique signature.");

        var ambiguous = new ImageFixture(65536);
        ambiguous.Recoil(ambiguous.FirstSection + 100, 0x80);
        ambiguous.Recoil(ambiguous.FirstSection + 300, 0x80);
        ambiguous.Spread(ambiguous.FirstSection + 500, 0x80);
        scan = ambiguous.Scan();
        Check(scan.Telemetry.Complete && scan.Telemetry.RecoilMatchCount == 2 && scan.RecoilOffset is null,
            "Even identical extracted operands from two matches must remain ambiguous.");
        Check(scan.Telemetry.RecoilReason.Contains("ambígua") && scan.SpreadOffset is null,
            "Signed negative disp8 operands must not pass writable-field bounds.");
        for (int i = 0; i < 20; i++) ambiguous.Recoil(ambiguous.FirstSection + 1000 + i * 32, 0x80);
        scan = ambiguous.Scan();
        Check(scan.Telemetry.RecoilMatchCount == 22 && scan.Telemetry.RecoilCandidates.Count == 16 && scan.RecoilOffset is null,
            "Diagnostic candidates must stay bounded without hiding the true ambiguous match count.");

        var headers = new ImageFixture(4096, 96);
        headers.Recoil(headers.FirstSection + 95 * 4096 + 128, 0x100);
        scan = headers.Scan();
        Check(headers.FirstSection > 4096 && scan.Telemetry.Complete && scan.Telemetry.ExecutableSectionsRead == 96,
            "A valid section table beyond the first 4 KiB must be read within the bounded header limit.");
        Check(scan.RecoilOffset == 0x100, "The last valid executable section must be included.");

        var nonExecutable = new ImageFixture(4096, 2);
        nonExecutable.SetSectionUInt32(1, 36, 0x40000040);
        nonExecutable.Recoil(nonExecutable.FirstSection + 100, 0x80);
        nonExecutable.Recoil(nonExecutable.FirstSection + 4096 + 100, 0x90);
        scan = nonExecutable.Scan();
        Check(scan.RecoilOffset == 0x80 && scan.Telemetry.ExecutableSectionsRead == 1,
            "Data-section lookalikes must not count as executable signature matches.");

        var absent = new ImageFixture(4096);
        scan = absent.Scan();
        Check(scan.Telemetry.Complete && scan.RecoilOffset is null && scan.Telemetry.RecoilReason.Contains("leitura completa"),
            "A fully scanned image without matches must report absence with complete coverage.");
        absent.Recoil(absent.FirstSection + 100, 0x1001);
        Check(absent.Scan().RecoilOffset is null, "An out-of-profile disp32 must never authorize a writable field.");

        Invalid(image => image.Bytes[0] = 0, "Bad MZ");
        Invalid(image => image.Bytes[ImageFixture.Pe + 3] = 1, "All four PE signature bytes must match");
        Invalid(image => image.Write16(ImageFixture.Pe + 4, 0x14C), "Non-x64 image");
        Invalid(image => image.Write32(0x3C, uint.MaxValue), "Negative PE pointer");
        Invalid(image => image.Write32(0x3C, 65536), "PE pointer outside bounded header");
        Invalid(image => image.Write16(ImageFixture.Pe + 6, 97), "Too many sections");
        Invalid(image => image.Write16(ImageFixture.Pe + 6, 0), "No declared sections");
        Invalid(image => image.Write16(ImageFixture.Pe + 20, 100), "Truncated optional header");
        Invalid(image => image.Write16(ImageFixture.Optional, 0x10B), "Non-PE32+ optional header");
        Invalid(image => image.Write32(ImageFixture.Optional + 56, (uint)image.Bytes.Length - 1), "Section truncated by copied PE extent");
        Invalid(image => image.Write32(ImageFixture.Optional + 56, 0), "Zero PE image size");
        Invalid(image => image.Write32(ImageFixture.Optional + 56, uint.MaxValue), "Unbounded PE image size");
        Invalid(image => image.Write32(ImageFixture.Optional + 60, 63), "Truncated declared headers");
        Invalid(image => image.Write32(ImageFixture.Optional + 60, (uint)image.Bytes.Length + 1), "Headers outside image bounds");
        Invalid(image => image.SetSectionUInt32(0, 12, (uint)image.Bytes.Length - 1), "Section beyond image end");
        Invalid(image => image.SetSectionUInt32(0, 12, 0), "Section overlaps headers");
        Invalid(image => { image.SetSectionUInt32(0, 8, 0); image.SetSectionUInt32(0, 16, 0); }, "Empty executable section");
        Invalid(image => image.SetSectionUInt32(0, 36, 0x40000040), "No executable section");
        Invalid(image => image.Refuse = (address, _) => address == ImageFixture.Base, "Unreadable DOS header");
        var wrongMagic = new ImageFixture(4096);
        wrongMagic.Write16(ImageFixture.Optional, 0x10B);
        scan = wrongMagic.Scan();
        Check(scan.Telemetry.OptionalHeaderMagic == 0x10B && scan.Telemetry.OptionalHeaderAndSectionTableRead &&
            scan.Telemetry.CoverageReason.Contains("magic 0x10B"),
            "An invalid optional magic must retain the observed value and identify that exact failed check.");
        var unreadableTable = new ImageFixture(4096);
        unreadableTable.Refuse = (address, _) => address == ImageFixture.Base + ImageFixture.Optional;
        scan = unreadableTable.Scan();
        Check(scan.Telemetry.DosHeaderRead && scan.Telemetry.CoffHeaderRead &&
            !scan.Telemetry.OptionalHeaderAndSectionTableRead && scan.Telemetry.OptionalHeaderMagic is null &&
            scan.Telemetry.HeaderBytesRead == 88,
            "Unavailable optional/table bytes must remain distinct from successfully read but invalid PE values.");
        var overlap = new ImageFixture(4096, 2);
        overlap.SetSectionUInt32(1, 12, (uint)overlap.FirstSection + 2048);
        Check(!overlap.Scan().Telemetry.Complete && overlap.Scan().RecoilOffset is null,
            "Overlapping declared sections must fail before counting duplicated bytes or granting offsets.");
        var malformedRead = ExecutableSignatureScanner.Scan(ImageFixture.Base, 8192,
            (ulong _, int length, out byte[] bytes) => { bytes = new byte[length - 1]; return true; });
        Check(!malformedRead.Telemetry.Complete, "A reader's short successful transfer must be rejected.");
        Check(!ExecutableSignatureScanner.Scan(ulong.MaxValue, 8192, absent.Read).Telemetry.Complete,
            "An overflowing or non-user module interval must fail before reads.");
        Check(!ExecutableSignatureScanner.Scan(ImageFixture.Base, uint.MaxValue, absent.Read).Telemetry.Complete,
            "Unbounded mapped-image sizes must fail before allocation or scans.");
        return assertions;

        void Invalid(Action<ImageFixture> corrupt, string message)
        {
            var image = new ImageFixture(4096);
            image.Recoil(image.FirstSection + 100, 0x80);
            corrupt(image);
            var invalid = image.Scan();
            Check(!invalid.Telemetry.Complete && invalid.RecoilOffset is null && invalid.SpreadOffset is null,
                message + " must fail closed.");
        }
    }

    private static byte[] RecoilBytes(int operand)
    {
        byte[] bytes = [0x48, 0x8B, 0x47, 0x40, 0xF3, 0x0F, 0x10, 0xB0, 0, 0, 0, 0];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), operand);
        return bytes;
    }

    private sealed class ImageFixture
    {
        public const ulong Base = 0x140000000;
        public const int Pe = 0x80;
        public const int Optional = Pe + 24;
        private const int OptionalSize = 240;
        public byte[] Bytes { get; }
        public uint SectionSize { get; }
        public int FirstSection { get; }
        public int PeOffset { get; }
        public int OptionalOffset => PeOffset + 24;
        public int LargestRead { get; private set; }
        public ulong LastReadEnd { get; private set; }
        public int OutOfFixtureReadAttempts { get; private set; }
        public Func<ulong, int, bool>? Refuse { get; set; }

        public ImageFixture(int sectionSize, int count = 1, int peOffset = Pe)
        {
            PeOffset = peOffset;
            SectionSize = (uint)sectionSize;
            int tableEnd = OptionalOffset + OptionalSize + count * 40;
            FirstSection = (tableEnd + 4095) & ~4095;
            Bytes = new byte[FirstSection + sectionSize * count];
            Bytes[0] = (byte)'M'; Bytes[1] = (byte)'Z';
            Write32(0x3C, (uint)PeOffset);
            Write32(PeOffset, 0x00004550);
            Write16(PeOffset + 4, 0x8664);
            Write16(PeOffset + 6, (ushort)count);
            Write16(PeOffset + 20, OptionalSize);
            Write16(OptionalOffset, 0x20B);
            Write32(OptionalOffset + 56, (uint)Bytes.Length);
            Write32(OptionalOffset + 60, (uint)FirstSection);
            for (int index = 0; index < count; index++)
            {
                ".text"u8.CopyTo(Bytes.AsSpan(OptionalOffset + OptionalSize + index * 40));
                SetSectionUInt32(index, 8, (uint)sectionSize);
                SetSectionUInt32(index, 12, (uint)(FirstSection + index * sectionSize));
                SetSectionUInt32(index, 16, (uint)sectionSize);
                SetSectionUInt32(index, 36, 0x60000020);
            }
        }

        public void Write16(int index, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(index), value);
        public void Write32(int index, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(index), value);
        public void SetSectionUInt32(int index, int field, uint value) => Write32(OptionalOffset + OptionalSize + index * 40 + field, value);
        public void Recoil(int index, int value) => RecoilBytes(value).CopyTo(Bytes, index);
        public void Spread(int index, byte value) => new byte[]
            { 0xF3, 0x0F, 0x10, 0x43, value, 0xF3, 0x0F, 0x59, 0x05, 0, 0, 0, 0, 0xF3, 0x0F, 0x2C, 0xC0 }.CopyTo(Bytes, index);
        public ExecutableSignatureScanResult Scan(uint? observedSize = null) => ExecutableSignatureScanner.Scan(Base, observedSize ?? (uint)Bytes.Length, Read);

        public bool Read(ulong address, int length, out byte[] bytes)
        {
            LargestRead = Math.Max(LargestRead, length);
            if (length > 0) LastReadEnd = Math.Max(LastReadEnd, address + (uint)length);
            bytes = [];
            if (address < Base || length <= 0 || address - Base > (ulong)Bytes.Length ||
                (ulong)length > (ulong)Bytes.Length - (address - Base))
            {
                OutOfFixtureReadAttempts++;
                return false;
            }
            if (Refuse?.Invoke(address, length) == true) return false;
            bytes = Bytes.AsSpan((int)(address - Base), length).ToArray();
            return true;
        }
    }
}
