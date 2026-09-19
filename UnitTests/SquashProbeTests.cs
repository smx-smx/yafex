using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using DiscUtils.SquashFs;

using Smx.SharpIO.Memory.Buffers;

using Yafex;
using Yafex.FileFormats;
using Yafex.FileFormats.EpkV2;
using Yafex.Support;

namespace UnitTests;

[TestFixture]
[Explicit]
public class SquashProbeTests
{
    // Manual probe against a real EPK on local storage (never runs in CI:
    // set these two env vars to run it explicitly).
    private static readonly string EPK =
        Environment.GetEnvironmentVariable("YAFEX_TEST_EPK") ?? string.Empty;
    private static readonly string SECRETS =
        Environment.GetEnvironmentVariable("YAFEX_TEST_SECRETS") ?? string.Empty;

    [Test]
    public void ProbeBasePak()
    {
        if (EPK.Length == 0 || SECRETS.Length == 0
            || !File.Exists(EPK) || !File.Exists(SECRETS))
        {
            Assert.Ignore("YAFEX_TEST_EPK/YAFEX_TEST_SECRETS not set; skipping manual probe.");
        }
        var keysRepo = new KeysRepository(new KeyBundle(SECRETS));
        using var mf = new MFile(EPK);

        var res = new Epk2Detector(keysRepo).Detect(mf);
        Assert.That(res, Is.InstanceOf<Epk2DetectionResult<EPK_V2_HEADER>>());
        var ctx = ((Epk2DetectionResult<EPK_V2_HEADER>)res).Context!;

        var images = Epk2SegmentWalker.BuildPakImages(
            mf.Data.AsReadonlySpan(), ctx, ctx.Header);

        var basePak = images.First(i => i.Image.Name == "base.pak").Image;
        TestContext.Progress.WriteLine($"base.pak length = {basePak.Length}");
        basePak.Source = new MappedBlockSource(mf.Data);

        using var fs = new SquashFileSystemReader(basePak.OpenStream());

        void Walk(string path, int depth)
        {
            if (depth > 3) return;
            string[] names;
            try
            {
                names = fs.GetFileSystemEntries(path);
            }
            catch (Exception ex)
            {
                TestContext.Progress.WriteLine($"[{path}] FAILED: {ex.Message}");
                return;
            }

            foreach (var full in names)
            {
                var name = full.Split('\\').Last();
                if (name is "." or ".." or "") continue;
                FileAttributes attrs;
                try
                {
                    attrs = fs.GetAttributes(full);
                }
                catch (Exception ex)
                {
                    TestContext.Progress.WriteLine($"{new string(' ', depth * 2)}{name} GETATTR FAILED: {ex.Message}");
                    continue;
                }
                var isDir = (attrs & FileAttributes.Directory) != 0;
                var isReparse = (attrs & FileAttributes.ReparsePoint) != 0;
                TestContext.Progress.WriteLine($"{new string(' ', depth * 2)}{name} dir={isDir} reparse={isReparse}");
                if (isDir && !isReparse)
                {
                    Walk(full, depth + 1);
                }
            }
        }

        Walk("\\", 0);
    }
}
