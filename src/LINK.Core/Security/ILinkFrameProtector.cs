namespace Link.Core.Security;

/// <summary>
/// Chiffrement authentifié du corps des paquets v2 (session LINK).
/// Implémentation : <see cref="LinkSessionCipher"/>.
/// </summary>
public interface ILinkFrameProtector
{
    /// <summary>Octets ajoutés au corps chiffré (compteur + tag).</summary>
    int Overhead { get; }

    /// <summary>Chiffre <paramref name="plaintext"/>. <paramref name="header"/> = 6 octets d'en-tête (déjà renseignés, longueur incluse).</summary>
    byte[] Protect(ReadOnlySpan<byte> header, ReadOnlySpan<byte> plaintext);

    /// <summary>Déchiffre et vérifie un corps. Retourne null si invalide ou rejoué.</summary>
    byte[]? Unprotect(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body);
}
