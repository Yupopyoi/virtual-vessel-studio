using System.Linq;
using NUnit.Framework;
using VirtualVessel.ProjectData.Credentials;

namespace VirtualVessel.ProjectData.Tests
{
    /// <summary>
    /// Writes to this user's real Credential Manager, under IDs made for each test and removed after it.
    /// </summary>
    public sealed class WindowsCredentialStoreTests
    {
        private readonly WindowsCredentialStore _store = new WindowsCredentialStore();
        private string _id;

        [SetUp]
        public void SetUp()
        {
            _id = CredentialId.New();
        }

        [TearDown]
        public void TearDown()
        {
            _store.Delete(_id);
        }

        [Test]
        public void Write_ThenRead_ReturnsTheSecret()
        {
            Assert.That(_store.Write(_id, "abcd-efgh-ijkl-mnop-qrst").Status, Is.EqualTo(CredentialStatus.Ok));

            Assert.That(_store.Exists(_id), Is.True);
            Assert.That(_store.Read(_id, out string secret).Status, Is.EqualTo(CredentialStatus.Ok));
            Assert.That(secret, Is.EqualTo("abcd-efgh-ijkl-mnop-qrst"));
        }

        [Test]
        public void Write_KeepsTextOutsideAscii()
        {
            _store.Write(_id, "鍵-キー-\U0001F511");

            _store.Read(_id, out string secret);
            Assert.That(secret, Is.EqualTo("鍵-キー-\U0001F511"));
        }

        [Test]
        public void Write_Again_ReplacesTheSecret()
        {
            _store.Write(_id, "first");
            _store.Write(_id, "second");

            _store.Read(_id, out string secret);
            Assert.That(secret, Is.EqualTo("second"));
        }

        [Test]
        public void Delete_RemovesTheSecret()
        {
            _store.Write(_id, "secret");

            Assert.That(_store.Delete(_id).Status, Is.EqualTo(CredentialStatus.Ok));

            Assert.That(_store.Exists(_id), Is.False);
            Assert.That(_store.Read(_id, out string secret).Status, Is.EqualTo(CredentialStatus.NotFound));
            Assert.That(secret, Is.Null);
        }

        [Test]
        public void Delete_WhatIsNotThere_IsNotFound()
        {
            Assert.That(_store.Delete(_id).Status, Is.EqualTo(CredentialStatus.NotFound));
        }

        [TestCase("")]
        [TestCase(null)]
        public void Write_NothingToKeep_IsInvalidAndWritesNothing(string secret)
        {
            Assert.That(_store.Write(_id, secret).Status, Is.EqualTo(CredentialStatus.Invalid));
            Assert.That(_store.Exists(_id), Is.False);
        }

        [Test]
        public void Write_TheLargestSecret_FitsAndOneMoreByteDoesNot()
        {
            string largest = new string('k', WindowsCredentialStore.MaxSecretBytes);
            Assert.That(_store.Write(_id, largest).Status, Is.EqualTo(CredentialStatus.Ok));
            _store.Read(_id, out string secret);
            Assert.That(secret, Is.EqualTo(largest));

            Assert.That(_store.Write(_id, largest + "k").Status, Is.EqualTo(CredentialStatus.Invalid));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("0123456789abcdef0123456789abcde")]
        [TestCase("0123456789abcdef0123456789abcdef0")]
        [TestCase("../../../../../other/target/name")]
        [TestCase("0123456789abcdef0123456789abcdeg")]
        public void AnIdOfAnotherForm_IsRefusedBeforeItNamesACredential(string id)
        {
            Assert.That(CredentialId.IsValid(id), Is.False);
            Assert.That(_store.Exists(id), Is.False);
            Assert.That(_store.Write(id, "secret").Status, Is.EqualTo(CredentialStatus.Invalid));
            Assert.That(_store.Read(id, out _).Status, Is.EqualTo(CredentialStatus.Invalid));
            Assert.That(_store.Delete(id).Status, Is.EqualTo(CredentialStatus.Invalid));
        }

        [Test]
        public void New_IsAValidIdEachTime()
        {
            string[] ids = Enumerable.Range(0, 8).Select(_ => CredentialId.New()).ToArray();

            Assert.That(ids.All(CredentialId.IsValid), Is.True);
            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length));
        }

        [Test]
        public void TargetName_DoesNotDependOnTheIdsCase()
        {
            string id = "0123456789ABCDEF0123456789ABCDEF";

            Assert.That(WindowsCredentialStore.TargetName(id), Is.EqualTo("VirtualVesselStudio/stream/0123456789abcdef0123456789abcdef"));
        }
    }
}
