using System;
using System.IO;
using GitHub.Runner.Sdk;
using Xunit;

namespace GitHub.Runner.Common.Tests.Util
{
    public sealed class NetRcUtilL0 : IDisposable
    {
        private readonly string _tempDirectory;

        public NetRcUtilL0()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), $"netrc_{Path.GetRandomFileName()}");
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private string WriteNetRc(string content)
        {
            var filePath = Path.Combine(_tempDirectory, Path.GetRandomFileName());
            File.WriteAllText(filePath, content);
            return filePath;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_MultiLineEntry()
        {
            var filePath = WriteNetRc(@"
machine internal.cache
  login builder
  password hunter2
");

            var credential = NetRcUtil.GetCredential(filePath, "internal.cache");

            Assert.NotNull(credential);
            Assert.Equal("builder", credential.Login);
            Assert.Equal("hunter2", credential.Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_SingleLineEntries()
        {
            var filePath = WriteNetRc(@"machine one.example.com login a password b machine two.example.com login c password d");

            var one = NetRcUtil.GetCredential(filePath, "one.example.com");
            var two = NetRcUtil.GetCredential(filePath, "two.example.com");

            Assert.Equal("a", one.Login);
            Assert.Equal("b", one.Password);
            Assert.Equal("c", two.Login);
            Assert.Equal("d", two.Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_DefaultEntryFallback()
        {
            var filePath = WriteNetRc(@"
machine one.example.com login a password b
default login fallback password everywhere
");

            var credential = NetRcUtil.GetCredential(filePath, "unlisted.example.com");

            Assert.NotNull(credential);
            Assert.Equal("fallback", credential.Login);
            Assert.Equal("everywhere", credential.Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_UnknownHostWithoutDefault_ReturnsNull()
        {
            var filePath = WriteNetRc(@"machine one.example.com login a password b");

            Assert.Null(NetRcUtil.GetCredential(filePath, "unlisted.example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_HostMatchIsCaseInsensitive()
        {
            var filePath = WriteNetRc(@"machine Internal.Cache login a password b");

            Assert.NotNull(NetRcUtil.GetCredential(filePath, "internal.cache"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_FirstEntryWins()
        {
            var filePath = WriteNetRc(@"
machine one.example.com login first password firstpw
machine one.example.com login second password secondpw
");

            var credential = NetRcUtil.GetCredential(filePath, "one.example.com");

            Assert.Equal("first", credential.Login);
            Assert.Equal("firstpw", credential.Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_MacroBodyIsSkipped()
        {
            var filePath = WriteNetRc(@"
macdef init
machine bogus.example.com login trap password trap

machine real.example.com login a password b
");

            Assert.Null(NetRcUtil.GetCredential(filePath, "bogus.example.com"));
            Assert.NotNull(NetRcUtil.GetCredential(filePath, "real.example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_EntryWithoutPassword_ReturnsNull()
        {
            var filePath = WriteNetRc(@"machine one.example.com login a");

            Assert.Null(NetRcUtil.GetCredential(filePath, "one.example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_MissingFile_ReturnsNull()
        {
            Assert.Null(NetRcUtil.GetCredential(Path.Combine(_tempDirectory, "does-not-exist"), "one.example.com"));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_CommentsDoNotOverrideActiveEntries()
        {
            var filePath = WriteNetRc(@"
# machine internal.cache login obsolete password obsolete
machine internal.cache login builder password correct # password obsolete
# macdef ignored
machine other.cache login other password otherpw
");

            var credential = NetRcUtil.GetCredential(filePath, "internal.cache");
            Assert.Equal("builder", credential.Login);
            Assert.Equal("correct", credential.Password);
            Assert.Equal("otherpw", NetRcUtil.GetCredential(filePath, "other.cache").Password);
        }

        [Theory]
        [InlineData("plain#password", "plain#password")]
        [InlineData("\"two words\"", "two words")]
        [InlineData("\"two # words\"", "two # words")]
        [InlineData("\"quote\\\"slash\\\\line\\nreturn\\rtab\\t\"", "quote\"slash\\line\nreturn\rtab\t")]
        [InlineData("\"\"", "")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_PreservesPasswordCharacters(string encodedPassword, string expectedPassword)
        {
            var filePath = WriteNetRc($"machine internal.cache login \"build user\" password {encodedPassword}\n");

            var credential = NetRcUtil.GetCredential(filePath, "internal.cache");

            Assert.Equal("build user", credential.Login);
            Assert.Equal(expectedPassword, credential.Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void GetCredential_ValuesMayFollowOnTheNextLine()
        {
            var filePath = WriteNetRc("machine\ninternal.cache\nlogin\nbuilder\npassword\ncorrect\n");

            var credential = NetRcUtil.GetCredential(filePath, "internal.cache");

            Assert.Equal("builder", credential.Login);
            Assert.Equal("correct", credential.Password);
        }

        [Theory]
        [InlineData("\"unterminated")]
        [InlineData("\"unterminated\n")]
        [InlineData("\"trailing\\")]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ReadCredentials_MalformedFileDoesNotReturnPartialCredentials(string password)
        {
            var filePath = WriteNetRc($"default login fallback password fallbackpw\nmachine internal.cache login builder password {password}");

            Assert.Empty(NetRcUtil.ReadCredentials(filePath));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ReadCredentials_KeepsASnapshotUntilReadAgain()
        {
            var filePath = WriteNetRc("machine internal.cache login builder password original");
            var credentials = NetRcUtil.ReadCredentials(filePath);
            File.WriteAllText(filePath, "machine internal.cache login builder password rotated");

            Assert.Equal("original", NetRcUtil.GetCredential(credentials, "internal.cache").Password);
            Assert.Equal("rotated", NetRcUtil.GetCredential(filePath, "internal.cache").Password);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void ResolveFilePath_HonorsNetRcEnvironmentVariable()
        {
            var filePath = WriteNetRc(@"machine one.example.com login a password b");
            var originalValue = Environment.GetEnvironmentVariable("NETRC");
            try
            {
                Environment.SetEnvironmentVariable("NETRC", filePath);
                Assert.Equal(filePath, NetRcUtil.ResolveFilePath());

                Environment.SetEnvironmentVariable("NETRC", Path.Combine(_tempDirectory, "does-not-exist"));
                Assert.Null(NetRcUtil.ResolveFilePath());
            }
            finally
            {
                Environment.SetEnvironmentVariable("NETRC", originalValue);
            }
        }
    }
}
