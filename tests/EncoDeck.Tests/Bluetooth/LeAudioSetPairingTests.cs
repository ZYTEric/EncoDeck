// SPDX-License-Identifier: GPL-3.0-only
using EncoDeck.Bluetooth.LeAudio;
using Windows.Devices.Enumeration;

namespace EncoDeck.Tests.Bluetooth;

public class LeAudioSetPairingTests
{
    private const ulong Primary = 0x112233445566;
    private const ulong Secondary = 0x11223344A1B2;

    [Fact]
    public async Task FailedEncryptedMember_RetriesSameEndpointWithDefaultProtection()
    {
        var levels = new List<DevicePairingProtectionLevel>();
        var result = await LeAudioSetPairing.PairMemberAsync(level =>
        {
            levels.Add(level);
            return Task.FromResult(level == DevicePairingProtectionLevel.Encryption
                ? DevicePairingResultStatus.Failed : DevicePairingResultStatus.Paired);
        }, DevicePairingProtectionLevel.Encryption, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(DevicePairingResultStatus.Paired, result);
        Assert.Equal([DevicePairingProtectionLevel.Encryption, DevicePairingProtectionLevel.None], levels);
    }

    [Theory]
    [InlineData(DevicePairingResultStatus.Paired)]
    [InlineData(DevicePairingResultStatus.AlreadyPaired)]
    [InlineData(DevicePairingResultStatus.OperationAlreadyInProgress)]
    [InlineData(DevicePairingResultStatus.AuthenticationFailure)]
    public async Task CompletedOrUnresolvedMember_DoesNotSubmitFallback(DevicePairingResultStatus status)
    {
        int calls = 0;
        var result = await LeAudioSetPairing.PairMemberAsync(_ =>
        {
            calls++;
            return Task.FromResult(status);
        }, DevicePairingProtectionLevel.Encryption, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(status, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task CancelledAfterFailure_DoesNotStartAnotherPairing()
    {
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LeAudioSetPairing.PairMemberAsync(_ =>
        {
            calls++;
            cancellation.Cancel();
            return Task.FromResult(DevicePairingResultStatus.Failed);
        }, DevicePairingProtectionLevel.Encryption, _ => { }, cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NativeVectorWithoutIterable_IsReadWithoutRequestingAnEnumerator()
    {
        var nativeLike = new IndexOnlyVector<string>(["expected-peer", "other-member"]);
        Assert.Equal(["expected-peer", "other-member"], LeAudioSetPairing.SnapshotByIndex(nativeLike));
        Assert.Equal(0, nativeLike.EnumerationAttempts);
        Assert.Equal(2, nativeLike.IndexReads);
    }

    [Fact]
    public void EmptyNativeVector_DoesNotRequestEnumeratorOrReadAnElement()
    {
        var nativeLike = new IndexOnlyVector<string>([]);
        Assert.Empty(LeAudioSetPairing.SnapshotByIndex(nativeLike));
        Assert.Equal(0, nativeLike.EnumerationAttempts);
        Assert.Equal(0, nativeLike.IndexReads);
    }

    private sealed class IndexOnlyVector<T>(T[] values) : IReadOnlyList<T>
    {
        public int EnumerationAttempts { get; private set; }
        public int IndexReads { get; private set; }
        public int Count => values.Length;
        public T this[int index] { get { IndexReads++; return values[index]; } }
        public IEnumerator<T> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidCastException("Native vector exposes GetAt but not IIterable (E_NOINTERFACE).");
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData("BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-11:22:33:44:a1:b2", "AdvertisedPeer")]
    [InlineData("bluetoothle#bluetoothleaa:bb:cc:dd:ee:ff-11:22:33:44:A1:B2", "AdvertisedPeer")]
    // Enco X3: the other earbud's LE Audio identity is not the scan-response address.
    [InlineData("BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-11:22:33:44:a1:b3", "SetMember")]
    [InlineData("BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-5a:bb:cc:44:a1:b2", "SetMember")]
    [InlineData("Bluetooth#Bluetoothaa:bb:cc:dd:ee:ff-11:22:33:44:a1:b2", "Rejected")]
    [InlineData("BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-11:22:33:44:55:66", "Rejected")]
    [InlineData("BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-11:22:33:44:a1", "Rejected")]
    [InlineData("OPPO Enco X4", "Rejected")]
    [InlineData("", "Rejected")]
    public void SetMembers_AdvertisedPeerPreferred_OtherLeMembersAccepted_PrimaryAndClassicRejected(string id, string expected)
    {
        Assert.Equal(expected, LeAudioSetPairing.Classify(id, Primary, Secondary).ToString());
    }

    [Fact]
    public void WithoutAHint_MembersAreStillClassifiedByAddress()
    {
        const string id = "BluetoothLE#BluetoothLEaa:bb:cc:dd:ee:ff-11:22:33:44:a1:b2";
        Assert.Equal(LeAudioSetPairing.MemberKind.SetMember, LeAudioSetPairing.Classify(id, Primary, 0));
        Assert.Equal(LeAudioSetPairing.MemberKind.Rejected, LeAudioSetPairing.Classify(id, Secondary, Secondary));
    }
}
