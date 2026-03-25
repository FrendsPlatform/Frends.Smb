using Frends.Smb.MoveFiles.Definitions;
using NUnit.Framework;

namespace Frends.Smb.MoveFiles.Tests;

[TestFixture]
public class PathStringTests
{
    [Test]
    public void ImplicitConversion_Normalizes_WithDefaultBackslashSeparator()
    {
        PathString.Setup(Separator.Backslash);

        PathString path = @"my/disk\test.txt";

        Assert.That(path, Is.EqualTo(@"my\disk\test.txt"));
    }

    [Test]
    public void SetupSeparatorToSlash_NormalizesAllSeparatorsToSlash()
    {
        PathString.Setup(Separator.Slash);

        PathString path = @"my\disk/test.txt";

        Assert.That(path, Is.EqualTo("my/disk/test.txt"));
    }

    [Test]
    public void ReassigningValue_NormalizesOnEachAssignment()
    {
        PathString.Setup(Separator.Backslash);

        PathString path = @"one\two/three";
        Assert.That(path, Is.EqualTo(@"one\two\three"));

        path = @"other/root\file.txt";
        Assert.That(path, Is.EqualTo(@"other\root\file.txt"));

        path += "/foo";
        Assert.That(path, Is.EqualTo(@"other\root\file.txt\foo"));
    }

    [Test]
    public void Setup_CanBeCalledMultipleTimes_AndUsesLatestSeparator()
    {
        PathString.Setup(Separator.Slash);
        PathString.Setup(Separator.Backslash);

        PathString path = @"one/two\three";

        Assert.That(path, Is.EqualTo(@"one\two\three"));
    }

    [Test]
    public void GetValue_With_MultipleMethods()
    {
        PathString.Setup(Separator.Slash);

        PathString path = @"one/two\three";
        Assert.That(path, Is.EqualTo("one/two/three"));
        Assert.That(path.Value, Is.EqualTo("one/two/three"));
        Assert.That(path.ToString(), Is.EqualTo("one/two/three"));
    }

    [Test]
    public void SetValue_With_MultipleMethods()
    {
        PathString.Setup(Separator.Slash);

        PathString path1 = @"one/two\three";
        PathString path2 = new PathString { Value = @"one\two\three" };
        PathString path3 = new PathString(@"one\two\three");

        Assert.That(path1, Is.EqualTo("one/two/three"));
        Assert.That(path2, Is.EqualTo("one/two/three"));
        Assert.That(path3, Is.EqualTo("one/two/three"));
    }
}
