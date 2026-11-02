using System.Reflection;
using Xunit;

namespace S7CommPlusDriver.Tests
{
    public sealed class StrongNameTests
    {
        [Fact]
        public void DriverAndFriendAssemblyUseConfiguredSigning()
        {
            var driver = typeof(S7CommPlusClient).Assembly.GetName();
            var tests = typeof(StrongNameTests).Assembly.GetName();
#if S7COMMPLUS_STRONG_NAME
            Assert.NotEmpty(driver.GetPublicKeyToken()!);
            Assert.Equal(driver.GetPublicKey(), tests.GetPublicKey());
            foreach (AssemblyName reference in typeof(S7CommPlusClient).Assembly.GetReferencedAssemblies())
                Assert.NotEmpty(reference.GetPublicKeyToken()!);
#else
            Assert.Empty(driver.GetPublicKeyToken()!);
            Assert.Empty(tests.GetPublicKeyToken()!);
#endif
        }
    }
}
