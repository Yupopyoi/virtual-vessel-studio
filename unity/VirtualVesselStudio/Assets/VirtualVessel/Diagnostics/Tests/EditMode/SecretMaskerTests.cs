using NUnit.Framework;
using VirtualVessel.Diagnostics.Logging.Pipeline;

namespace VirtualVessel.Diagnostics.Tests
{
    public sealed class SecretMaskerTests
    {
        [TestCase("Authorization: Bearer eyJhbGciOi.secret.value", "eyJhbGciOi")]
        [TestCase("stream key = abcd-1234-efgh", "abcd-1234-efgh")]
        [TestCase("streamKey=abcd1234", "abcd1234")]
        [TestCase("password: hunter2", "hunter2")]
        [TestCase("userPassword=hunter2", "hunter2")]
        [TestCase("api_key=AKIA12345", "AKIA12345")]
        [TestCase("{\"access_token\":\"tok-123\"}", "tok-123")]
        [TestCase("client_secret=s3cr3t&grant=x", "s3cr3t")]
        [TestCase("Publishing to rtmps://a.rtmps.youtube.com/live2/xxxx-yyyy-zzzz-wwww", "xxxx-yyyy-zzzz-wwww")]
        public void MaskText_RemovesSecretValue(string input, string secret)
        {
            string masked = SecretMasker.MaskText(input);

            Assert.That(masked, Does.Not.Contain(secret));
            Assert.That(masked, Does.Contain(SecretMasker.Mask));
        }

        [TestCase("Avatar loaded.")]
        [TestCase("Tokenizer initialized with 512 entries.")]
        [TestCase("Bitrate = 6000")]
        public void MaskText_LeavesOrdinaryTextUnchanged(string input)
        {
            Assert.That(SecretMasker.MaskText(input), Is.EqualTo(input));
        }

        [TestCase("StreamKey", true)]
        [TestCase("stream_key", true)]
        [TestCase("Password", true)]
        [TestCase("ApiKey", true)]
        [TestCase("refresh-token", true)]
        [TestCase("ClientSecret", true)]
        [TestCase("Bitrate", false)]
        [TestCase("DatasetId", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void IsSensitiveKey(string key, bool expected)
        {
            Assert.That(SecretMasker.IsSensitiveKey(key), Is.EqualTo(expected));
        }
    }
}
