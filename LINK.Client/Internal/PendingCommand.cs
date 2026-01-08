using Link.Core.Frames;

namespace Link.Client.Internal;

internal sealed class PendingCommand
{
    public string Command { get; }
    public TaskCompletionSource<LinkFrame> Tcs { get; }

    public PendingCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            throw new ArgumentException(nameof(command));

        Command = command;
        Tcs = new TaskCompletionSource<LinkFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
