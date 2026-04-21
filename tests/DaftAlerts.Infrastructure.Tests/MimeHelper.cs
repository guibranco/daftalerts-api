using System.IO;
using System.Threading.Tasks;
using MimeKit;

namespace DaftAlerts.Infrastructure.Tests;

internal static class MimeHelper
{
    private static readonly string TestDataRoot =
        Path.Combine(Path.GetDirectoryName(typeof(MimeHelper).Assembly.Location)!, "TestData");

    public static async Task<MimeMessage> LoadAsync(string filename)
    {
        var path = Path.Combine(TestDataRoot, filename);
        await using var fs = File.OpenRead(path);
        return await MimeMessage.LoadAsync(fs);
    }

    public static byte[] LoadBytes(string filename)
    {
        var path = Path.Combine(TestDataRoot, filename);
        return File.ReadAllBytes(path);
    }
}
