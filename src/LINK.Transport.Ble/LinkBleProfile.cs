namespace Link.Transport.Ble;

/// <summary>
/// Profil GATT transportant le flux d'octets LINK (trames COBS + 0x00).
/// </summary>
/// <param name="Name">Nom lisible.</param>
/// <param name="ServiceUuid">Service à découvrir.</param>
/// <param name="RxCharacteristicUuid">Caractéristique écrite par le client (vers le device).</param>
/// <param name="TxCharacteristicUuid">Caractéristique notifiée par le device (vers le client).</param>
public sealed record LinkBleProfile(string Name, Guid ServiceUuid, Guid RxCharacteristicUuid, Guid TxCharacteristicUuid)
{
    /// <summary>Service LINK natif (docs/LINK_Protocol_v2.md §6), ex. STM32WB.</summary>
    public static LinkBleProfile Link { get; } = new(
        "LINK",
        Guid.Parse("6c696e6b-0200-4c4b-a000-000000000001"),
        Guid.Parse("6c696e6b-0200-4c4b-a000-000000000002"),
        Guid.Parse("6c696e6b-0200-4c4b-a000-000000000003"));

    /// <summary>Nordic UART Service (nRF52, ESP32 « BLE UART », nombreux modules).</summary>
    public static LinkBleProfile NordicUart { get; } = new(
        "NUS",
        Guid.Parse("6e400001-b5a3-f393-e0a9-e50e24dcca9e"),
        Guid.Parse("6e400002-b5a3-f393-e0a9-e50e24dcca9e"),
        Guid.Parse("6e400003-b5a3-f393-e0a9-e50e24dcca9e"));

    /// <summary>Modules HM-10 / CC2541 / JDY (une seule caractéristique FFE1).</summary>
    public static LinkBleProfile Hm10 { get; } = new(
        "HM-10",
        Guid.Parse("0000ffe0-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"),
        Guid.Parse("0000ffe1-0000-1000-8000-00805f9b34fb"));

    public static IReadOnlyList<LinkBleProfile> All { get; } = new[] { Link, NordicUart, Hm10 };

    /// <summary>MTU demandé (247 = 244 octets utiles par paquet en BLE 4.2+).</summary>
    public const int PreferredMtu = 247;
}
