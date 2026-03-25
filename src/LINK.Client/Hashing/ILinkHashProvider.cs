namespace Link.Client.Hashing;

public interface ILinkHashProvider
{
    string Algorithm { get; }
    string ComputeHash(string input);
}
