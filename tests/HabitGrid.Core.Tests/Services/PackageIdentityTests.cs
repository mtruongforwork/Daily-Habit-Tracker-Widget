using FluentAssertions;
using HabitGrid.Core.Services;
using Xunit;

namespace HabitGrid.Core.Tests.Services;

public class PackageIdentityTests
{
    [Fact]
    public void TestHost_IsNotPackaged()
    {
        PackageIdentity.IsPackaged.Should().BeFalse("test host chạy không đóng gói");
        PackageIdentity.FullName.Should().BeNull();
    }
}
