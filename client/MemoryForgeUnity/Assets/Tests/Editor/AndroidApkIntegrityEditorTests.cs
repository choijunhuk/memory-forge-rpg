using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MemoryForge.EditorTests
{
    public class AndroidApkIntegrityEditorTests
    {
        const string Data = "assets/bin/Data/";

        [Test]
        public void WholeReflectionDataIsAccepted()
        {
            Assert.DoesNotThrow(() => ValidateArchive(Data + "sharedassets0.assets"));
        }

        [Test]
        public void ConsecutiveSplitDataIsAcceptedInAnyZipOrder()
        {
            var chunks = Enumerable.Range(0, 12).Reverse().Select(i => Data + "sharedassets0.assets.split" + i).ToArray();
            Assert.DoesNotThrow(() => ValidateArchive(chunks));
        }

        [Test]
        public void DifferentFilesCanUseDifferentRepresentations()
        {
            Assert.DoesNotThrow(() => ValidateArchive(Data + "sharedassets0.assets", Data + "resources.assets.split0"));
        }

        [Test]
        public void NonUnityDataEntriesAreIgnored()
        {
            Assert.DoesNotThrow(() => ValidateArchive("other/data.assets", "other/data.assets.split2"));
        }

        [Test]
        public void WholeAndSplitReflectionDataIsRejected()
        {
            var error = Assert.Throws<TargetInvocationException>(() => ValidateArchive(
                Data + "sharedassets0.assets", Data + "sharedassets0.assets.split0", Data + "sharedassets0.assets.split1",
                Data + "sharedassets0.assets.split2", Data + "sharedassets0.assets.split3"));
            Assert.That(error.InnerException, Is.TypeOf<InvalidDataException>());
            Assert.That(error.InnerException.Message, Does.Contain(Data + "sharedassets0.assets"));
        }

        [TestCase(1, 2)]
        [TestCase(0, 2)]
        public void IncompleteSplitSequenceIsRejected(int first, int second)
        {
            var error = Assert.Throws<TargetInvocationException>(() => ValidateArchive(
                Data + "sharedassets0.assets.split" + first, Data + "sharedassets0.assets.split" + second));
            Assert.That(error.InnerException, Is.TypeOf<InvalidDataException>());
            Assert.That(error.InnerException.Message, Does.Contain(Data + "sharedassets0.assets"));
        }

        static void ValidateArchive(params string[] entries)
        {
            var path = Path.Combine(Path.GetTempPath(), "memoryforge-apk-integrity-" + Guid.NewGuid().ToString("N") + ".apk");
            try
            {
                using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                    foreach (var entry in entries) zip.CreateEntry(entry);
                // The test asmdef cannot reference a predefined Editor assembly directly.
                var builder = Type.GetType("MemoryForgeAndroidBuild, Assembly-CSharp-Editor", true);
                var validate = builder.GetMethod("ValidateApk", BindingFlags.Public | BindingFlags.Static);
                Assert.That(validate, Is.Not.Null, "The Android build must expose its actual APK validation boundary.");
                validate.Invoke(null, new object[] { path });
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
