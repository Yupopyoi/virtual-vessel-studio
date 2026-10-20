using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VirtualVessel.Avatar.Tests
{
    public sealed class AvatarCoreTests
    {
        [Test]
        public void AvatarBones_MatchUnityHumanBodyBones_ByNameAndValue()
        {
            foreach (AvatarBone bone in Enum.GetValues(typeof(AvatarBone)))
            {
                Assert.That(((HumanBodyBones)(int)bone).ToString(), Is.EqualTo(bone.ToString()), "Loaders convert by casting.");
            }

            Assert.That(AvatarBones.Count, Is.EqualTo((int)HumanBodyBones.LastBone));
        }

        [Test]
        public void MissingRequired_ListsOnlyRequiredBonesTheModelLacks()
        {
            Assert.That(AvatarBones.MissingRequired(_ => true), Is.Empty);
            Assert.That(
                AvatarBones.MissingRequired(bone => bone != AvatarBone.Head && bone != AvatarBone.LeftHand),
                Is.EqualTo(new[] { AvatarBone.Head, AvatarBone.LeftHand }));

            // Chest and neck are optional in VRM 1.0.
            Assert.That(AvatarBones.MissingRequired(bone => bone != AvatarBone.Chest && bone != AvatarBone.Neck), Is.Empty);
        }

        [Test]
        public void ChildCandidates_FollowTheChains_AndSkipOptionalBones()
        {
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.LeftUpperArm), Is.EqualTo(new[] { AvatarBone.LeftLowerArm }));
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.Spine).First(), Is.EqualTo(AvatarBone.Chest));
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.Spine), Does.Contain(AvatarBone.Head), "Used when chest and neck are missing.");
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.RightIndexProximal), Is.EqualTo(new[] { AvatarBone.RightIndexIntermediate }));
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.RightIndexDistal), Is.Empty);
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.LeftThumbIntermediate), Is.EqualTo(new[] { AvatarBone.LeftThumbDistal }));
            Assert.That(AvatarBones.ChildCandidates(AvatarBone.Head), Is.Empty);
        }

        [TestCase(".vrm", 100L, true, AvatarProblem.None)]
        [TestCase(".VRM", 100L, true, AvatarProblem.None)]
        [TestCase(".glb", 100L, true, AvatarProblem.NotVrm)]
        [TestCase(".vrm", 100L, false, AvatarProblem.NotVrm)]
        [TestCase(".vrm", AvatarFileCheck.MaxBytes + 1, true, AvatarProblem.TooLarge)]
        public void FileCheck_ChecksExtensionSizeAndMagic(string extension, long length, bool glb, AvatarProblem expected)
        {
            byte[] header = glb ? new byte[] { 0x67, 0x6C, 0x54, 0x46 } : new byte[] { 0x50, 0x4B, 0x03, 0x04 };
            Assert.That(AvatarFileCheck.Check(extension, length, header, new[] { ".vrm" }), Is.EqualTo(expected));
        }

        [Test]
        public void FileCheck_AShortFile_IsNotVrm()
        {
            Assert.That(AvatarFileCheck.Check(".vrm", 2, new byte[] { 0x67, 0x6C }, new[] { ".vrm" }), Is.EqualTo(AvatarProblem.NotVrm));
            Assert.That(AvatarFileCheck.Check(".vrm", 0, null, new[] { ".vrm" }), Is.EqualTo(AvatarProblem.NotVrm));
        }

        [Test]
        public void Ids_AreGuidsInTheNFormat_AndNothingElseIsAnId()
        {
            Assert.That(AvatarStore.IsId(AvatarStore.NewId()), Is.True);
            Assert.That(AvatarStore.IsId(".importing"), Is.False);
            Assert.That(AvatarStore.IsId(Guid.NewGuid().ToString("D")), Is.False);
            Assert.That(AvatarStore.IsId("..\\..\\Windows"), Is.False);
            Assert.That(AvatarStore.IsId(null), Is.False);
        }

        [Test]
        public void FormatNames_AndThumbnailNames_FromAProfile_CannotLeaveTheFolder()
        {
            Assert.That(AvatarStore.IsFormatName("vrm"), Is.True);
            Assert.That(AvatarStore.IsFormatName("..\\x"), Is.False);
            Assert.That(AvatarStore.IsFormatName("VRM"), Is.False);
            Assert.That(AvatarStore.IsFormatName(string.Empty), Is.False);
            Assert.That(AvatarStore.ThumbnailPath("C:\\a", "thumbnail.png"), Is.EqualTo("C:\\a\\thumbnail.png"));
            Assert.That(AvatarStore.ThumbnailPath("C:\\a", "..\\thumbnail.png"), Is.Null);
            Assert.That(AvatarStore.ThumbnailPath("C:\\a", "model.vrm"), Is.Null);
            Assert.That(AvatarStore.ThumbnailPath("C:\\a", null), Is.Null);
        }

        [TestCase("image/png", ".png")]
        [TestCase("image/jpeg", ".jpg")]
        [TestCase("IMAGE/PNG", ".png")]
        [TestCase("image/ktx2", null)]
        [TestCase(null, null)]
        public void ThumbnailExtension_ComesFromTheMimeType(string mimeType, string expected)
        {
            Assert.That(AvatarThumbnail.ExtensionOf(mimeType), Is.EqualTo(expected));
        }

        [Test]
        public void License_NeedsANotice_UnlessEveryoneMayUseIt()
        {
            Assert.That(new AvatarLicense { AvatarPermission = AvatarPermission.Everyone }.NeedsNotice, Is.False);
            Assert.That(new AvatarLicense { AvatarPermission = AvatarPermission.OnlyAuthor }.NeedsNotice, Is.True);
            Assert.That(new AvatarLicense { AvatarPermission = AvatarPermission.OnlySeparatelyLicensedPerson }.NeedsNotice, Is.True);
            Assert.That(new AvatarLicense().NeedsNotice, Is.True, "Not stated counts as not open to everyone.");
        }

        [Test]
        public void CreateProfile_TakesTheModelsNameAndConditions()
        {
            var license = new AvatarLicense { AvatarPermission = AvatarPermission.Everyone, Authors = { "Author" } };
            var info = new AvatarModelInfo("  Model  ", "vrm", "0.x", true, license, new AvatarThumbnail(new byte[] { 1 }, ".jpg"));
            AvatarProfile profile = AvatarImporter.CreateProfile("id", info, "file.vrm", "abc", new DateTimeOffset(2026, 10, 20, 21, 0, 0, TimeSpan.FromHours(9)));

            Assert.That(profile.DisplayName, Is.EqualTo("Model"));
            Assert.That(profile.FormatVersion, Is.EqualTo("0.x"));
            Assert.That(profile.Converted, Is.True);
            Assert.That(profile.ImportedAt, Is.EqualTo("2026-10-20T12:00:00Z"));
            Assert.That(profile.Thumbnail, Is.EqualTo("thumbnail.jpg"));
            Assert.That(profile.License.Authors, Is.EqualTo(new[] { "Author" }));
            Assert.That(profile.License, Is.Not.SameAs(license), "A copy, so later changes do not reach the model information.");
        }

        [Test]
        public void CreateProfile_WithoutAName_UsesTheFileName()
        {
            var info = new AvatarModelInfo(" ", "vrm", "1.0", false, null, null);
            AvatarProfile profile = AvatarImporter.CreateProfile("id", info, "My Avatar.vrm", "abc", DateTimeOffset.UnixEpoch);

            Assert.That(profile.DisplayName, Is.EqualTo("My Avatar"));
            Assert.That(profile.Thumbnail, Is.Null);
        }

        [Test]
        public void ProblemOf_MapsExceptionsToWhatTheUserIsTold()
        {
            Assert.That(AvatarImporter.ProblemOf(new AvatarLoadException(AvatarProblem.MissingBones, "x")), Is.EqualTo(AvatarProblem.MissingBones));
            Assert.That(AvatarImporter.ProblemOf(new System.IO.IOException()), Is.EqualTo(AvatarProblem.StorageFailed));
            Assert.That(AvatarImporter.ProblemOf(new UnauthorizedAccessException()), Is.EqualTo(AvatarProblem.StorageFailed));
            Assert.That(AvatarImporter.ProblemOf(new InvalidOperationException()), Is.EqualTo(AvatarProblem.LoadFailed));
        }
    }
}
