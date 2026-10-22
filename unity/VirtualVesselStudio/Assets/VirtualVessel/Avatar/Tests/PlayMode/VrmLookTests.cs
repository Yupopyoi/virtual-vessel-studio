using NUnit.Framework;
using VirtualVessel.Avatar.Vrm;

namespace VirtualVessel.Avatar.Tests
{
    /// <summary>Gaze as a share of the model's eye range (avatar detailed design 5.2, 7.4).</summary>
    public sealed class VrmLookTests
    {
        [Test]
        public void AShareOfTheRange_IsThatShareOfTheModelsInputRange_UpAndDownApart()
        {
            Assert.That(VrmAvatarExpressions.ToDegrees(1f, 0f, 90f, 60f, 45f), Is.EqualTo((90f, 0f)), "The whole range turns the eyes fully.");
            Assert.That(VrmAvatarExpressions.ToDegrees(-0.5f, 0.5f, 90f, 60f, 45f), Is.EqualTo((-45f, 30f)));
            Assert.That(VrmAvatarExpressions.ToDegrees(0f, -1f, 90f, 60f, 45f), Is.EqualTo((0f, -45f)), "Down uses its own range.");
            Assert.That(VrmAvatarExpressions.ToDegrees(3f, -3f, 90f, 60f, 45f), Is.EqualTo((90f, -45f)), "Beyond the range stays at its end.");
        }
    }
}
