using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using StringSpy.Models;
using Xunit.Abstractions;

namespace StringSpyTest;

public class StringsFinderTest(ITestOutputHelper output)
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class TestFile(
        string path,
        int? ascii = null,
        int? utf8 = null,
        int? utf16le = null,
        int? utf16be = null,
        int? utf32le = null,
        int? utf32be = null
    )
    {
        public string Path => path;

        public readonly Dictionary<Encoding, int?> Encodings = new()
        {
            [Encoding.ASCII] = ascii,
            [Encoding.UTF8] = utf8,
            [Encoding.Unicode] = utf16le,
            [Encoding.BigEndianUnicode] = utf16be,
            [Encoding.UTF32] = utf32le,
        };

        public override string ToString()
        {
            return System.IO.Path.GetFileName(Path);
        }
    };

    private static string GetTestDataPath(string fileName)
    {
        var codeBaseUrl = new Uri(Assembly.GetExecutingAssembly().Location);
        string codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
        string? dirPath = Path.GetDirectoryName(codeBasePath);
        if (dirPath == null) throw new DirectoryNotFoundException("Application directory could not be found.");
        return Path.Combine(dirPath, "TestData", fileName);
    }

    public static IEnumerable<object[]> TestFiles =>
    [
        [new TestFile(GetTestDataPath("ascii.txt"), 611, 355, 24, 83, 0, 0)],
        [new TestFile(GetTestDataPath("ascii_random.txt"), 1573, 272, 1, 1, 0, 0)],
        [new TestFile(GetTestDataPath("utf8.txt"), 619, 372, 56, 107, 0, 0)],
        [new TestFile(GetTestDataPath("utf8_random.txt"), 0, 635, 77, 76, 0, 0)],
        [new TestFile(GetTestDataPath("utf16le.txt"), 67, 102, 665, 876, 0, 0)],
        [new TestFile(GetTestDataPath("utf16le_random.txt"), 2379, 7055, 531, 524, 0, 0)],
        [new TestFile(GetTestDataPath("utf16be.txt"), 63, 85, 796, 633, 0, 0)],
        [new TestFile(GetTestDataPath("utf16be_random.txt"), 2341, 7073, 528, 536, 0, 0)],
    ];

    [Theory(DisplayName = "String counts equal expected counts")]
    [MemberData(nameof(TestFiles))]
    public void TestStringCountsEqualExpectedCounts(TestFile file)
    {
        foreach ((Encoding encoding, int? expectedCount) in file.Encodings)
        {
            if (expectedCount == null) continue;
            var results =
                new StringsFinder(file.Path, encoding, CharSet.CurrentEncoding).FindStrings(useMemoryMappedFile: false);
            Assert.Equal(expectedCount, results.Count);
        }
    }

    [Theory(DisplayName = "ASCII string counts are equal across thread counts")]
    [MemberData(nameof(TestFiles))]
    public void TestAsciiStringCountsAreEqualAcrossThreadCounts(TestFile file)
    {
        const int numThreads = 8;
        var counts = new int[numThreads];
        for (var i = 0; i < numThreads; i++)
        {
            counts[i] = new StringsFinder(file.Path, Encoding.ASCII, CharSet.CurrentEncoding,
                threadCount: i + 1).FindStrings().Count;
        }

        Assert.True(counts.All(x => x == counts[0]),
            $"ASCII string counts are not equal across different thread counts. \n" +
            $"Counts = {string.Join(',', counts)}");
    }

    [Theory(DisplayName = "String counts are similar across thread counts")]
    [MemberData(nameof(TestFiles))]
    public void TestStringCountsAreSimilarAcrossThreadCounts(TestFile file)
    {
        foreach (Encoding encoding in file.Encodings.Keys)
        {
            const int numThreads = 6;
            var counts = new int[numThreads];
            for (var i = 0; i < numThreads; i++)
            {
                counts[i] = new StringsFinder(file.Path, encoding, CharSet.CurrentEncoding,
                    threadCount: i + 1).FindStrings().Count;
            }

            Assert.True(counts.All(x => Math.Abs(x - counts[0]) < encoding.GetMaxByteCount(1) * 3),
                $"String counts are not similar across different thread counts. \n" +
                $"Encoding = {encoding.EncodingName}, Counts = {string.Join(',', counts)}");
        }
    }

    [Theory(DisplayName = "Stream types produce the same string counts")]
    [MemberData(nameof(TestFiles))]
    public void TestStreamTypesProduceSameStringCounts(TestFile file)
    {
        int memoryMappedCount = new StringsFinder(file.Path, Encoding.ASCII, CharSet.CurrentEncoding)
            .FindStrings(useMemoryMappedFile: true).Count;
        int fileStreamCount = new StringsFinder(file.Path, Encoding.ASCII, CharSet.CurrentEncoding)
            .FindStrings(useMemoryMappedFile: false).Count;
        Assert.Equal(fileStreamCount, memoryMappedCount);
    }
}