using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ZyonMenu.Standalone.Tests;

internal static class EmbeddedClientModuleChecks
{
    // Inspect metadata and resource bytes without loading Windows code.
    public static int Verify(string managedAssembly, string nativeModule)
    {
        try
        {
            using var input = File.OpenRead(managedAssembly);
            using var pe = new PEReader(input);
            var metadata = pe.GetMetadataReader();
            var resources = metadata.ManifestResources.Select(metadata.GetManifestResource)
                .Where(resource => metadata.GetString(resource.Name) == "ZyonMenu.ClientModule.asi").ToArray();
            if (resources.Length != 1 || !resources[0].Implementation.IsNil)
                throw new InvalidDataException("The desktop assembly must embed exactly one client module.");
            var directory = pe.PEHeaders.CorHeader?.ResourcesDirectory
                ?? throw new InvalidDataException("Missing CLR resource directory.");
            var data = pe.GetSectionData(directory.RelativeVirtualAddress).GetContent(0, directory.Size).AsSpan();
            int offset = checked((int)resources[0].Offset);
            if (offset < 0 || offset > data.Length - sizeof(int)) throw new InvalidDataException("Invalid module resource offset.");
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
            if (length < 0 || length > data.Length - offset - sizeof(int) ||
                !data.Slice(offset + sizeof(int), length).SequenceEqual(File.ReadAllBytes(nativeModule)))
                throw new InvalidDataException("Embedded native bytes differ from the verified native build.");
            Console.WriteLine($"PASS embedded client module: {length} bytes match the native build.");
            return 0;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            Console.Error.WriteLine("FAIL embedded client module: " + error.Message);
            return 1;
        }
    }
}
