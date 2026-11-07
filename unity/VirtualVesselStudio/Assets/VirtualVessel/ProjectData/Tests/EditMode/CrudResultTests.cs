using System;
using System.IO;
using NUnit.Framework;
using VirtualVessel.ProjectData.Records;

namespace VirtualVessel.ProjectData.Tests
{
    /// <summary>The CRUD results every module returns (system design 6.31).</summary>
    public sealed class CrudResultTests
    {
        [Test]
        public void StorageFailures_AreReported_AndBugsStillThrow()
        {
            CrudResult failed = CrudResult.Run(() => throw new IOException("disk"), "Saving x");
            Assert.That(failed.Ok, Is.False);
            Assert.That(failed.Error, Is.EqualTo(CrudError.StorageFailed));
            Assert.That(failed.Detail, Is.EqualTo("Saving x: disk"));
            Assert.That(CrudResult.Run(() => throw new UnauthorizedAccessException("no"), "x").Error, Is.EqualTo(CrudError.StorageFailed));
            Assert.Throws<ArgumentNullException>(() => CrudResult.Run(() => throw new ArgumentNullException("x"), "x"));
            Assert.That(CrudResult.Run(() => { }, "x").Ok, Is.True);
        }

        [Test]
        public void Values_ComeBackOnSuccess_AndFailuresPassOn()
        {
            CrudResult<int> ok = CrudResult<int>.Run(() => 3, "x");
            Assert.That((ok.Ok, ok.Value), Is.EqualTo((true, 3)));
            CrudResult<string> passed = CrudResult<string>.From(CrudResult.Fail(CrudError.Busy, "making"));
            Assert.That((passed.Error, passed.Detail), Is.EqualTo((CrudError.Busy, "making")));
            Assert.That(CrudResult<int>.Fail(CrudError.NotFound, "x").WithoutValue.Error, Is.EqualTo(CrudError.NotFound));
            Assert.Throws<ArgumentException>(() => CrudResult.Fail(CrudError.None, "x"));
        }
    }
}
