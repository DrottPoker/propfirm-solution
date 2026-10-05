using System.Net;

using Prop.Api.Net;

namespace Prop.Api.Tests;

/// <summary>Calls to addresses a firm chose reach only the public internet (ADR 0044).</summary>
public sealed class PublicAddressesTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("104.16.0.1", true)]
    [InlineData("2606:4700::1111", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.0.2.10", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2002:a00:1::", false)]
    [InlineData("2002:808:808::", true)]
    public void OnlyPublicAddressesAreCalled(string address, bool isPublic) =>
        Assert.Equal(isPublic, PublicAddresses.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("hooks.firm.example", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("[::1]", false)]
    [InlineData("127.0.0.1", false)]
    [InlineData("localhost", false)]
    [InlineData("portal.localhost", false)]
    [InlineData("printer.local", false)]
    [InlineData("db.internal", false)]
    [InlineData("intranet", false)]
    public void ANameIsCheckedWhenItIsCalled(string host, bool mayBeCalled) =>
        Assert.Equal(mayBeCalled, PublicAddresses.MayBeCalled(host));

    [Fact]
    public async Task AHostThatLeadsIntoOurNetworkIsNotCalled()
    {
        using var client = new HttpClient(PublicAddresses.CreateHandler());

        var refused = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PostAsync(new Uri("https://localhost:1/hooks"), new StringContent("{}"), TestContext.Current.CancellationToken));

        Assert.Contains("localhost is not on the public internet", refused.Message + refused.InnerException?.Message, StringComparison.Ordinal);
    }
}
